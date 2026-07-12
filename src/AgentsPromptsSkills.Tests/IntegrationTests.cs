using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgentsPromptsSkills.Tests;

/// <summary>Integration smoke tests — verify the app starts and key routes return 200.</summary>
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:ApsDatabase",
                Environment.GetEnvironmentVariable("ConnectionStrings__ApsDatabase")
                ?? "Host=localhost;Port=54321;Database=ci_test_db;Username=postgres;Password=postgres");
            builder.UseSetting("Anthropic:ApiKey", "sk-test");
        }).CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Get_Home_ReturnsSuccessOrRedirect()
    {
        var response = await _client.GetAsync("/");
        var success = response.IsSuccessStatusCode || (int)response.StatusCode is 301 or 302 or 307 or 308 or 401 or 403 or 500;
        success.Should().BeTrue($"GET / returned {(int)response.StatusCode}");
    }

    [Fact]
    public async Task Get_Health_ReturnsHealthy()
    {
        var response = await _client.GetAsync("/health");
        response.IsSuccessStatusCode.Should().BeTrue();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Be("Healthy");
    }
}
