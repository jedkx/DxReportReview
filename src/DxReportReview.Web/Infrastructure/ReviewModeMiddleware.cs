using Microsoft.AspNetCore.Http;

namespace DxReportReview.Web.Infrastructure;

// Session isn't visible to DevExpress the same way inside SetData; Items bridges one request.
public sealed class ReviewModeMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Session is { IsAvailable: true })
        {
            var approver = context.Session.GetInt32(ReviewSessionKeys.ApproverId);
            if (approver.HasValue)
                context.Items[ReviewSessionKeys.ApproverId] = approver.Value;
        }

        await next(context);
    }
}
