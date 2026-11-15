using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement;

/// <summary>
/// Put on a page model: the page is only available when the tenant's license package includes the feature.
/// Otherwise the user is sent to Plan &amp; usage, which explains which package includes it.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class RequireFeatureAttribute(Feature feature) : Attribute, IAsyncPageFilter
{
    public Feature Feature { get; } = feature;

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context) => Task.CompletedTask;

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var license = context.HttpContext.RequestServices.GetRequiredService<LicenseService>();
        if (!await license.HasFeatureAsync(Feature))
        {
            context.Result = new RedirectToPageResult("/Plan/Index", new { missing = Feature.ToString() });
            return;
        }
        await next();
    }
}
