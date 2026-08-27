namespace api.Dal.Interface
{
    /// <summary>
    /// Agrumy.Web variant: exposes only the HTTP-backed API client (IApi / ApiRepository).
    /// The View layer never touches the database directly - it always goes through Agrumy.Api over HTTP.
    /// </summary>
    public static class RepoFactory
    {

        private static readonly Lazy<IApi> api = new(() => new ApiRepository());

        public static IApi GetApi() => api.Value;
    }
}
