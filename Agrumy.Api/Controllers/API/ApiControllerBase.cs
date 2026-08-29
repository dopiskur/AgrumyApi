using api.Dal.Interface;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace api.Controllers.API
{
    /// <summary>
    /// Shared base for the JSON API controllers: constructor-injected data-access + cache, and the
    /// caller's identity read off the JWT. Data-access exceptions are turned into responses by the
    /// global <see cref="api.Filters.DbExceptionFilter"/>, so actions don't catch them individually.
    /// </summary>
    [ApiController]
    public abstract class ApiControllerBase : ControllerBase
    {
        protected IRepository Repo { get; }
        protected ICache Cache { get; }

        protected ApiControllerBase(IRepository repo, ICache cache)
        {
            Repo = repo;
            Cache = cache;
        }

        /// <summary>TenantID claim set at login (JwtTokenProvider.CreateToken), or null if absent.</summary>
        protected int? CallerTenantId
        {
            get
            {
                var claim = (User.Identity as ClaimsIdentity)?.FindFirst("TenantID");
                return claim != null && int.TryParse(claim.Value, out var id) ? id : null;
            }
        }

        /// <summary>The caller's role claim ("admin" / "user"), or null.</summary>
        protected string? CallerRole => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.Role)?.Value;
    }
}
