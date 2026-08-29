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
            _ => new
            {
                reason = "connection_failure",
                message = "The service could not reach the database. Please try again later."
            }
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
