using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using NLog;

namespace NzbDrone.Core.Datastore.SpacetimeDb
{
    public interface IOidcTokenProvider
    {
        string GetToken();
    }

    public class OidcTokenProvider : IOidcTokenProvider, IDisposable
    {
        private static readonly Logger Logger = LogManager.GetCurrentClassLogger();
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

        private readonly string _tokenEndpoint;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly HttpClient _httpClient;
        private readonly object _lock = new object();

        private string _cachedToken;
        private DateTime _tokenExpiry = DateTime.MinValue;

        public OidcTokenProvider(string tokenEndpoint, string clientId, string clientSecret)
        {
            _tokenEndpoint = tokenEndpoint;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        }

        public string GetToken()
        {
            lock (_lock)
            {
                if (_cachedToken != null && DateTime.UtcNow < _tokenExpiry - RefreshMargin)
                {
                    return _cachedToken;
                }

                Logger.Info("Obtaining OIDC token from {0} for client {1}", _tokenEndpoint, _clientId);

                var request = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = _clientId,
                    ["client_secret"] = _clientSecret,
                    ["scope"] = "openid"
                });

                var response = _httpClient.PostAsync(_tokenEndpoint, request).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    var body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    throw new InvalidOperationException(
                        $"OIDC token request failed ({response.StatusCode}): {body}");
                }

                var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                _cachedToken = root.GetProperty("access_token").GetString()
                    ?? root.GetProperty("id_token").GetString()
                    ?? throw new InvalidOperationException("OIDC token response contained neither access_token nor id_token");

                var expiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 3600;
                _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

                Logger.Info("OIDC token obtained, expires in {0}s", expiresIn);
                return _cachedToken;
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    public class NoOpTokenProvider : IOidcTokenProvider
    {
        public string GetToken() => null;
    }
}
