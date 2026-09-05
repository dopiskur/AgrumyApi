namespace api.Dal
{
    /// Builds a consistent { reason, message } response body for database failures instead of a bare false / raw exception message.
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

        /// HTTP status for a failure kind: 409 for a constraint violation, 500 for an unknown/unexpected error, otherwise 503.
        public static int StatusCodeFor(DbFailureKind kind) => kind switch
        {
            DbFailureKind.ConstraintViolation => 409,
            DbFailureKind.Unknown => 500,
            _ => 503
        };

        /// True if <paramref name="needle"/> appears in <paramref name="ex"/> or any inner exception - EF's DbException/DbUpdateException wrapping usually puts the useful text on an inner exception.
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
