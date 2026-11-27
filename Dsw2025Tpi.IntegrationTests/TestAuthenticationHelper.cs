
using System.Net.Http;
using System.Net.Http.Headers;
using Dsw2025Tpi.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Dsw2025Tpi.IntegrationTests;

public static class TestAuthenticationHelper
{
    public static string CreateAdminToken(IServiceProvider services)
    {
        var tokenService =
            services.GetRequiredService<JwtTokenService>();

        return tokenService.GenerateToken(
            username: "admin.integration.test",
            role: "Admin",
            customerId: string.Empty);
    }

    public static string CreateUserToken(
        IServiceProvider services,
        string customerId)
    {
        var tokenService =
            services.GetRequiredService<JwtTokenService>();

        return tokenService.GenerateToken(
            username: "user.integration.test",
            role: "User",
            customerId: customerId);
    }

    public static void Authenticate(
        HttpClient client,
        string token)
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    public static void AuthenticateAsAdmin(
        HttpClient client,
        IServiceProvider services)
    {
        var token = CreateAdminToken(services);
        Authenticate(client, token);
    }

    public static void AuthenticateAsUser(
        HttpClient client,
        IServiceProvider services,
        string customerId)
    {
        var token = CreateUserToken(services, customerId);
        Authenticate(client, token);
    }
}
