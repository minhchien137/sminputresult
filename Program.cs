using Microsoft.EntityFrameworkCore;
using SMInputProduction.Models;

var builder = WebApplication.CreateBuilder(args);

// ✅ Tất cả services phải đăng ký TRƯỚC Build()
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UsePathBase("/sigma");

app.Use((context, next) =>
{
    context.Request.PathBase = "/sigma";
    return next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=SM}/{action=InputResult}/{id?}")
    .WithStaticAssets();

app.Run();