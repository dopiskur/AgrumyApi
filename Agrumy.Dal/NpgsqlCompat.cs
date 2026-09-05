using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace api.Dal
{
    internal static class NpgsqlCompat
    {
        /// Runs before any Npgsql type mapping is initialised - opts back into pre-6.0 behavior (DateTime maps to timestamp without time zone, any DateTimeKind accepted) since Agrumy stores naive local datetimes everywhere (legacy MySQL datetime).
        [ModuleInitializer]
        [SuppressMessage("Usage", "CA2255", Justification = "Deliberate: Agrumy.Dal is a library, but this switch must be set before any Npgsql type mapping runs, and there is no earlier library-safe hook than a module initializer.")]
        internal static void Init()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }
    }
}
