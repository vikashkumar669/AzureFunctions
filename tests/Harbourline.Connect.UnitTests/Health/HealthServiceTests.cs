using Harbourline.Connect.Core.Health;
using NSubstitute;

namespace Harbourline.Connect.UnitTests.Health;

public sealed class HealthServiceTests
{
    [Fact]
    public void GetStatus_ReturnsServiceNameAndCurrentUtcTime()
    {
        // Arrange: freeze time so the assertion is deterministic.
        var now = new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);
        var time = Substitute.For<TimeProvider>();
        time.GetUtcNow().Returns(now);
        var service = new HealthService(time);

        // Act
        var status = service.GetStatus();

        // Assert
        Assert.Equal(HealthService.ServiceName, status.Service);
        Assert.Equal(now, status.CheckedAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(status.Version));
    }
}
