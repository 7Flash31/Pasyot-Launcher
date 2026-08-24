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
            string dynamicRedirectUri = StartListenerOnFreePort();

            string codeVerifier = GenerateCodeVerifier();
            string codeChallenge = GenerateCodeChallenge(codeVerifier);
            string state = Guid.NewGuid().ToString("N");
            string nonce = Guid.NewGuid().ToString("N");

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
                HttpListenerContext? context = null;
                for (int attempt = 0; attempt < 10 && context == null; attempt++)
                {
                    HttpListenerContext candidate = await _httpListener!.GetContextAsync();
                    if (candidate.Request.Url?.AbsolutePath == RedirectPath)
                    {
                        context = candidate;
                    }
                    else
                    {
                        candidate.Response.StatusCode = 404;
                        candidate.Response.Close();
                    }
                }

                if (context == null)
                {
                    MessageBox.Show("Не удалось получить ответ авторизации.");
                    return;
                }

                HttpListenerRequest request = context.Request;
                HttpListenerResponse response = context.Response;

                string? code = request.QueryString["code"];
                string? returnedState = request.QueryString["state"];

                if (returnedState != state)
                {
                    ShowHtmlResponse(response, false, "Ошибка авторизации",
                        "Не совпал параметр state. Попробуйте войти ещё раз в приложении.");
                    return;
                }

                if (string.IsNullOrEmpty(code))
                {
                    ShowHtmlResponse(response, false, "Авторизация отменена",
                        "Код авторизации не получен. Попробуйте войти ещё раз в приложении.");
                    return;
                }

                ShowHtmlResponse(response, true, "Авторизация успешна",
                    "Можете закрыть эту вкладку и вернуться в приложение.");

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

                    if (response.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        if (!string.IsNullOrEmpty(session.RefreshToken))
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

        private static void ShowHtmlResponse(HttpListenerResponse response, bool isSuccess, string title, string message)
        {
            string html = BuildAuthResultPage(isSuccess, title, message);
            byte[] buffer = Encoding.UTF8.GetBytes(html);
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            using (Stream output = response.OutputStream)
            {
                output.Write(buffer, 0, buffer.Length);
            }
        }

        private static string BuildAuthResultPage(bool isSuccess, string title, string message)
        {
            string accent = isSuccess ? "#22C55E" : "#EF4444";
            string icon = isSuccess
                ? "<svg viewBox=\"0 0 24 24\" width=\"30\" height=\"30\" fill=\"none\" stroke=\"white\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><polyline points=\"20 6 9 17 4 12\"/></svg>"
                : "<svg viewBox=\"0 0 24 24\" width=\"30\" height=\"30\" fill=\"none\" stroke=\"white\" stroke-width=\"2.5\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><line x1=\"18\" y1=\"6\" x2=\"6\" y2=\"18\"/><line x1=\"6\" y1=\"6\" x2=\"18\" y2=\"18\"/></svg>";

            return $$"""
            <!DOCTYPE html>
            <html lang="ru">
            <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1" />
                <title>Pasyot Launcher</title>
                <style>
                    :root { color-scheme: dark; }
                    * { box-sizing: border-box; }
                    body {
                        margin: 0;
                        min-height: 100vh;
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        background: radial-gradient(circle at top, #1f1f22 0%, #0e0e10 65%);
                        font-family: "Segoe UI", system-ui, -apple-system, sans-serif;
                        color: #ffffff;
                    }
                    .card {
                        width: min(420px, 90vw);
                        background: #1c1c1e;
                        border: 1px solid #2a2a2d;
                        border-radius: 16px;
                        padding: 40px 32px;
                        text-align: center;
                        box-shadow: 0 20px 60px rgba(0, 0, 0, 0.45);
                        animation: rise 0.35s ease-out;
                    }
                    .icon {
                        width: 64px;
                        height: 64px;
                        margin: 0 auto 20px;
                        border-radius: 50%;
                        background: {{accent}};
                        display: flex;
                        align-items: center;
                        justify-content: center;
                        box-shadow: 0 0 0 6px {{accent}}22;
                    }
                    h1 {
                        font-size: 20px;
                        margin: 0 0 8px;
                        font-weight: 600;
                    }
                    p {
                        font-size: 14px;
                        color: #a1a1aa;
                        line-height: 1.5;
                        margin: 0;
                    }
                    .brand {
                        margin-top: 28px;
                        font-size: 11px;
                        letter-spacing: 0.08em;
                        text-transform: uppercase;
                        color: #55555a;
                    }
                    .countdown {
                        margin-top: 14px;
                        font-size: 12px;
                        color: #71717a;
                    }
                    .close-btn {
                        margin-top: 16px;
                        display: none;
                        border: 1px solid #33333a;
                        background: #232326;
                        color: #ffffff;
                        font-size: 13px;
                        padding: 9px 18px;
                        border-radius: 8px;
                        cursor: pointer;
                        font-family: inherit;
                    }
                    .close-btn:hover { background: #2b2b2f; }
                    @keyframes rise {
                        from { opacity: 0; transform: translateY(8px); }
                        to { opacity: 1; transform: translateY(0); }
                    }
                </style>
            </head>
            <body>
                <div class="card">
                    <div class="icon">{{icon}}</div>
                    <h1>{{title}}</h1>
                    <p>{{message}}</p>
                    <p class="countdown" id="countdown">Окно закроется через 5 секунд…</p>
                    <button class="close-btn" id="closeBtn" onclick="tryClose()">Закрыть окно</button>
                    <div class="brand">Pasyot Launcher</div>
                </div>
                <script>
                    var secondsLeft = 5;
                    var countdownEl = document.getElementById('countdown');
                    var closeBtn = document.getElementById('closeBtn');

                    function tryClose() {
                        try { window.close(); } catch (e) {}
                        try { window.open('', '_self', ''); window.close(); } catch (e) {}
                    }

                    var timer = setInterval(function () {
                        secondsLeft--;
                        if (secondsLeft > 0) {
                            countdownEl.textContent = 'Окно закроется через ' + secondsLeft + ' сек…';
                        } else {
                            clearInterval(timer);
                            countdownEl.textContent = 'Можно закрыть это окно.';
                            tryClose();
                            setTimeout(function () { closeBtn.style.display = 'inline-block'; }, 400);
                        }
                    }, 1000);
                </script>
            </body>
            </html>
            """;
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

        private static string StartListenerOnFreePort()
        {
            const int maxAttempts = 5;

            for (int i = 0; i < maxAttempts; i++)
            {
                int port = GetFreePort();
                string redirectUri = $"http://127.0.0.1:{port}{RedirectPath}";
                var listener = new HttpListener();
                listener.Prefixes.Add(redirectUri);

                try
                {
                    listener.Start();
                    _httpListener = listener;
                    return redirectUri;
                }
                catch (HttpListenerException)
                {
                    listener.Close();
                }
            }

            throw new InvalidOperationException("Не удалось запустить локальный сервер для авторизации.");
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