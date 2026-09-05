using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace api.Dal
{
    /// Forces the DB session's own timezone to UTC on every connection open, so a `CURRENT_TIMESTAMP`/`NOW()` column default computes in UTC regardless of the server process's OS timezone - verified live on invent.hr (MySQL @@global.time_zone is SYSTEM, 2h off UTC at the time this was found), which every app-level DateTime.UtcNow assumes it already is (roadmap #302).
    internal sealed class SessionTimeZoneInterceptor(string setTimeZoneSql) : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = setTimeZoneSql;
            cmd.ExecuteNonQuery();
        }

        public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = setTimeZoneSql;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
