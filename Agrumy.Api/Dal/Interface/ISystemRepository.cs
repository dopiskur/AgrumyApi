namespace api.Dal.Interface
{
    /// <summary>Startup/health facet of the data layer - the only part infrastructure like
    /// DbExceptionFilter and the startup DB check needs.</summary>
    public interface ISystemRepository
    {
        /// <summary>Opens and immediately closes a database connection. Returns true if the connection could be opened.</summary>
        Task<bool> TestConnectionAsync();

        /// <summary>Ensures the schema exists: on an empty database, applies the EF Core baseline migration; a database that already has tables is left untouched.</summary>
        Task EnsureSchemaAsync();

        /// <summary>Classifies a database-layer exception so callers can return a consistent error response. CPU-only, stays synchronous.</summary>
        DbFailureKind ClassifyException(Exception ex);
    }
}
