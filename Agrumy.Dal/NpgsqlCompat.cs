using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace api.Dal
{
    internal static class NpgsqlCompat
    {
        /// <summary>
        /// Runs when Agrumy.Dal loads, before any Npgsql type mapping is initialised. The Agrumy
        /// schema stores naive local datetimes everywhere (legacy MySQL <c>datetime</c>), so opt
        /// back into Npgsql's pre-6.0 behaviour: <c>DateTime</c> maps to
        /// <c>timestamp without time zone</c> and any <see cref="System.DateTimeKind"/> is accepted,
        /// instead of the UTC-only <c>timestamp with time zone</c> default.
        /// </summary>
        [ModuleInitializer]
        [SuppressMessage("Usage", "CA2255", Justification = "Deliberate: Agrumy.Dal is a library, but this switch must be set before any Npgsql type mapping runs, and there is no earlier library-safe hook than a module initializer.")]
        internal static void Init()
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        }
    }
}
