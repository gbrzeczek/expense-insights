using Microsoft.AspNetCore.Antiforgery;

namespace ExpenseInsights.Api.Auth;

/// <summary>
/// Antiforgery for the SPA, in the shape Angular's HttpClient expects: a JS-readable
/// <c>XSRF-TOKEN</c> cookie that it echoes back in the <c>X-XSRF-TOKEN</c> header. The token is bound
/// to the signed-in user, so it is (re)issued on every authenticated GET under <c>/api</c>
/// (the app calls <c>GET /api/me</c> first), and every state-changing <c>/api</c> request must carry it.
/// </summary>
internal static class Xsrf
{
    public const string CookieName = "XSRF-TOKEN";
    public const string HeaderName = "X-XSRF-TOKEN";

    public static IApplicationBuilder UseXsrf(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments("/api"))
        {
            await next(context);
            return;
        }

        var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                Issue(context, antiforgery);
            }
        }
        else if (!await antiforgery.IsRequestValidAsync(context))
        {
            // Runs after authorization, so an anonymous request still gets its 401 first.
            await ApiProblem.Of(StatusCodes.Status400BadRequest, "antiforgery", "The XSRF token is missing or invalid.")
                .ExecuteAsync(context);
            return;
        }

        await next(context);
    });

    public static void Clear(HttpContext context) => context.Response.Cookies.Delete(CookieName);

    private static void Issue(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        context.Response.Cookies.Append(CookieName, tokens.RequestToken!, new CookieOptions
        {
            // Angular reads it; it is useless without the HttpOnly antiforgery cookie and the session.
            HttpOnly = false,
            SameSite = SameSiteMode.Strict,
            Secure = context.Request.IsHttps,
            Path = "/",
        });
    }
}
