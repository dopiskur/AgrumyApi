using System.Net;
using System.Net.Http.Headers;
using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace api.Security
{
    /// <summary>
    /// Attaches the signed-in user's JWT (stashed in the auth-cookie ticket by <c>LoginController</c>
    /// via <c>props.StoreTokens</c>) as a <c>Bearer</c> header on every outgoing Agrumy.Api call.
    ///
    /// The API issues a ~2h access token but the auth cookie lives 7 days, so an expired access
    /// token is the common case, not an error: on a 401 this handler redeems the stored refresh
    /// token for a new access+refresh pair (via <see cref="RefreshCoordinator"/>, so concurrent
    /// requests don't each burn the single-use refresh token), re-signs the cookie in with the new
    /// tokens, and retries the original call once. If the refresh itself fails (refresh token also
    /// expired/revoked/reused), the original 401 is returned unchanged and
    /// <see cref="api.Filters.ApiAuthExceptionFilter"/> sends the user back to login.
    /// </summary>
    public sealed class BearerTokenHandler(
        IHttpContextAccessor accessor, IAuthApi authApi, RefreshCoordinator refreshCoordinator)
        : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var http = accessor.HttpContext;
            string? accessToken = http is null ? null : await http.GetTokenAsync("access_token").ConfigureAwait(false);
            ApplyBearer(request, accessToken);

            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.Unauthorized || http is null)
            {
                return response;
            }

            string? refreshToken = await http.GetTokenAsync("refresh_token").ConfigureAwait(false);
            if (string.IsNullOrEmpty(refreshToken))
            {
                return response; // nothing to refresh with - let the 401 surface as-is
            }

            var refreshed = await refreshCoordinator.RefreshAsync(
                refreshToken,
                stale => RedeemAsync(stale, cancellationToken),
                cancellationToken).ConfigureAwait(false);

            if (refreshed is null)
            {
                return response; // refresh token itself is dead - ApiAuthExceptionFilter handles this 401
            }

            await PersistRefreshedTokensAsync(http, refreshed.Value.AccessToken, refreshed.Value.RefreshToken)
                .ConfigureAwait(false);

            response.Dispose();
            var retryRequest = await CloneAsync(request).ConfigureAwait(false);
            ApplyBearer(retryRequest, refreshed.Value.AccessToken);
            return await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
        }

        private async Task<(string AccessToken, string RefreshToken)?> RedeemAsync(string staleRefreshToken, CancellationToken ct)
        {
            try
            {
                UserLoginResult result = await authApi
                    .RefreshToken(new RefreshTokenRequest { RefreshToken = staleRefreshToken }, ct)
                    .ConfigureAwait(false);
                return result.Token is null || result.RefreshToken is null
                    ? null
                    : (result.Token, result.RefreshToken);
            }
            catch (ApiException)
            {
                return null;
            }
        }

        private static void ApplyBearer(HttpRequestMessage request, string? token)
        {
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }
        }

        private static async Task PersistRefreshedTokensAsync(HttpContext http, string accessToken, string refreshToken)
        {
            var auth = await http.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme).ConfigureAwait(false);
            if (!auth.Succeeded || auth.Principal is null || auth.Properties is null)
            {
                return;
            }

            auth.Properties.StoreTokens(new[]
            {
                new AuthenticationToken { Name = "access_token", Value = accessToken },
                new AuthenticationToken { Name = "refresh_token", Value = refreshToken },
            });
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, auth.Principal, auth.Properties)
                .ConfigureAwait(false);
        }

        /// <summary>An HttpRequestMessage can only be sent once - HttpClient disposes it after the
        /// first send - so retrying needs a fresh clone of method/URI/headers/body.</summary>
        private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            if (request.Content is not null)
            {
                byte[] bytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                clone.Content = new ByteArrayContent(bytes);
                foreach (var header in request.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }
            return clone;
        }
    }
}
