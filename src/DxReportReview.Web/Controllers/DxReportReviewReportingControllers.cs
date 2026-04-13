using DevExpress.AspNetCore.Reporting.QueryBuilder;
using DevExpress.AspNetCore.Reporting.QueryBuilder.Native.Services;
using DevExpress.AspNetCore.Reporting.ReportDesigner;
using DevExpress.AspNetCore.Reporting.ReportDesigner.Native.Services;
using DevExpress.AspNetCore.Reporting.WebDocumentViewer;
using DevExpress.AspNetCore.Reporting.WebDocumentViewer.Native.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DxReportReview.Web.Controllers;

[Authorize]
public sealed class DxReportReviewWebDocumentViewerController(IWebDocumentViewerMvcControllerService controllerService)
    : WebDocumentViewerController(controllerService);

[Authorize]
public sealed class DxReportReviewReportDesignerController(IReportDesignerMvcControllerService controllerService)
    : ReportDesignerController(controllerService);

[Authorize]
public sealed class DxReportReviewQueryBuilderController(IQueryBuilderMvcControllerService controllerService)
    : QueryBuilderController(controllerService);
