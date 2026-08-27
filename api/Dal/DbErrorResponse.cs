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
    }
}
