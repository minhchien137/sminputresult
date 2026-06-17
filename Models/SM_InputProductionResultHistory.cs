using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SMInputProduction.Models
{
    /// <summary>
    /// Lưu lịch sử mỗi lần user nhập quantity vào hệ thống Input Result.
    /// Mỗi lần bấm "Lưu" = 1 record mới (append-only log).
    /// </summary>
    [Table("SM_InputProductionResultHistory")]
    public class SM_InputProductionResultHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        /// <summary>Ngày sản xuất, định dạng yyyyMMdd (vd: 20260616)</summary>
        [MaxLength(8)]
        public string? ProductionDate { get; set; }

        /// <summary>
        /// Loại dữ liệu hiển thị (Production Qty / Defect / Man Qty).
        /// Đây là giá trị người dùng chọn trên UI — không phải giá trị lưu DB chính.
        /// </summary>
        [MaxLength(50)]
        public string? TypeDisplay { get; set; }

        /// <summary>
        /// Giá trị thực sự lưu vào SVN_Production_result_Viindoo
        /// (Production Qty / NG_Qty / Man Q'ty)
        /// </summary>
        [MaxLength(50)]
        public string? TypeDb { get; set; }

        /// <summary>Operation (có chứa "SM"), lấy từ SVN_Target</summary>
        [MaxLength(200)]
        public string? Operation { get; set; }

        /// <summary>Khung giờ được chọn: Time1 → Time6</summary>
        [MaxLength(10)]
        public string? TimeSlot { get; set; }

        /// <summary>Số lượng người dùng nhập thêm lần này</summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? QuantityAdded { get; set; }

        /// <summary>Tổng giá trị của TimeSlot đó SAU KHI cộng thêm</summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? QuantityAfter { get; set; }

        /// <summary>Tổng giá trị của TimeSlot đó TRƯỚC KHI cộng</summary>
        [Column(TypeName = "decimal(18,4)")]
        public decimal? QuantityBefore { get; set; }

        /// <summary>WC mặc định = "FG"</summary>
        [MaxLength(20)]
        public string? WC { get; set; }

  
        [MaxLength(15)]
        public string? Shift { get; set; }

        public DateTime CreatedAt { get; set; } = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time"));

    
        [MaxLength(60)]
        public string? ClientIp { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }
    }
}