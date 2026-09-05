using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using api.Dal.Interface;
using api.Models;
using api.Utils;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace api.Security
{
    // Access token lives ~2h, the auth cookie 7 days: on a 401, redeem the refresh token via RefreshCoordinator, re-sign the cookie, and retry once.
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
            // WWW-Authenticate is set only by the JWT bearer challenge itself (missing/invalid/expired/revoked token) - an [Authorize]'d action returning 401 for its own business reason (e.g. a wrong old-password check) never sets it, so this alone should not burn a refresh-token rotation.
            if (response.StatusCode != HttpStatusCode.Unauthorized || http is null || response.Headers.WwwAuthenticate.Count == 0)
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

            // Rebuild role claims from the fresh token (not the stale cookie) so a role change propagates on refresh; keep the old principal on any parsing hiccup.
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                RebuildPrincipalFromToken(auth.Principal, accessToken), auth.Properties).ConfigureAwait(false);
        }

        private static ClaimsPrincipal RebuildPrincipalFromToken(ClaimsPrincipal current, string accessToken)
        {
            // Trusted already - this token just came back from RedeemAsync's own HTTPS call to Agrumy.Api, so no shared signing key is needed here.
            IReadOnlyList<string>? roles = JwtTokenProvider.DecodeRolesWithoutVerification(accessToken);
            if (roles is null || roles.Count == 0)
            {
                return current;
            }

            var claims = new List<Claim> { new(ClaimTypes.Name, current.Identity?.Name ?? "") };
            claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
            // Not in the JWT itself - carry it forward from the cookie instead of round-tripping to Agrumy.Api just to refresh it.
            if (current.GetTimeZone() is { } timeZone)
            {
                claims.Add(new Claim(UserClaims.TimeZone, timeZone));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme));
        }

        // HttpRequestMessage can only be sent once (HttpClient disposes it after), so retrying needs a fresh clone.
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
