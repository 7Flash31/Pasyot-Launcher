using Pasyot_Launcher.Models;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Services
{
    public class SkinService
    {
        private readonly HttpClient _httpClient;
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

        public SkinService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public static string TextureUrl(string username) =>
            $"{PasyotBackendAuth.BaseUrl}/skins/{Uri.EscapeDataString(username)}.png?t={DateTime.UtcNow.Ticks}";

        public async Task<SkinInfo?> GetMySkinAsync(string token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, $"{PasyotBackendAuth.BaseUrl}/skins/me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<SkinInfo>(JsonOptions);
        }

        public async Task<SkinInfo> UploadSkinAsync(string token, string filePath, string model)
        {
            byte[] bytes = await File.ReadAllBytesAsync(filePath);

            using var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            content.Add(fileContent, "file", Path.GetFileName(filePath));
            content.Add(new StringContent(model), "model");

            var request = new HttpRequestMessage(HttpMethod.Post, $"{PasyotBackendAuth.BaseUrl}/skins") { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception(await ErrorMessageAsync(response));

            return (await response.Content.ReadFromJsonAsync<SkinInfo>(JsonOptions))!;
        }

        public async Task<SkinInfo> ImportSkinAsync(string token, string username)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, $"{PasyotBackendAuth.BaseUrl}/skins/import")
            {
                Content = JsonContent.Create(new { username })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            HttpResponseMessage response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception(await ErrorMessageAsync(response));

            return (await response.Content.ReadFromJsonAsync<SkinInfo>(JsonOptions))!;
        }

        private static async Task<string> ErrorMessageAsync(HttpResponseMessage response)
        {
            string body = await response.Content.ReadAsStringAsync();
            return string.IsNullOrWhiteSpace(body) ? $"HTTP {(int)response.StatusCode}" : body.Trim();
        }
    }
}
