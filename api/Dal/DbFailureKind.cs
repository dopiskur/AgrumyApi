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
        SchemaMissing
    }
}
