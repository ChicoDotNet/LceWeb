using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace LceWeb.Api.Admin;

public sealed record AdminConfiguration(bool Enabled, string? PublicOrigin)
{
    public static AdminConfiguration Register(IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.GetValue<bool>("Admin:Enabled");
        if (!enabled) return new(false, null);
        var tenant = configuration["Admin:TenantId"];
        var client = configuration["Admin:ClientId"];
        var secret = configuration["Admin:ClientSecret"];
        var origin = configuration["Admin:PublicOrigin"];
        if (!Guid.TryParse(tenant, out var tenantId) || !Guid.TryParse(client, out var clientId) ||
            string.IsNullOrWhiteSpace(secret) ||
            !Uri.TryCreate(origin, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
            throw new InvalidOperationException("Admin configuration is incomplete or invalid.");
        var trustedOrigin = uri.GetLeftPart(UriPartial.Authority);
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = "__Host-LceAdmin";
                options.Cookie.Path = "/";
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.ExpireTimeSpan = TimeSpan.FromHours(1);
                options.SlidingExpiration = false;
                options.LoginPath = "/admin/login";
                options.AccessDeniedPath = "/admin/denied";
            })
            .AddOpenIdConnect("Entra", options =>
            {
                options.Authority = $"https://login.microsoftonline.com/{tenantId:D}/v2.0";
                options.ClientId = clientId.ToString("D");
                options.ClientSecret = secret;
                options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.UsePkce = true;
                options.SaveTokens = false;
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "name";
                options.TokenValidationParameters.RoleClaimType = "roles";
                options.TokenValidationParameters.ValidateIssuer = true;
                options.TokenValidationParameters.ValidateAudience = true;
                options.CallbackPath = "/signin-oidc";
                options.Events.OnRedirectToIdentityProvider = context =>
                {
                    context.ProtocolMessage.RedirectUri = trustedOrigin + "/signin-oidc";
                    return Task.CompletedTask;
                };
                options.Events.OnTokenValidated = context =>
                {
                    var principal = context.Principal;
                    if (principal is null || !Guid.TryParse(principal.FindFirst("tid")?.Value, out var actualTenant) ||
                        actualTenant != tenantId || !Guid.TryParse(principal.FindFirst("oid")?.Value, out _) ||
                        !(principal.IsInRole("Lce.Advisor") || principal.IsInRole("Lce.Admin")))
                        context.Fail("The account is not authorized for LCE administration.");
                    return Task.CompletedTask;
                };
                options.Events.OnRemoteFailure = context =>
                {
                    context.HandleResponse();
                    context.Response.Redirect("/admin/denied");
                    return Task.CompletedTask;
                };
            });
        services.AddAuthorization(options => options.AddPolicy("LceStaff", policy => policy
            .RequireAuthenticatedUser().RequireRole("Lce.Advisor", "Lce.Admin").RequireClaim("oid")));
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-LceAdminCsrf";
            options.Cookie.Path = "/";
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
        });
        return new(true, trustedOrigin);
    }
}
