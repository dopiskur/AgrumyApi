namespace api.Utils
{
    /// UI languages the admin panel offers - only the display language, never CultureInfo.CurrentCulture, which Program.cs pins to Invariant so numeric form fields keep parsing "8.2" correctly regardless of the visitor's chosen language.
    public static class SupportedCultures
    {
        public const string Default = "en";

        public static readonly string[] All = ["en", "hr"];
    }
}
