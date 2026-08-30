using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using api.Security;
using api.Utils;
using Microsoft.IdentityModel.Tokens;

namespace Agrumy.Api.Tests;

public class AuthenticationProviderTests
{
    [Fact]
    public void GetSalt_ReturnsDifferentValuesEachCall()
    {
        Assert.NotEqual(AuthenticationProvider.GetSalt(), AuthenticationProvider.GetSalt());
    }

    [Fact]
    public void GetHash_SamePasswordAndSalt_ProducesSameHash()
    {
        string salt = AuthenticationProvider.GetSalt();

        string a = AuthenticationProvider.GetHash("correct horse battery staple", salt);
        string b = AuthenticationProvider.GetHash("correct horse battery staple", salt);

        Assert.Equal(a, b);
    }

    [Fact]
    public void GetHash_DifferentPassword_ProducesDifferentHash()
    {
        string salt = AuthenticationProvider.GetSalt();

        string a = AuthenticationProvider.GetHash("password-one", salt);
        string b = AuthenticationProvider.GetHash("password-two", salt);

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GetHash_DifferentSalt_ProducesDifferentHash()
    {
        string a = AuthenticationProvider.GetHash("same-password", AuthenticationProvider.GetSalt());
        string b = AuthenticationProvider.GetHash("same-password", AuthenticationProvider.GetSalt());

        Assert.NotEqual(a, b);
    }

    [Fact]
    public void VerifyHash_CorrectPassword_ReturnsTrue()
    {
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash("s3cret!", salt);

        Assert.True(AuthenticationProvider.VerifyHash(hash, salt, "s3cret!"));
    }

    [Fact]
    public void VerifyHash_WrongPassword_ReturnsFalse()
    {
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash("s3cret!", salt);

        Assert.False(AuthenticationProvider.VerifyHash(hash, salt, "not-the-password"));
    }

    [Fact]
    public void VerifyHash_NullArguments_ReturnFalseWithoutThrowing()
    {
        Assert.False(AuthenticationProvider.VerifyHash(null, "salt", "pw"));
        Assert.False(AuthenticationProvider.VerifyHash("hash", null, "pw"));
        Assert.False(AuthenticationProvider.VerifyHash("hash", "salt", null));
    }

    [Fact]
    public void VerifyHash_StoredHashOfDifferentLength_ReturnsFalse()
    {
        // Exercises the explicit length guard added before FixedTimeEquals.
        string salt = AuthenticationProvider.GetSalt();
        string hash = AuthenticationProvider.GetHash("pw", salt);

        Assert.False(AuthenticationProvider.VerifyHash(hash + "AB", salt, "pw")); // longer
        Assert.False(AuthenticationProvider.VerifyHash(hash[..^2], salt, "pw")); // shorter
    }

    [Fact]
    public void FixedTimeEquals_MatchesEqualBytes_AndRejectsDifferentLengths()
    {
        // Sanity check on the primitive the security fix relies on.
        byte[] a = [1, 2, 3, 4];
        byte[] same = [1, 2, 3, 4];
        byte[] diffValue = [1, 2, 3, 9];
        byte[] diffLength = [1, 2, 3];

        Assert.True(CryptographicOperations.FixedTimeEquals(a, same));
        Assert.False(CryptographicOperations.FixedTimeEquals(a, diffValue));
        Assert.False(CryptographicOperations.FixedTimeEquals(a, diffLength));
    }
}

public class JwtTokenProviderTests
{
    // Config (Agrumy.Shared) reads appsettings.json from the working directory; the test
    // project ships one, so Config.secureKey / jwtIssuer / jwtAudience resolve.
    private const string SigningKey = "unit-test-signing-key-not-a-secret-0123456789ABCDEF";

    [Fact]
    public void ValidateToken_AcceptsFreshTokenAndReturnsRoleClaim()
    {
        string token = JwtTokenProvider.CreateToken(SigningKey, expiration: 5, subject: "alice@example.com", role: "admin", tenantID: "0");

        string? role = JwtTokenProvider.ValidateToken(token);

        Assert.Equal("admin", role);
    }

    [Fact]
    public void ValidateToken_RejectsExpiredToken()
    {
        // Hand-craft a token that already expired (CreateToken can't produce Expires < NotBefore),
        // signed with the same key ValidateToken uses (Config.secureKey == SigningKey).
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("role", "user") }),
            NotBefore = DateTime.UtcNow.AddMinutes(-10),
            Expires = DateTime.UtcNow.AddMinutes(-5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        string expired = handler.WriteToken(handler.CreateToken(descriptor));

        Assert.Null(JwtTokenProvider.ValidateToken(expired));
    }

    [Fact]
    public void ValidateToken_RejectsTokenSignedWithADifferentKey()
    {
        string token = JwtTokenProvider.CreateToken("a-totally-different-signing-key-that-is-long-enough", 5, "eve@example.com", "user", "0");

        Assert.Null(JwtTokenProvider.ValidateToken(token));
    }

    [Fact]
    public void ValidateToken_RejectsGarbage()
    {
        Assert.Null(JwtTokenProvider.ValidateToken("not-a-jwt"));
    }

    [Fact]
    public void ValidateToken_RejectsWrongIssuerOrAudience()
    {
        // Roadmap #48 regression guard: correctly signed with the same key Config.secureKey
        // validates against, but stamped with an issuer/audience that doesn't match
        // Config.jwtIssuer/jwtAudience - before the fix ValidateIssuer/ValidateAudience were
        // false, so this token would have silently passed.
        var key = new SymmetricSecurityKey(Encoding.ASCII.GetBytes(SigningKey));
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new[] { new Claim("role", "admin") }),
            Issuer = "https://attacker.example",
            Audience = "not-agrumy-api",
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256Signature)
        };
        var handler = new JwtSecurityTokenHandler();
        string tokenWithWrongIssuerAndAudience = handler.WriteToken(handler.CreateToken(descriptor));

        Assert.Null(JwtTokenProvider.ValidateToken(tokenWithWrongIssuerAndAudience));
    }
}

public class FieldValidatorTests
{
    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last@sub.domain.co")]
    [InlineData("a+b@c.io")]
    public void IsValidEmail_AcceptsWellFormedAddresses(string email)
    {
        Assert.True(FieldValidator.IsValidEmail(email));
    }

    [Theory]
    [InlineData("plainstring")]
    [InlineData("missing-at.com")]
    [InlineData("@no-local-part.com")]
    [InlineData("spaces in@email.com")]
    [InlineData("trailing@dot.")]
    public void IsValidEmail_RejectsMalformedAddresses(string email)
    {
        Assert.False(FieldValidator.IsValidEmail(email));
    }
}
