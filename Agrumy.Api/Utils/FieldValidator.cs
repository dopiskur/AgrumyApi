using System.Text.RegularExpressions;

namespace api.Utils
{
    public static partial class FieldValidator
    {
        public static bool IsValidEmail(string? email) => email is not null && EmailRegex().IsMatch(email);

        [GeneratedRegex(
            @"\A(?:[a-z0-9!#$%&'*+/=?^_`{|}~-]+(?:\.[a-z0-9!#$%&'*+/=?^_`{|}~-]+)*@(?:[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.)+[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)\Z",
            RegexOptions.IgnoreCase)]
        private static partial Regex EmailRegex();
    }
}
