
using System.Net;
using Xunit;

namespace Dsw2025Tpi.IntegrationTests;

public class FactoryTests
{
    [Fact]
    public async Task Factory_CanStartCreateDatabasesAndRespondToHttpRequest()
    {
        // Arrange
        using var factory = new CustomWebApplicationFactory();

        using var client = factory.CreateClient();

        factory.InitializeDatabase();

        // Act
        var response = await client.GetAsync("/healthcheck");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
