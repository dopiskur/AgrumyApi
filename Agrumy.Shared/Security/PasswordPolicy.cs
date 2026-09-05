using api.Models;

namespace api.Security
{
    /// Minimal, server-configurable password policy (ServerConfig.PasswordMinLength/PasswordRequireComplexity) - checked wherever a NEW password is set, never against an existing hash being verified.
    public static class PasswordPolicy
    {
        public static string? Validate(string? password, ServerConfig config)
        {
            if (string.IsNullOrEmpty(password) || password.Length < config.PasswordMinLength)
            {
                return $"Password must be at least {config.PasswordMinLength} characters long.";
            }
            if (config.PasswordRequireComplexity && CharacterClassCount(password) < 3)
            {
                return "Password must contain at least 3 of: uppercase letter, lowercase letter, digit, symbol.";
            }
            return null;
        }

        private static int CharacterClassCount(string password)
        {
            int count = 0;
            if (password.Any(char.IsUpper)) { count++; }
            if (password.Any(char.IsLower)) { count++; }
            if (password.Any(char.IsDigit)) { count++; }
            if (password.Any(c => !char.IsLetterOrDigit(c))) { count++; }
            return count;
        }
    }
}
