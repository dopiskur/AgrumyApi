namespace api.Dal.Interface
{
    /// <summary>
    /// Agrumy.Api variant: exposes the data-access and cache repositories only.
    /// (The MVC/View app has its own RepoFactory that exposes the HTTP-backed IApi instead.)
    /// </summary>
    public static class RepoFactory
    {

        private static readonly Lazy<IRepository> repository = new(() => new SqlRepository());

        private static readonly Lazy<ICache> cache = new(() => new CacheRepository());

        public static IRepository GetRepo() => repository.Value;
        public static ICache GetCache() => cache.Value;
    }
}
