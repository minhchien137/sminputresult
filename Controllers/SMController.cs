using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SMInputProduction.Models;

namespace SMInputProduction.Controllers
{
    public class SMController : Controller
    {
        private readonly ILogger<SMController> _logger;
        private readonly ApplicationDbContext _context;

        public SMController(ILogger<SMController> logger, ApplicationDbContext context)
        {
            _logger  = logger;
            _context = context;
        }

        // ── GET /SM/InputResult ──────────────────────────────────────────────
        public async Task<IActionResult> InputResult()
        {
            var today = DateTime.Now.ToString("yyyyMMdd");

            var operations = await _context.SVN_Target
                .Where(t => t.Date_time == today && t.Operation != null && t.Operation.Contains("SM"))
                .Select(t => t.Operation!)
                .Distinct()
                .OrderBy(o => o)
                .ToListAsync();

            ViewBag.Operations = operations;
            return View("Input");
        }

        // ── POST /SM/SaveResult ──────────────────────────────────────────────
        [HttpPost]
        public async Task<IActionResult> SaveResult([FromBody] SaveResultDto dto)
        {
            if (dto == null)
                return Json(new { success = false, message = "Invalid data." });
            if (string.IsNullOrWhiteSpace(dto.TypeValue))
                return Json(new { success = false, message = "Please select a Type." });
            if (string.IsNullOrWhiteSpace(dto.Operation))
                return Json(new { success = false, message = "Please select an Operation." });
            if (string.IsNullOrWhiteSpace(dto.TimeSlot))
                return Json(new { success = false, message = "Please select a Time slot." });
            if (dto.Quantity == null || dto.Quantity < 0)
                return Json(new { success = false, message = "Quantity cannot be negative." });

            try
            {
                var today       = DateTime.Now.ToString("yyyyMMdd");
                var dbTypeValue = MapTypeToDb(dto.TypeValue);

                // Đọc record hiện tại bằng raw SQL (vì HasNoKey)
                var existing = await _context.SVN_Production_result_Viindoo
                    .FromSqlRaw(@"SELECT TOP 1 * FROM SVN_Production_result_Viindoo
                                  WHERE Type_value = {0} AND Operation = {1} AND Date_time = {2}",
                                  dbTypeValue, dto.Operation, today)
                    .FirstOrDefaultAsync();

                decimal before = 0;
                decimal after  = 0;

                if (existing != null)
                {
                    before = GetTimeValue(existing, dto.TimeSlot);
                    // Defect cộng dồn vào slot, các type khác ghi đè
                    after  = dto.TypeValue == "Defect" ? before + dto.Quantity.Value : dto.Quantity.Value;

                    // UPDATE đúng cột TimeSlot bằng raw SQL
                    var updateSql = $@"UPDATE SVN_Production_result_Viindoo
                                       SET {dto.TimeSlot} = {{0}}
                                       WHERE Type_value = {{1}} AND Operation = {{2}} AND Date_time = {{3}}";
                    await _context.Database.ExecuteSqlRawAsync(updateSql,
                        after.ToString("0.######"), dbTypeValue, dto.Operation, today);
                }
                else
                {
                    before = 0;
                    after  = dto.Quantity.Value;

                    // Tạo values 6 Time, chỉ slot được chọn = after, còn lại = 0
                    var times = new Dictionary<string, decimal>
                    {
                        ["Time1"] = 0, ["Time2"] = 0, ["Time3"] = 0,
                        ["Time4"] = 0, ["Time5"] = 0, ["Time6"] = 0
                    };
                    times[dto.TimeSlot] = after;

                    await _context.Database.ExecuteSqlRawAsync(@"
                        INSERT INTO SVN_Production_result_Viindoo
                            (Type_value, Time1, Time2, Time3, Time4, Time5, Time6,
                             Operation, Date_time, WC, Achieve, Forecast, WORunning, Product, Customer, Shift)
                        VALUES ({0},{1},{2},{3},{4},{5},{6},{7},{8},{9},NULL,NULL,NULL,NULL,NULL,{10})",
                        dbTypeValue,
                        times["Time1"].ToString("0.######"),
                        times["Time2"].ToString("0.######"),
                        times["Time3"].ToString("0.######"),
                        times["Time4"].ToString("0.######"),
                        times["Time5"].ToString("0.######"),
                        times["Time6"].ToString("0.######"),
                        dto.Operation,
                        today,
                        "FG",
                        "day");
                }

                // ── Ghi log history ───────────────────────────────────────────
                var clientIp = HttpContext.Connection.RemoteIpAddress?.ToString();
                _context.SM_InputProductionResultHistory.Add(new SM_InputProductionResultHistory
                {
                    ProductionDate = today,
                    TypeDisplay    = dto.TypeValue,
                    TypeDb         = dbTypeValue,
                    Operation      = dto.Operation,
                    TimeSlot       = dto.TimeSlot,
                    QuantityAdded  = dto.Quantity,
                    QuantityBefore = before,
                    QuantityAfter  = after,
                    WC             = "FG",
                    Shift          = "day",
                    CreatedAt      = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")),
                    ClientIp       = clientIp,
                    Description    = dto.Description
                });
                await _context.SaveChangesAsync();

                // Nếu type là Defect thì upsert vào SVN_Defect_Record
                if (dto.TypeValue == "Defect")
                {
                    var existingDefect = await _context.SVN_Defect_Record
                        .FromSqlRaw(@"SELECT TOP 1 * FROM SVN_Defect_Record
                                      WHERE Operation = {0} AND INSDatetime = {1} AND Defect_Code = 'R01'",
                                      dto.Operation, today)
                        .FirstOrDefaultAsync();

                    if (existingDefect != null)
                    {
                        var currentQty = decimal.TryParse(existingDefect.Qty_NG, out var q) ? q : 0;
                        var newQty     = currentQty + dto.Quantity.Value;
                        await _context.Database.ExecuteSqlRawAsync(
                            @"UPDATE SVN_Defect_Record SET Qty_NG = {0}
                              WHERE Operation = {1} AND INSDatetime = {2} AND Defect_Code = 'R01'",
                            newQty.ToString("0.######"), dto.Operation, today);
                    }
                    else
                    {
                        await _context.Database.ExecuteSqlRawAsync(@"
                            INSERT INTO SVN_Defect_Record
                                (Item_code, Defect_Code, Qty_NG, INSDatetime, Operation, Employer_code, Employer_name)
                            VALUES ({0}, 'R01', {1}, {2}, {3}, NULL, NULL)",
                            dto.Operation,
                            dto.Quantity.Value.ToString("0.######"),
                            today,
                            dto.Operation);
                    }
                }

                return Json(new { success = true, message = "Saved successfully!" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SaveResult error");
                return Json(new { success = false, message = $"Error: {ex.Message}" });
            }
        }

        // ── GET /SM/GetCurrentValues ─────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetCurrentValues(string operation, string typeValue)
        {
            var today       = DateTime.Now.ToString("yyyyMMdd");
            var dbTypeValue = MapTypeToDb(typeValue);

            var record = await _context.SVN_Production_result_Viindoo
                .FromSqlRaw(@"SELECT TOP 1 * FROM SVN_Production_result_Viindoo
                              WHERE Type_value = {0} AND Operation = {1} AND Date_time = {2}",
                              dbTypeValue, operation, today)
                .FirstOrDefaultAsync();

            if (record == null)
                return Json(new { time1 = 0m, time2 = 0m, time3 = 0m, time4 = 0m, time5 = 0m, time6 = 0m });

            return Json(new
            {
                time1 = ParseDecimal(record.Time1),
                time2 = ParseDecimal(record.Time2),
                time3 = ParseDecimal(record.Time3),
                time4 = ParseDecimal(record.Time4),
                time5 = ParseDecimal(record.Time5),
                time6 = ParseDecimal(record.Time6),
            });
        }

        // ── GET /SM/InputHistory ─────────────────────────────────────────────
        public IActionResult InputHistory() => View("History");

        // ── GET /SM/GetInputHistory ──────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetInputHistory(
            string? typeDisplay,
            string? operation,
            string? timeSlot,
            string? dateFrom,
            string? dateTo)
        {
            try
            {
                var query = _context.SM_InputProductionResultHistory.AsQueryable();

                if (!string.IsNullOrEmpty(typeDisplay))
                    query = query.Where(x => x.TypeDisplay == typeDisplay);
                if (!string.IsNullOrEmpty(operation))
                    query = query.Where(x => x.Operation == operation);
                if (!string.IsNullOrEmpty(timeSlot))
                    query = query.Where(x => x.TimeSlot == timeSlot);
                if (!string.IsNullOrEmpty(dateFrom))
                    query = query.Where(x => string.Compare(x.ProductionDate, dateFrom.Replace("-", "")) >= 0);
                if (!string.IsNullOrEmpty(dateTo))
                    query = query.Where(x => string.Compare(x.ProductionDate, dateTo.Replace("-", "")) <= 0);

                var data = await query
                    .OrderByDescending(x => x.CreatedAt)
                    .Select(x => new {
                        x.Id,
                        x.ProductionDate,
                        x.TypeDisplay,
                        x.TypeDb,
                        x.Operation,
                        x.TimeSlot,
                        x.QuantityAdded,
                        x.QuantityBefore,
                        x.QuantityAfter,
                        x.WC,
                        x.Shift,
                        x.CreatedAt,
                        x.ClientIp,
                        x.Description
                    })
                    .ToListAsync();

                return Json(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetInputHistory error");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── GET /SM/GetOperationsForFilter ───────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetOperationsForFilter()
        {
            var ops = await _context.SM_InputProductionResultHistory
                .Where(x => x.Operation != null)
                .Select(x => x.Operation!)
                .Distinct()
                .OrderBy(x => x)
                .ToListAsync();
            return Json(ops);
        }

        // ── GET /SM/Report ────────────────────────────────────────────────────────────────
        public IActionResult Report() => View("Report");

        // ── GET /SM/GetReportData ─────────────────────────────────────────────────────────
        [HttpGet]
        public async Task<IActionResult> GetReportData(string? date, string? ops)
        {
            try
            {
                var targetDate = string.IsNullOrEmpty(date)
                    ? DateTime.Now.ToString("yyyyMMdd")
                    : date.Replace("-", "");

                var allProd = await _context.SVN_Production_result_Viindoo
                    .Where(p => p.Date_time == targetDate)
                    .ToListAsync();

                var allTgts = await _context.SVN_Target
                    .Where(t => t.Date_time == targetDate)
                    .ToListAsync();

                var availableOps = allProd.Select(p => p.Operation)
                    .Concat(allTgts.Select(t => t.Operation))
                    .Where(o => !string.IsNullOrWhiteSpace(o) && o!.Contains("SM"))
                    .Select(o => o!)
                    .Distinct().OrderBy(o => o).ToList();

                var opSet = string.IsNullOrWhiteSpace(ops)
                    ? null
                    : ops.Split(',', StringSplitOptions.RemoveEmptyEntries)
                         .Select(o => o.Trim()).ToHashSet();

                var filteredOps = (opSet != null && opSet.Count > 0)
                    ? availableOps.Where(o => opSet.Contains(o)).ToList()
                    : availableOps;

                decimal[] Slots(SVN_Production_result_Viindoo? r)
                {
                    if (r == null) return new decimal[5];
                    return new[] { ParseDecimal(r.Time1), ParseDecimal(r.Time2),
                                   ParseDecimal(r.Time3), ParseDecimal(r.Time4),
                                   ParseDecimal(r.Time5) };
                }

                var opResults = filteredOps.Select(op =>
                {
                    var prod = allProd.FirstOrDefault(p => p.Operation == op && p.Type_value == "Production Qty");
                    var ng   = allProd.FirstOrDefault(p => p.Operation == op && p.Type_value == "NG_Qty");
                    var man  = allProd.FirstOrDefault(p => p.Operation == op && p.Type_value == "Man Q'ty");
                    var tgt  = allTgts.FirstOrDefault(t => t.Operation == op);

                    var hourly   = Slots(prod);
                    var ngHourly = Slots(ng);
                    var manHrs   = Slots(man);

                    var actualQty   = hourly.Sum();
                    var activeSlots = hourly.Count(h => h > 0);
                    var targetQty   = tgt?.Daily_plan ?? 0m;
                    var achieveRate = targetQty > 0 ? Math.Round(actualQty / targetQty * 100, 1) : 0m;
                    var ngQty       = ngHourly.Sum();
                    var defectRate  = actualQty > 0 ? Math.Round(ngQty / actualQty * 100, 2) : 0m;
                    var laborArr    = manHrs.Where(h => h > 0).ToArray();
                    var actualLabor = laborArr.Length > 0 ? Math.Round((decimal)laborArr.Average(), 1) : 0m;
                    var targetLabor = tgt?.Labor ?? 0m;
                    var actualUPH   = activeSlots > 0 ? Math.Round(actualQty / activeSlots, 1) : 0m;
                    var targetUPH   = tgt?.UPH ?? 0m;

                    return new
                    {
                        operation = op, actualQty, targetQty, achieveRate,
                        ngQty, defectRate, actualLabor, targetLabor,
                        actualUPH, targetUPH,
                        hourlyData = hourly, ngHourlyData = ngHourly,
                        manHourlyData = manHrs,
                    };
                }).ToList();

                var totActual = opResults.Sum(r => r.actualQty);
                var totTarget = opResults.Sum(r => r.targetQty);
                var totNG     = opResults.Sum(r => r.ngQty);
                var ovAchieve = totTarget > 0 ? Math.Round(totActual / totTarget * 100, 1) : 0m;
                var ovDefect  = totActual > 0 ? Math.Round(totNG / totActual * 100, 2) : 0m;
                var avgAUPH   = opResults.Any() ? Math.Round((decimal)opResults.Average(r => (double)r.actualUPH), 1) : 0m;
                var avgTUPH   = opResults.Any(r => r.targetUPH > 0)
                                ? Math.Round((decimal)opResults.Where(r => r.targetUPH > 0).Average(r => (double)r.targetUPH), 1) : 0m;
                var totALabor = opResults.Sum(r => r.actualLabor);
                var totTLabor = opResults.Sum(r => r.targetLabor);

                return Json(new
                {
                    summary      = new { totActual, totTarget, totNG, ovAchieve, ovDefect,
                                         avgAUPH, avgTUPH, totALabor, totTLabor },
                    operations   = opResults,
                    availableOps,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetReportData error");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string MapTypeToDb(string typeDisplay) => typeDisplay switch
        {
            "Production Qty" => "Production Qty",
            "Defect"         => "NG_Qty",
            "Man Qty"        => "Man Q'ty",
            _                => typeDisplay
        };

        private static decimal GetTimeValue(SVN_Production_result_Viindoo r, string slot) =>
            slot switch
            {
                "Time1" => ParseDecimal(r.Time1),
                "Time2" => ParseDecimal(r.Time2),
                "Time3" => ParseDecimal(r.Time3),
                "Time4" => ParseDecimal(r.Time4),
                "Time5" => ParseDecimal(r.Time5),
                "Time6" => ParseDecimal(r.Time6),
                _ => 0
            };

        private static void SetTimeValue(SVN_Production_result_Viindoo r, string slot, decimal value)
        {
            var s = value.ToString("0.######");
            switch (slot)
            {
                case "Time1": r.Time1 = s; break;
                case "Time2": r.Time2 = s; break;
                case "Time3": r.Time3 = s; break;
                case "Time4": r.Time4 = s; break;
                case "Time5": r.Time5 = s; break;
                case "Time6": r.Time6 = s; break;
            }
        }

        private static decimal ParseDecimal(string? s) =>
            decimal.TryParse(s, out var v) ? v : 0;

        // ── DTOs ─────────────────────────────────────────────────────────────

        public class SaveResultDto
        {
            public string?  TypeValue   { get; set; }
            public string?  Operation   { get; set; }
            public string?  TimeSlot    { get; set; }
            public decimal? Quantity    { get; set; }
            public string?  Description { get; set; }
        }
    }
}