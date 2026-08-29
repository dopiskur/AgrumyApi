using System.Net.Http.Headers;
using Microsoft.AspNetCore.Authentication;

namespace api.Security
{
    /// <summary>
    /// Attaches the signed-in user's JWT (stashed in the auth-cookie ticket by
    /// <c>LoginController</c> via <c>props.StoreTokens</c>) as a <c>Bearer</c> header on every
    /// outgoing Agrumy.Api call. Lets the Refit interface stay free of an explicit token parameter.
    /// </summary>
    public sealed class BearerTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var http = accessor.HttpContext;
            if (http is not null)
            {
                var token = await http.GetTokenAsync("access_token").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(token))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                }
            }

            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
    }
}
