namespace api.Dal.Interface
{
    public static class RepoFactory
    {

        private static readonly Lazy<IRepository> repository = new(() => new SqlRepository());

        private static readonly Lazy<ICache> cache = new(() => new CacheRepository());

        private static readonly Lazy<IApi> api = new(() => new ApiRepository());
        public static IRepository GetRepo() => repository.Value;
        public static ICache GetCache() => cache.Value;
        public static IApi GetApi() => api.Value;
    }
}
