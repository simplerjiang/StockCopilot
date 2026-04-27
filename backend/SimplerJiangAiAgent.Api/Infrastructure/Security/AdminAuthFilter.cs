using Microsoft.AspNetCore.Http;
using System.Net.Http.Headers;

namespace SimplerJiangAiAgent.Api.Infrastructure.Security;

public sealed class AdminAuthFilter : IEndpointFilter
{
    private readonly IAdminAuthService _authService;

    public AdminAuthFilter(IAdminAuthService authService)
    {
        _authService = authService;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var authorizationHeader = context.HttpContext.Request.Headers.Authorization.ToString();
        if (!AuthenticationHeaderValue.TryParse(authorizationHeader, out var authorization)
            || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(authorization.Parameter)
            || !_authService.IsTokenValid(authorization.Parameter))
        {
            return Results.Unauthorized();
        }

        return await next(context);
    }
}
