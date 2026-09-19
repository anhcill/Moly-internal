using FluentAssertions;
using InternalManagement.Application.Features.Integration.Models;
using InternalManagement.Infrastructure.Integration.Connectors;

namespace InternalManagement.UnitTests.Integration;

public class WebsiteEdtechConnectorTests
{
    [Fact]
    public async Task PullCourses_ShouldReturnValidSyncPage()
    {
        // Arrange
        var connector = new WebsiteEdtechConnector();
        var cursor = new SyncCursor();

        // Act
        var page = await connector.PullAsync("Courses", cursor, CancellationToken.None);

        // Assert
        page.Should().NotBeNull();
        page.Items.Should().NotBeEmpty();
        page.Items.Count.Should().Be(3);
    }

    [Fact]
    public async Task PullCustomers_ShouldReturnValidSyncPage()
    {
        // Arrange
        var connector = new WebsiteEdtechConnector();
        var cursor = new SyncCursor();

        // Act
        var page = await connector.PullAsync("Customers", cursor, CancellationToken.None);

        // Assert
        page.Should().NotBeNull();
        page.Items.Should().NotBeEmpty();
        page.Items.Count.Should().Be(4);
    }

    [Fact]
    public async Task PullPayments_ShouldReturnValidSyncPage()
    {
        // Arrange
        var connector = new WebsiteEdtechConnector();
        var cursor = new SyncCursor();

        // Act
        var page = await connector.PullAsync("Payments", cursor, CancellationToken.None);

        // Assert
        page.Should().NotBeNull();
        page.Items.Should().NotBeEmpty();
        page.Items.Count.Should().Be(3);
    }

    [Fact]
    public async Task PullSubscriptions_ShouldReturnValidSyncPage()
    {
        // Arrange
        var connector = new WebsiteEdtechConnector();
        var cursor = new SyncCursor();

        // Act
        var page = await connector.PullAsync("Subscriptions", cursor, CancellationToken.None);

        // Assert
        page.Should().NotBeNull();
        page.Items.Should().NotBeEmpty();
        page.Items.Count.Should().Be(2);
    }

    [Fact]
    public async Task PullQuestions_ShouldReturnValidSyncPage()
    {
        // Arrange
        var connector = new WebsiteEdtechConnector();
        var cursor = new SyncCursor();

        // Act
        var page = await connector.PullAsync("Questions", cursor, CancellationToken.None);

        // Assert
        page.Should().NotBeNull();
        page.Items.Should().NotBeEmpty();
        page.Items.Count.Should().Be(2);
    }

    [Fact]
    public async Task CheckHealth_ShouldReturnHealthy()
    {
        // Arrange
        var connector = new WebsiteEdtechConnector();

        // Act
        var health = await connector.CheckHealthAsync(CancellationToken.None);

        // Assert
        health.Should().NotBeNull();
        health.IsHealthy.Should().BeTrue();
        health.SourceSystem.Should().Be("WEBSITE_EDTECH");
    }
}
