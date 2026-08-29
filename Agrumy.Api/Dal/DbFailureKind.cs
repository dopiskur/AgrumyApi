namespace api.Dal
{
    /// <summary>
    /// Coarse classification of a database-layer failure, used to shape a consistent API response.
    /// </summary>
    public enum DbFailureKind
    {
        /// <summary>The database could not be reached / the connection failed.</summary>
        ConnectionFailure,

        /// <summary>The database is reachable but a required table or stored routine is missing.</summary>
        SchemaMissing,

        /// <summary>A FK / check / unique constraint was violated (and DbExceptionFilter did not name it specifically).</summary>
        ConstraintViolation,

        /// <summary>A deadlock or lock-wait timeout - transient, the caller can retry.</summary>
        Contention,

        /// <summary>Not a recognised database failure - an unexpected server-side error (HTTP 500, not 503).</summary>
        Unknown
    }
}
