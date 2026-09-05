namespace api.Dal.Interface
{
    /// Startup/health facet of the data layer - the only part infrastructure like DbExceptionFilter and the startup DB check needs.
    public interface ISystemRepository
    {
        /// Opens and immediately closes a database connection; returns true if it could be opened.
        Task<bool> TestConnectionAsync();

        /// On an empty database applies the EF Core baseline migration; a database that already has tables is left untouched.
        Task EnsureSchemaAsync();

        /// Classifies a database-layer exception so callers can return a consistent error response.
        DbFailureKind ClassifyException(Exception ex);
    }
}
