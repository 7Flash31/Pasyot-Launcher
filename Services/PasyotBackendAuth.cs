using Pasyot_Launcher.Models;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Services
{
    internal static class PasyotBackendAuth
    {
        public const string BaseUrl = "http://26.75.134.108:8081";

        private static readonly HttpClient HttpClient = new HttpClient();

        public static BackendUser? CurrentUser { get; private set; }

        public static async Task<BackendUser?> EnsureAuthenticatedAsync()
        {
            string token = AuthService.BackendSessionToken;
            if (!string.IsNullOrEmpty(token))
            {
                var user = await FetchMeAsync(token);
                if (user != null)
                {
                    CurrentUser = user;
                    return user;
                }
            }

            return await ExchangeAsync();
        }

        public static async Task<BackendUser?> ExchangeAsync()
        {
            if (!AuthService.IsLoggedIn) return null;

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/auth/vedrow/native")
                {
                    Content = JsonContent.Create(new { access_token = AuthService.AccessToken })
                };

                HttpResponseMessage response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var result = await response.Content.ReadFromJsonAsync<NativeLoginResult>(options);
                if (result == null || string.IsNullOrEmpty(result.SessionToken)) return null;

                AuthService.SaveBackendSessionToken(result.SessionToken);
                CurrentUser = result.User;
                return result.User;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PasyotBackendAuth] {ex}");
                return null;
            }
        }

        private static async Task<BackendUser?> FetchMeAsync(string token)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/auth/me");
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response = await HttpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return await response.Content.ReadFromJsonAsync<BackendUser>(options);
            }
            catch
            {
                return null;
            }
        }

        private class NativeLoginResult
        {
            [JsonPropertyName("session_token")]
            public string SessionToken { get; set; } = "";

            [JsonPropertyName("user")]
            public BackendUser? User { get; set; }
        }
    }
}
