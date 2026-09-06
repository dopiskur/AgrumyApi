using api.Models;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Npgsql;

namespace api.Dal
{
    /// Pure exception-to-DbFailureKind mapping, pulled out of EfRepository (roadmap #246) so domain repositories (EfCommandRepository, EfRepository.Devices.Diagnostics.cs) can classify a caught exception without depending on the whole ISystemRepository facet.
    internal static class DbExceptionClassifier
    {
        internal static DbFailureKind Classify(Exception ex)
        {
            Exception inner = ex is DbUpdateException due && due.InnerException != null ? due.InnerException : ex;

            // 1146/1051/1305 = table/routine missing; 1216/1217/1451/1452 = FK violation; 1062 = duplicate key; 1213/1205 = deadlock/lock-wait timeout.
            if (inner is MySqlException mysqlEx)
            {
                switch (mysqlEx.Number)
                {
                    case 1146:
                    case 1051:
                    case 1305:
                        return DbFailureKind.SchemaMissing;
                    case 1216:
                    case 1217:
                    case 1451:
                    case 1452:
                    case 1062:
                        return DbFailureKind.ConstraintViolation;
                    case 1213:
                    case 1205:
                        return DbFailureKind.Contention;
                }
                // Any other MySql error still reached the server but failed - treat as a connection-level failure.
                return DbFailureKind.ConnectionFailure;
            }

            // 42P01/42703/3F000 = missing table/column/schema; 23503/23505/23514 = FK/unique/check violation; 40P01/40001/55P03 = deadlock/serialization failure/lock unavailable.
            if (inner is PostgresException pgEx)
            {
                switch (pgEx.SqlState)
                {
                    case "42P01":
                    case "42703":
                    case "3F000":
                        return DbFailureKind.SchemaMissing;
                    case "23503":
                    case "23505":
                    case "23514":
                        return DbFailureKind.ConstraintViolation;
                    case "40P01":
                    case "40001":
                    case "55P03":
                        return DbFailureKind.Contention;
                }
                return DbFailureKind.ConnectionFailure;
            }

            // MySQL text fallback for a missing table when the exception type isn't MySqlException - PostgreSQL's equivalent is already covered by the 42P01 SqlState above.
            if (DbErrorResponse.Mentions(ex, "doesn't exist") ||
                DbErrorResponse.Mentions(ex, "Unknown table"))
            {
                return DbFailureKind.SchemaMissing;
            }

            // Genuine transport-level failures still mean "can't reach the DB, retry later" (503).
            if (ex is TimeoutException or System.Net.Sockets.SocketException or System.Data.Common.DbException ||
                inner is TimeoutException or System.Net.Sockets.SocketException or System.Data.Common.DbException)
            {
                return DbFailureKind.ConnectionFailure;
            }

            // Anything else escaping an action is a server-side bug, not a database outage - surface it as 500, not a misleading 503.
            return DbFailureKind.Unknown;
        }
    }
}
