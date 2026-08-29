namespace api.Dal
{
    /// <summary>
    /// Builds a consistent response body for database failures, so controllers can return the same
    /// shape ({ reason, message }) instead of a bare <c>false</c> / raw exception message.
    /// </summary>
    public static class DbErrorResponse
    {
        public static object For(DbFailureKind kind) => kind switch
        {
            DbFailureKind.SchemaMissing => new
            {
                reason = "schema_missing",
                message = "The database schema is not provisioned. Restart the service to auto-provision it, or contact the administrator."
            },
            DbFailureKind.ConstraintViolation => new
            {
                reason = "constraint_violation",
                message = "The request conflicts with an existing record or a referenced record does not exist."
            },
            DbFailureKind.Contention => new
            {
                reason = "contention",
                message = "The database is busy (deadlock or lock timeout). Please try again."
            },
            DbFailureKind.Unknown => new
            {
                reason = "server_error",
                message = "The service hit an unexpected error handling the request."
            },
            _ => new
            {
                reason = "connection_failure",
                message = "The service could not reach the database. Please try again later."
            }
        };

        /// <summary>HTTP status for a failure kind: 409 for a constraint violation, 500 for an unknown/unexpected error, otherwise 503.</summary>
        public static int StatusCodeFor(DbFailureKind kind) => kind switch
        {
            DbFailureKind.ConstraintViolation => 409,
            DbFailureKind.Unknown => 500,
            _ => 503
        };

        /// <summary>
        /// True if <paramref name="needle"/> appears in the message of <paramref name="ex"/> or any
        /// of its inner exceptions. EF wraps provider errors in <see cref="System.Data.Common.DbException"/>
        /// / DbUpdateException, so the useful text (unique-key names, "doesn't exist", ...) is usually
        /// on an inner exception, not the outer one.
        /// </summary>
        public static bool Mentions(Exception? ex, string needle)
        {
            for (Exception? e = ex; e != null; e = e.InnerException)
            {
                if (e.Message.Contains(needle, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
