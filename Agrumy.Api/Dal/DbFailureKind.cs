namespace api.Dal
{
    /// Coarse classification of a database-layer failure, used to shape a consistent API response.
    public enum DbFailureKind
    {
        /// The database could not be reached / the connection failed.
        ConnectionFailure,

        /// The database is reachable but a required table or stored routine is missing.
        SchemaMissing,

        /// A FK / check / unique constraint was violated (and DbExceptionFilter did not name it specifically).
        ConstraintViolation,

        /// A deadlock or lock-wait timeout - transient, the caller can retry.
        Contention,

        /// Not a recognised database failure - an unexpected server-side error (HTTP 500, not 503).
        Unknown
    }
}
