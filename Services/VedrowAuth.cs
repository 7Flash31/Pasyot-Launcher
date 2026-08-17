using Pasyot_Launcher.Models;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Pasyot_Launcher.Services
{
    internal class VedrowAuth
    {
        private const string ClientId = "vdr_7f515f1a54e6429693db795f";
        private const string VedrowBaseUrl = "https://vedrow.com";
        private const string VedrowApiUrl = "https://vedrow.com/api";
        private const string RedirectPath = "/callback/";

        public static event EventHandler<UserProfile>? OnAuthCompleted;

        private static HttpListener? _httpListener;

        public static async Task StartAuthAsync()
        {
            int port = GetFreePort();
            string dynamicRedirectUri = $"http://127.0.0.1:{port}{RedirectPath}";

            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);
            string state = Guid.NewGuid().ToString("N");
            string nonce = Guid.NewGuid().ToString("N");

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add(dynamicRedirectUri);
            _httpListener.Start();

            string authUrl = $"{VedrowBaseUrl}/oauth/authorize" +
                             $"?response_type=code" +
                             $"&client_id={Uri.EscapeDataString(ClientId)}" +
                             $"&redirect_uri={Uri.EscapeDataString(dynamicRedirectUri)}" +
                             $"&state={Uri.EscapeDataString(state)}" +
                             $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                             $"&code_challenge_method=S256" +
                             $"&nonce={Uri.EscapeDataString(nonce)}";

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            try
            {
                HttpListenerContext context = await _httpListener.GetContextAsync();
                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                string? code = request.QueryString["code"];
                string? returnedState = request.QueryString["state"];

                if (returnedState != state)
                {
                    ShowHtmlResponse(response, "Ошибка: Не совпал параметр state.");
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    ShowHtmlResponse(response, "Ошибка: Авторизация была отменена или код не получен.");
                    return;
                }

                ShowHtmlResponse(response, "<h2>Авторизация успешна!</h2><p>Можете закрыть эту вкладку и вернуться в приложение.</p>");

                await ExchangeCodeForTokenAsync(code, codeVerifier, dynamicRedirectUri);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка в процессе авторизации: {ex.Message}");
            }
            finally
            {
                StopServer();
            }
        }

        private static async Task ExchangeCodeForTokenAsync(string code, string codeVerifier, string redirectUri)
        {
            using (HttpClient client = new HttpClient())
            {
                var bodyParams = new Dictionary<string, string>
                {
                    { "grant_type", "authorization_code" },
                    { "client_id", ClientId },
                    { "code", code },
                    { "redirect_uri", redirectUri },
                    { "code_verifier", codeVerifier }
                };

                var content = new FormUrlEncodedContent(bodyParams);
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage tokenResponse = await client.PostAsync($"{VedrowApiUrl}/oauth/token", content);
                string responseJson = await tokenResponse.Content.ReadAsStringAsync();

                if (!tokenResponse.IsSuccessStatusCode) return;

                using (JsonDocument doc = JsonDocument.Parse(responseJson))
                {
                    JsonElement root = doc.RootElement;
                    string accessToken = root.GetProperty("access_token").GetString()!;
                    string refreshToken = root.TryGetProperty("refresh_token", out var rt) ? rt.GetString()! : string.Empty;

                    UserProfile? profile = await FetchUserInfoAsync(accessToken);
                    if (profile != null)
                    {
                        AuthService.SaveSession(accessToken, refreshToken, profile);
                        OnAuthCompleted?.Invoke(null, profile);
                    }
                }
            }
        }

        public static async Task<string?> RefreshAccessTokenAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) return null;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    var bodyParams = new Dictionary<string, string>
                    {
                        { "grant_type", "refresh_token" },
                        { "client_id", ClientId },
                        { "refresh_token", refreshToken }
                    };

                    var content = new FormUrlEncodedContent(bodyParams);
                    HttpResponseMessage response = await client.PostAsync($"{VedrowApiUrl}/oauth/token", content);

                    if (!response.IsSuccessStatusCode) return null;

                    string json = await response.Content.ReadAsStringAsync();
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        JsonElement root = doc.RootElement;
                        return root.GetProperty("access_token").GetString();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        public static async Task<(UserProfile? Profile, bool IsInvalidSession)> ValidateAndGetProfileAsync(UserSession session)
        {
            if (session == null || string.IsNullOrWhiteSpace(session.AccessToken))
                return (null, true);

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

                try
                {
                    HttpResponseMessage response = await client.GetAsync($"{VedrowApiUrl}/oauth/userinfo");

                    if (response.StatusCode == HttpStatusCode.Unauthorized && !string.IsNullOrEmpty(session.RefreshToken))
                    {
                        string? newAccessToken = await RefreshAccessTokenAsync(session.RefreshToken);
                        if (!string.IsNullOrEmpty(newAccessToken))
                        {
                            UserProfile? refreshedProfile = await FetchUserInfoAsync(newAccessToken);
                            if (refreshedProfile != null)
                            {
                                AuthService.SaveSession(newAccessToken, session.RefreshToken, refreshedProfile);
                                return (refreshedProfile, false);
                            }
                        }
                        return (null, true);
                    }

                    if (!response.IsSuccessStatusCode)
                        return (null, false);

                    string json = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var profile = JsonSerializer.Deserialize<UserProfile>(json, options);

                    return (profile, false);
                }
                catch (HttpRequestException)
                {
                    return (null, false);
                }
                catch
                {
                    return (null, false);
                }
            }
        }

        private static async Task<UserProfile?> FetchUserInfoAsync(string accessToken)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                HttpResponseMessage response = await client.GetAsync($"{VedrowApiUrl}/oauth/userinfo");

                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<UserProfile>(json, options);
            }
        }

        private static void ShowHtmlResponse(HttpListenerResponse response, string htmlMessage)
        {
            string html = $"<html><head><meta charset='utf-8'></head><body>{htmlMessage}</body></html>";
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentLength64 = buffer.Length;
            using (Stream output = response.OutputStream)
            {
                output.Write(buffer, 0, buffer.Length);
            }
        }

        private static void StopServer()
        {
            if (_httpListener != null && _httpListener.IsListening)
            {
                _httpListener.Stop();
                _httpListener.Close();
            }
        }

        private static string GenerateCodeVerifier()
        {
            byte[] bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Base64UrlEncode(bytes);
        }

        private static string Base64UrlEncode(byte[] input)
        {
            return Convert.ToBase64String(input)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] challengeBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Base64UrlEncode(challengeBytes);
            }
        }
    }
}