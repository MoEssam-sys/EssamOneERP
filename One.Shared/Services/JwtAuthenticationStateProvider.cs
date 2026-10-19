using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace One.Shared.Services
{
    /// <summary>
    /// Custom AuthenticationStateProvider that parses the JWT token from ApiService.AuthToken
    /// and provides claims (including roles) to Blazor's authorization infrastructure.
    /// </summary>
    public class JwtAuthenticationStateProvider : AuthenticationStateProvider
    {
        private readonly AuthenticationService _authService;

        public JwtAuthenticationStateProvider(AuthenticationService authService)
        {
            _authService = authService;
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var identity = new ClaimsIdentity();

            if (_authService.CheckAuthentication() && !string.IsNullOrEmpty(ApiService.AuthToken))
            {
                try
                {
                    var claims = ParseClaimsFromJwt(ApiService.AuthToken);
                    identity = new ClaimsIdentity(claims, "jwt");
                }
                catch
                {
                    // Token parsing failed, return anonymous
                    identity = new ClaimsIdentity();
                }
            }

            var user = new ClaimsPrincipal(identity);
            return Task.FromResult(new AuthenticationState(user));
        }

        /// <summary>
        /// Call this after login to notify Blazor that authentication state has changed.
        /// </summary>
        public void NotifyAuthenticationStateChanged()
        {
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        private static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>();

            var parts = jwt.Split('.');
            if (parts.Length < 2) return claims;

            var payload = DecodeBase64Url(parts[1]);
            using var doc = System.Text.Json.JsonDocument.Parse(payload);

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                // Handle role claims (can be string or array)
                if (prop.Name == "role" || prop.Name == ClaimTypes.Role ||
                    prop.Name == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                {
                    if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.Array)
                    {
                        foreach (var role in prop.Value.EnumerateArray())
                        {
                            claims.Add(new Claim(ClaimTypes.Role, role.GetString() ?? ""));
                        }
                    }
                    else
                    {
                        claims.Add(new Claim(ClaimTypes.Role, prop.Value.GetString() ?? ""));
                    }
                }
                // Handle name claims
                else if (prop.Name == "name" || prop.Name == "unique_name" || prop.Name == ClaimTypes.Name)
                {
                    claims.Add(new Claim(ClaimTypes.Name, prop.Value.GetString() ?? ""));
                }
                // Handle email claims
                else if (prop.Name == "email" || prop.Name == ClaimTypes.Email)
                {
                    claims.Add(new Claim(ClaimTypes.Email, prop.Value.GetString() ?? ""));
                }
                // Add other claims as-is
                else if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    claims.Add(new Claim(prop.Name, prop.Value.GetString() ?? ""));
                }
            }

            return claims;
        }

        private static string DecodeBase64Url(string input)
        {
            var output = input.Replace('-', '+').Replace('_', '/');
            switch (output.Length % 4)
            {
                case 2: output += "=="; break;
                case 3: output += "="; break;
            }
            return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(output));
        }
    }
}
