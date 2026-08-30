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

        /// <summary>The caller's FIRST role claim - pre-#66 code (checks against literal "admin"/
        /// "user") keeps working unmodified because JwtTokenProvider.CreateToken always adds the
        /// legacy alias claim first. New code should prefer <see cref="CallerHasRole"/> or
        /// <see cref="CallerRoles"/> instead - a caller can hold several roles at once.</summary>
        protected string? CallerRole => (User.Identity as ClaimsIdentity)?.FindFirst(ClaimTypes.Role)?.Value;

        /// <summary>Every role claim on the caller's token (roadmap #66).</summary>
        protected IEnumerable<string> CallerRoles =>
            (User.Identity as ClaimsIdentity)?.FindAll(ClaimTypes.Role).Select(c => c.Value) ?? Enumerable.Empty<string>();

        /// <summary>True if the caller holds this exact role name, among possibly several.</summary>
        protected bool CallerHasRole(string roleName) => CallerRoles.Contains(roleName);
    }
}
