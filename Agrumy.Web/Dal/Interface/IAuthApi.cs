using api.Models;
using Refit;

namespace api.Dal.Interface
{
    /// <summary>
    /// Refresh/revoke calls for the stored JWT. Registered as its own Refit client, deliberately
    /// WITHOUT <see cref="api.Security.BearerTokenHandler"/>: these calls authenticate by possessing
    /// the refresh token itself, not a (possibly already-expired) access token, and must not route
    /// through the handler that calls them - that would risk recursing back into itself.
    /// </summary>
    public interface IAuthApi
    {
        [Post("/api/User/RefreshToken")]
        Task<UserLoginResult> RefreshToken([Body] RefreshTokenRequest request, CancellationToken ct = default);

        [Post("/api/User/RevokeRefreshToken")]
        Task RevokeRefreshToken([Body] RefreshTokenRequest request);
    }
}
