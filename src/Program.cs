using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VisitorManagement;
using VisitorManagement.Data;
using VisitorManagement.Models;
using VisitorManagement.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(o =>
{
    // Tenant pages need a staff sign-in except the visitor kiosk and the sign-in pages themselves.
    o.Conventions.AddFolderApplicationModelConvention("/", m =>
    {
        if (!TenantPageRouteConvention.IsPlatformPage(m.ViewEnginePath))
            m.EndpointMetadata.Add(new AuthorizeAttribute());
    });
    o.Conventions.AllowAnonymousToPage("/CheckIn");
    o.Conventions.AllowAnonymousToPage("/Visits/Badge"); // checks a kiosk token itself when signed out
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Account/Logout");
    o.Conventions.AllowAnonymousToPage("/Account/Setup");
    o.Conventions.AllowAnonymousToPage("/Account/AccessDenied");
    o.Conventions.AuthorizeFolder("/Admin", AuthPolicies.Admin);
    o.Conventions.AuthorizeFolder("/Employees", AuthPolicies.Admin);

    // Platform admin: its own sign-in. Its URLs are lowercase so they always match the platform
    // cookie's path (/platform); cookie paths are case-sensitive, and redirects would otherwise
    // go to /Platform/... and lose the sign-in.
    o.Conventions.AddFolderRouteModelConvention("/Platform", model =>
    {
        foreach (var selector in model.Selectors)
            if (selector.AttributeRouteModel is { Template: { } template })
                selector.AttributeRouteModel.Template = template.ToLowerInvariant();
    });
    o.Conventions.AuthorizeFolder("/Platform", AuthPolicies.Platform);
    o.Conventions.AllowAnonymousToPage("/Platform/Index");
    o.Conventions.AllowAnonymousToPage("/Platform/Setup");
    o.Conventions.AllowAnonymousToPage("/Platform/Login");
    o.Conventions.AllowAnonymousToPage("/Platform/Logout");
});

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    // Tenant staff. The tenant's URL prefix is the request's PathBase, so the cookie is scoped to it
    // (/acme) and sign-in redirects stay inside the tenant automatically. The cookie also records
    // the tenant it belongs to, which is checked on every request.
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.LogoutPath = "/Account/Logout";
        o.AccessDeniedPath = "/Account/AccessDenied";
        o.Cookie.Name = "vms.auth";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Lax;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(10);
        o.SlidingExpiration = true;
        o.Events.OnValidatePrincipal = StaffAuthService.ValidatePrincipalAsync;
    })
    // Platform admins.
    .AddCookie(PlatformAuthService.Scheme, o =>
    {
        o.Cookie.Name = "vms.platform";
        o.Cookie.Path = "/platform";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        o.ExpireTimeSpan = TimeSpan.FromHours(4);
        o.SlidingExpiration = true;
        o.LoginPath = "/platform/login";
        o.AccessDeniedPath = "/platform/login";
        o.Events.OnValidatePrincipal = PlatformAuthService.ValidatePrincipalAsync;
    });

builder.Services.AddAuthorization(o =>
{
    o.AddPolicy(AuthPolicies.Admin, p => p.RequireRole(StaffRoles.Admin));
    o.AddPolicy(AuthPolicies.Platform, p => p
        .AddAuthenticationSchemes(PlatformAuthService.Scheme)
        .RequireAuthenticatedUser()
        .RequireRole(PlatformAuthService.Role));
});

// Resolve a relative SQLite path against the content root, not the process's current directory.
var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Default"));
if (!Path.IsPathRooted(connection.DataSource))
{
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
}
Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);

builder.Services.AddScoped<TenantContext>();
builder.Services.AddSingleton<TenantRegistry>();
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection.ToString()));
builder.Services.AddScoped<VisitService>();
builder.Services.AddScoped<BlacklistService>();
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<ReportService>();
builder.Services.AddScoped<LookupService>();
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<StaffAuthService>();
builder.Services.AddScoped<HostImportService>();
builder.Services.AddScoped<PlatformAuthService>();
builder.Services.AddScoped<TenantProvisioningService>();
builder.Services.AddScoped<LicenseService>();
builder.Services.AddScoped<PackageService>();
builder.Services.AddScoped<RazorpayService>();
builder.Services.AddHttpClient(RazorpayService.HttpClientName, c => c.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddScoped<TenantExportService>();
builder.Services.AddScoped<DatabaseBackupService>();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<IOtpService, DummyOtpService>();
builder.Services.AddSingleton<PhoneVerification>();
builder.Services.AddSingleton<KioskBadgeToken>();

var app = builder.Build();

VisitorManagement.Format.CurrencySymbol = app.Configuration["Billing:CurrencySymbol"] ?? "₹";
VisitorManagement.Format.MoneyCulture = System.Globalization.CultureInfo.GetCultureInfo(app.Configuration["Billing:Culture"] ?? "en-IN");
await SeedData.InitializeAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

// Resolve the tenant from the first URL segment and make it the PathBase: /acme/Visits is handled
// as /Visits with PathBase /acme. Every generated link, redirect, ~/ path and cookie then carries
// the tenant automatically. Unknown and inactive tenants get the same 404 (so ids can't be probed);
// a differently-cased id redirects to the canonical one (paths and cookies are case-sensitive).
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "/";
    var slash = path.IndexOf('/', 1);
    var segment = slash < 0 ? path[1..] : path[1..slash];

    if (segment.Length > 0 && !Tenant.ReservedSlugs.Contains(segment) && Tenant.SlugPattern().IsMatch(segment.ToLowerInvariant()))
    {
        var tenant = await context.RequestServices.GetRequiredService<TenantRegistry>().FindBySlugAsync(segment);
        if (tenant is not { IsActive: true })
        {
            await WriteNotFoundAsync(context);
            return;
        }
        var rest = slash < 0 ? "/" : path[slash..];
        if (segment != tenant.Slug)
        {
            context.Response.Redirect($"{context.Request.PathBase}/{tenant.Slug}{rest}{context.Request.QueryString}");
            return;
        }
        context.Request.PathBase = context.Request.PathBase.Add("/" + tenant.Slug);
        context.Request.Path = rest;
        context.RequestServices.GetRequiredService<TenantContext>().Set(tenant);
    }
    else if (path == "/")
    {
        context.Request.Path = "/Platform"; // site root: the organisation finder
    }
    await next();
});

app.UseStaticFiles();
app.UseRouting();

// Tenant pages and endpoints only exist under a tenant; platform pages only outside one.
app.Use(async (context, next) =>
{
    if (context.GetEndpoint() is not null)
    {
        var page = context.GetRouteValue("page") as string;
        var isPlatform = page is not null && TenantPageRouteConvention.IsPlatformPage(page);
        var hasTenant = context.RequestServices.GetRequiredService<TenantContext>().HasTenant;
        if (hasTenant == isPlatform && page != "/Error")
        {
            await WriteNotFoundAsync(context);
            return;
        }
    }
    await next();
});

app.UseAuthentication();

// Accounts with a temporary password must choose their own before doing anything else.
app.Use(async (context, next) =>
{
    // Only pages (they have a "page" route value); images and stylesheets still load.
    var page = context.GetRouteValue("page") as string ?? "";
    var tenant = context.RequestServices.GetRequiredService<TenantContext>();
    if (tenant.HasTenant
        && page.Length > 0
        && !page.StartsWith("/Account/", StringComparison.OrdinalIgnoreCase)
        && context.User.HasClaim(StaffAuthService.MustChangePasswordClaim, "true"))
    {
        context.Response.Redirect(tenant.Url("/Account/ChangePassword"));
        return;
    }
    await next();
});

app.UseAuthorization();
app.MapRazorPages();

// Tenant endpoints (reached as /{tenant}/branding/logo etc. via PathBase).
// Razorpay webhook (platform-level, no tenant in the URL). Verified by its HMAC signature; register
// https://<host>/payments/razorpay/webhook in the Razorpay dashboard for payment.captured and payment.failed.
app.MapPost("/payments/razorpay/webhook", async (HttpRequest request, RazorpayService razorpay) =>
{
    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync();
    return await razorpay.HandleWebhookAsync(body, request.Headers["X-Razorpay-Signature"]) ? Results.Ok() : Results.BadRequest();
});

var tenantApi = app.MapGroup("");

// Company logo from Admin > Branding (public: shown on the kiosk and sign-in page).
// The URL carries a version, so it can be cached for long.
tenantApi.MapGet("/branding/logo", async (SettingsService settings, HttpContext http) =>
{
    if (await settings.GetLogoAsync() is not { } logo) return Results.NotFound();
    http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
    http.Response.Headers["X-Content-Type-Options"] = "nosniff";
    return Results.File(logo.Data, logo.ContentType);
});

// Purpose colours, generated from Admin > Lists so new purposes get their badge colour immediately.
tenantApi.MapGet("/lookups/purposes.css", async (LookupService lookups, HttpContext http) =>
{
    http.Response.Headers.CacheControl = "no-cache";
    return Results.Text(await lookups.PurposeCssAsync(), "text/css");
});

// Live notifications for the top bar bell.
tenantApi.MapGet("/notifications", async (NotificationService notifications, HttpContext http) =>
{
    http.Response.Headers.CacheControl = "no-store";
    var items = await notifications.GetAsync();
    return Results.Json(items.Select(n => new
    {
        n.Id, n.Level, n.Title, n.Detail, n.Url,
        At = n.At.ToString("o"),
        Ago = Format.Ago(n.At),
        Icon = Icons.Svg(n.Icon, 16).ToString()
    }));
}).RequireAuthorization();

// Signature and ID card images captured at check-in (staff only).
tenantApi.MapGet("/visits/{id:int}/attachments/{kind}", async (int id, AttachmentKind kind, AppDbContext db, HttpContext http) =>
{
    var file = await db.VisitAttachments.AsNoTracking().FirstOrDefaultAsync(a => a.VisitId == id && a.Kind == kind);
    if (file is null) return Results.NotFound();

    http.Response.Headers.CacheControl = "private, no-store";
    return Results.File(file.Data, file.ContentType);
}).RequireAuthorization();

// Visitor photo: staff, or the visitor at the kiosk holding a fresh badge token for this visit.
tenantApi.MapGet("/visits/{id:int}/photo", async (int id, string? t, AppDbContext db, KioskBadgeToken tokens, TenantContext tenant, HttpContext http) =>
{
    if (http.User.Identity?.IsAuthenticated != true && !tokens.IsValidFor(t, tenant.Id, id)) return Results.NotFound();

    var photo = await db.VisitPhotos.AsNoTracking().FirstOrDefaultAsync(p => p.VisitId == id);
    if (photo is null) return Results.NotFound();

    http.Response.Headers.CacheControl = "private, max-age=3600";
    return Results.File(photo.Data, photo.ContentType);
});

app.Run();

static async Task WriteNotFoundAsync(HttpContext context)
{
    context.Response.StatusCode = StatusCodes.Status404NotFound;
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(
        "<!doctype html><meta charset=utf-8><title>Not found</title><body style=\"font-family:system-ui;padding:3rem;text-align:center\">" +
        "<h1>Not found</h1><p>Check the address, or <a href=\"/\">find your organisation</a>.</p>");
}

/// <summary>Authorization policy names.</summary>
public static class AuthPolicies
{
    public const string Admin = "Admin";
    public const string Platform = "Platform";
}
