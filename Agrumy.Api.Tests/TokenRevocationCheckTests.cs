using api.Security;

namespace Agrumy.Api.Tests;

public class TokenRevocationCheckTests
{
    [Fact]
    public void IsRevoked_NoCutoff_NeverRevoked()
    {
        Assert.False(TokenRevocationCheck.IsRevoked(DateTime.UtcNow.AddDays(-1), null));
    }

    [Fact]
    public void IsRevoked_TokenIssuedBeforeCutoff_ReturnsTrue()
    {
        DateTime cutoff = DateTime.UtcNow;
        DateTime issuedAt = cutoff.AddMinutes(-5);
        Assert.True(TokenRevocationCheck.IsRevoked(issuedAt, cutoff));
    }

    [Fact]
    public void IsRevoked_TokenIssuedAfterCutoff_ReturnsFalse()
    {
        DateTime cutoff = DateTime.UtcNow;
        DateTime issuedAt = cutoff.AddMinutes(5);
        Assert.False(TokenRevocationCheck.IsRevoked(issuedAt, cutoff));
    }
}
