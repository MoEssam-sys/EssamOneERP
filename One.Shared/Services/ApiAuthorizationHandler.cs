using System.Net.Http.Headers;

namespace One.Shared.Services;

public sealed class ApiAuthorizationHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(ApiService.AuthToken))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", ApiService.AuthToken);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
