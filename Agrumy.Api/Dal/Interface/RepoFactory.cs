using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Agrumy.Api.Tests")]

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

        private static IRepository? _repoOverride;
        private static ICache? _cacheOverride;

        public static IRepository GetRepo() => _repoOverride ?? repository.Value;
        public static ICache GetCache() => _cacheOverride ?? cache.Value;

        /// <summary>Test-only seam: swap in a mock IRepository / ICache. Pass null to restore the real one.</summary>
        internal static void OverrideForTests(IRepository? repo = null, ICache? cacheImpl = null)
        {
            _repoOverride = repo;
            _cacheOverride = cacheImpl;
        }
    }
}
