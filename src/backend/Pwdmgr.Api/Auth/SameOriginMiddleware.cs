namespace Pwdmgr.Api.Auth;

/// <summary>
/// CSRF defence in depth next to SameSite=Strict: a browser request that carries an Origin
/// header must be same-origin with the Host it is talking to. Requests without Origin (curl,
/// same-origin GET navigations) pass; safe methods are not checked.
/// </summary>
public sealed class SameOriginMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) && !HttpMethods.IsOptions(context.Request.Method)
            && context.Request.Headers.TryGetValue("Origin", out var origin)
            && !string.IsNullOrEmpty(origin)
            && !IsSameOrigin(origin.ToString(), context.Request))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(context);
    }

    private static bool IsSameOrigin(string origin, HttpRequest request)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = request.Host;
        var originPort = uri.IsDefaultPort ? (uri.Scheme == Uri.UriSchemeHttps ? 443 : 80) : uri.Port;
        var hostPort = host.Port ?? (request.IsHttps ? 443 : 80);
        return string.Equals(uri.Host, host.Host, StringComparison.OrdinalIgnoreCase) && originPort == hostPort;
    }
}
