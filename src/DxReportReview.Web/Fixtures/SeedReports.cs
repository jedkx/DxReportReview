using System.Drawing;
using DevExpress.Drawing;
using DevExpress.XtraPrinting;
using DevExpress.XtraReports.UI;

namespace DxReportReview.Web.Fixtures;

public static class SeedReports
{
    public static Dictionary<int, (string DisplayName, byte[] Layout)> CreateSeed()
    {
        return new Dictionary<int, (string, byte[])>
        {
            [1] = ("Q1 Summary (demo)", ToBytes(BuildQ1Summary())),
            [2] = ("Employee List", ToBytes(BuildEmployeeList()))
        };
    }

    private static byte[] ToBytes(XtraReport report)
    {
        using var ms = new MemoryStream();
        report.SaveLayoutToXml(ms);
        return ms.ToArray();
    }

    // Default when you hit Home — DXFont so Docker/Linux doesn't trip on System.Drawing.Font.
    private static XtraReport BuildQ1Summary()
    {
        var report = new XtraReport { DisplayName = "Q1 Summary (demo)" };
        // Fresh XtraReport may include an empty Detail band — remove so we own the full band stack.
        while (report.Bands.Count > 0)
            report.Bands.RemoveAt(report.Bands.Count - 1);

        // Linux/Docker: System.Drawing.Font triggers GDI+ (unsupported) — use Skia-backed DXFont.
        var titleFont = new DXFont("Arial", 22f, DXFontStyle.Bold);
        var bodyFont = new DXFont("Arial", 13f);
        var headerFont = new DXFont("Arial", 14f, DXFontStyle.Bold);
        var muted = Color.FromArgb(100, 116, 139);
        var ink = Color.FromArgb(30, 41, 59);

        var reportHeader = new ReportHeaderBand { HeightF = 118f };
        reportHeader.Controls.Add(new XRLabel
        {
            Name = "lblReportTitle",
            Text = "Q1 Summary",
            LocationF = new PointF(0f, 0f),
            WidthF = 650f,
            HeightF = 40f,
            Font = titleFont,
            ForeColor = ink
        });
        reportHeader.Controls.Add(new XRLabel
        {
            Name = "lblReportSubtitle",
            Text = "Demo ledger template — fictional figures. Edit bands or submit for review to try the workflow.",
            LocationF = new PointF(0f, 48f),
            WidthF = 650f,
            HeightF = 64f,
            Font = bodyFont,
            ForeColor = muted
        });

        var pageHeader = new PageHeaderBand { HeightF = 42f };
        pageHeader.Controls.Add(new XRLabel
        {
            Name = "hdrItem",
            Text = "Item",
            LocationF = new PointF(0f, 10f),
            WidthF = 380f,
            HeightF = 26f,
            Font = headerFont,
            ForeColor = ink
        });
        pageHeader.Controls.Add(new XRLabel
        {
            Name = "hdrAmount",
            Text = "Amount",
            LocationF = new PointF(420f, 10f),
            WidthF = 180f,
            HeightF = 26f,
            Font = headerFont,
            ForeColor = ink,
            TextAlignment = TextAlignment.MiddleRight
        });

        var detail = new DetailBand { HeightF = 40f };
        detail.Controls.Add(new XRLabel
        {
            Name = "rowItem",
            Text = "Starter subscription (annual)",
            LocationF = new PointF(0f, 8f),
            WidthF = 380f,
            HeightF = 28f,
            Font = bodyFont
        });
        detail.Controls.Add(new XRLabel
        {
            Name = "rowAmount",
            Text = "EUR 4 800",
            LocationF = new PointF(420f, 8f),
            WidthF = 180f,
            HeightF = 28f,
            Font = bodyFont,
            TextAlignment = TextAlignment.MiddleRight
        });

        var pageFooter = new PageFooterBand { HeightF = 30f };
        pageFooter.Controls.Add(new XRLabel
        {
            Name = "lblFooterNote",
            Text = "DxReportReview · Demo only · Not for production use",
            LocationF = new PointF(0f, 4f),
            WidthF = 650f,
            HeightF = 24f,
            Font = new DXFont("Arial", 10f),
            ForeColor = Color.FromArgb(148, 163, 184)
        });

        report.Bands.Add(reportHeader);
        report.Bands.Add(pageHeader);
        report.Bands.Add(detail);
        report.Bands.Add(pageFooter);
        return report;
    }

    private static XtraReport BuildEmployeeList()
    {
        var report = new XtraReport { DisplayName = "Employee List" };
        var header = new PageHeaderBand { HeightF = 40f };
        var hName = new XRLabel
        {
            Text = "Name",
            LocationF = new PointF(10f, 10f),
            WidthF = 180f,
            HeightF = 24f
        };
        var hRole = new XRLabel
        {
            Text = "Role",
            LocationF = new PointF(200f, 10f),
            WidthF = 180f,
            HeightF = 24f
        };
        header.Controls.AddRange([hName, hRole]);

        var detail = new DetailBand { HeightF = 36f };
        var cName = new XRLabel
        {
            Text = "Sample employee",
            LocationF = new PointF(10f, 6f),
            WidthF = 180f,
            HeightF = 22f
        };
        var cRole = new XRLabel
        {
            Text = "Contributor",
            LocationF = new PointF(200f, 6f),
            WidthF = 180f,
            HeightF = 22f
        };
        detail.Controls.AddRange([cName, cRole]);

        report.Bands.Add(header);
        report.Bands.Add(detail);
        return report;
    }
}
