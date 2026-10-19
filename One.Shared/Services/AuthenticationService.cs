using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace One.Shared.Services
{
    public class AuthenticationService
    {
        private bool IsAuthenticated = false;
        public bool CheckAuthentication() => IsAuthenticated;
        public void SetAuthentication(bool value) => IsAuthenticated = value;

        public string? GetClaim(string claimType)
        {
            if (string.IsNullOrEmpty(One.Shared.Services.ApiService.AuthToken)) return null;
            try
            {
                var parts = One.Shared.Services.ApiService.AuthToken.Split('.');
                if (parts.Length < 2) return null;
                var payload = DecodeBase64(parts[1]);
                using var doc = System.Text.Json.JsonDocument.Parse(payload);
                if (doc.RootElement.TryGetProperty(claimType, out var elem))
                {
                    return elem.ToString();
                }
            }
            catch { }
            return null;
        }

        private string DecodeBase64(string input)
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
