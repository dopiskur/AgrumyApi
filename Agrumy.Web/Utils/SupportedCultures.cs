namespace api.Utils
{
    /// <summary>UI languages the admin panel offers (roadmap #237) - only the display language, never CultureInfo.CurrentCulture, which Program.cs pins to Invariant so numeric form fields keep parsing "8.2" correctly regardless of the visitor's chosen language.</summary>
    public static class SupportedCultures
    {
        public const string Default = "en";

        public static readonly string[] All = ["en", "hr"];
    }
}
