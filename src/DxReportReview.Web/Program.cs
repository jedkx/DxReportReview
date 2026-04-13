using DevExpress.AspNetCore;
using DevExpress.AspNetCore.Reporting;
using DevExpress.XtraReports.Web.Extensions;
using DxReportReview.Web.Fixtures;
using DxReportReview.Web.Infrastructure;
using DxReportReview.Web.Reporting;
using DxReportReview.Web.Storage;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;

DevExpress.Drawing.Settings.DrawingEngine = DevExpress.Drawing.DrawingEngine.Skia;

var builder = WebApplication.CreateBuilder(args);

var dataProtectionKeysDir = Path.Combine(builder.Environment.ContentRootPath, "App_Data", "dp-keys");
Directory.CreateDirectory(dataProtectionKeysDir);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysDir))
    .SetApplicationName("DxReportReview");

builder.Services.AddAntiforgery(options => options.HeaderName = "RequestVerificationToken");
builder.Services.AddDevExpressControls();
builder.Services.AddControllersWithViews();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromHours(8);
});

builder.Services.ConfigureReportingServices(configurator =>
{
    if (builder.Environment.IsDevelopment())
        configurator.UseDevelopmentMode();
    configurator.ConfigureReportDesigner(_ => { });
    // Leave UseCachedReportSourceBuilder off — cached XtraReport = diff highlights go missing.
    configurator.ConfigureWebDocumentViewer(_ => { });
});

builder.Services.AddSingleton<IReportStorage>(_ => new InMemoryReportStorage(SeedReports.CreateSeed()));
builder.Services.AddSingleton<IReviewRepository, InMemoryReviewRepository>();
builder.Services.AddScoped<ReportStorageWebExtension, ReviewAwareReportStorage>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/auth/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/Home/Error");

app.UseDevExpressControls();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ReviewModeMiddleware>();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
