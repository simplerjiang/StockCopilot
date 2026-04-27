using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace SimplerJiangAiAgent.Api.Tests;

public sealed class AdminAuthEndpointTests : IClassFixture<AdminAuthEndpointTests.Factory>
{
    private readonly Factory _factory;

    public AdminAuthEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task ProtectedAdminEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/admin/llm/settings/active");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("not-a-bearer-token")]
    [InlineData("Bearer")]
    [InlineData("Bearer wrong-token")]
    [InlineData("Basic wrong-token")]
    public async Task ProtectedAdminEndpoint_WithInvalidAuthorization_ReturnsUnauthorized(string authorizationHeader)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", authorizationHeader);

        var response = await client.GetAsync("/api/admin/llm/settings/active");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AdminLogin_WithValidCredentials_AllowsProtectedEndpoint()
    {
        var client = _factory.CreateClient();

        var loginResponse = await client.PostAsJsonAsync("/api/admin/login", new
        {
            username = Factory.AdminUsername,
            password = Factory.AdminPassword
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginBody = await loginResponse.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginBody.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var protectedResponse = await client.GetAsync("/api/admin/llm/settings/active");

        Assert.Equal(HttpStatusCode.OK, protectedResponse.StatusCode);
    }

    [Fact]
    public async Task AdminLogin_WithInvalidCredentials_ReturnsUnauthorized()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/admin/login", new
        {
            username = Factory.AdminUsername,
            password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public const string AdminUsername = "test-admin";
        public const string AdminPassword = "test-password";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var dataRoot = Path.Combine(Path.GetTempPath(), "sjai-tests", nameof(AdminAuthEndpointTests), Guid.NewGuid().ToString("N"));

            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:DataRootPath"] = dataRoot,
                    ["Admin:Username"] = AdminUsername,
                    ["Admin:Password"] = AdminPassword,
                    ["Admin:TokenExpiryMinutes"] = "5"
                });
            });
            builder.ConfigureServices(services =>
            {
                ApiTestDatabaseIsolation.UseIsolatedSqlite(services, dataRoot);
                services.RemoveAll<IHostedService>();
            });
        }
    }
}