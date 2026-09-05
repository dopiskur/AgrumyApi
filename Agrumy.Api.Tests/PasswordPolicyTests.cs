using api.Models;
using api.Security;

namespace Agrumy.Api.Tests;

public class PasswordPolicyTests
{
    [Fact]
    public void Validate_ShorterThanMinLength_ReturnsError()
    {
        var config = new ServerConfig { PasswordMinLength = 8 };
        Assert.NotNull(PasswordPolicy.Validate("short1", config));
    }

    [Fact]
    public void Validate_MeetsMinLength_ComplexityOff_ReturnsNull()
    {
        var config = new ServerConfig { PasswordMinLength = 8, PasswordRequireComplexity = false };
        Assert.Null(PasswordPolicy.Validate("alllowercase", config));
    }

    [Fact]
    public void Validate_NullOrEmpty_ReturnsError()
    {
        var config = new ServerConfig { PasswordMinLength = 8 };
        Assert.NotNull(PasswordPolicy.Validate(null, config));
        Assert.NotNull(PasswordPolicy.Validate("", config));
    }

    [Fact]
    public void Validate_ComplexityRequired_OnlyOneCharacterClass_ReturnsError()
    {
        var config = new ServerConfig { PasswordMinLength = 8, PasswordRequireComplexity = true };
        Assert.NotNull(PasswordPolicy.Validate("alllowercase", config));
    }

    [Fact]
    public void Validate_ComplexityRequired_ThreeCharacterClasses_ReturnsNull()
    {
        var config = new ServerConfig { PasswordMinLength = 8, PasswordRequireComplexity = true };
        Assert.Null(PasswordPolicy.Validate("Password1", config)); // upper, lower, digit
    }

    [Fact]
    public void Validate_ComplexityRequired_TwoCharacterClasses_ReturnsError()
    {
        var config = new ServerConfig { PasswordMinLength = 8, PasswordRequireComplexity = true };
        Assert.NotNull(PasswordPolicy.Validate("password", config)); // lower only
    }
}
