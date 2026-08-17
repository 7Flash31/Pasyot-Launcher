using Pasyot_Launcher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Services
{
    public class ModpackSyncService
    {
        private readonly HttpClient _httpClient;

        public ModpackSyncService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public class SyncProgressReport
        {
            public string Status { get; set; } = string.Empty;
            public double Percentage { get; set; }
        }

        public async Task<ManifestModel?> SyncAsync(PasyotPack pack, string profilesPath, IProgress<SyncProgressReport>? progress = null)
        {
            string baseUrl = pack.Server.TrimEnd('/');
            string manifestUrl = $"{baseUrl}/modpacks/{pack.Modpack}/versions/{pack.Version}/manifest";

            progress?.Report(new SyncProgressReport { Status = "Получение манифеста...", Percentage = 0 });

            var manifest = await _httpClient.GetFromJsonAsync<ManifestModel>(manifestUrl);
            if (manifest == null || manifest.Files == null) return null;

            string modpackDir = Path.Combine(profilesPath, pack.Modpack);
            int totalFiles = manifest.Files.Count;
            int processedFiles = 0;

            foreach (var file in manifest.Files)
            {
                processedFiles++;
                double basePercent = ((double)(processedFiles - 1) / totalFiles) * 100;
                string fileName = Path.GetFileName(file.Path);

                string destinationPath = Path.Combine(modpackDir, file.Path);

                if (File.Exists(destinationPath) && CalculateSha256(destinationPath).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(new SyncProgressReport
                    {
                        Status = $"Пропущен ({processedFiles}/{totalFiles}): {fileName}",
                        Percentage = ((double)processedFiles / totalFiles) * 100
                    });
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

                string fileUrl = string.IsNullOrEmpty(file.Url) ? $"{baseUrl}/objects/{file.Sha256}" : file.Url;

                try
                {
                    progress?.Report(new SyncProgressReport
                    {
                        Status = $"Загрузка ({processedFiles}/{totalFiles}): {fileName}",
                        Percentage = basePercent
                    });

                    using var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    long? totalBytes = response.Content.Headers.ContentLength;
                    using var downloadStream = await response.Content.ReadAsStreamAsync();
                    using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                    var buffer = new byte[8192];
                    long totalReadBytes = 0;
                    int bytesRead;

                    while ((bytesRead = await downloadStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalReadBytes += bytesRead;

                        if (totalBytes.HasValue && totalBytes.Value > 0)
                        {
                            double fileProgress = (double)totalReadBytes / totalBytes.Value;
                            double currentOverallPercent = basePercent + (fileProgress * (100.0 / totalFiles));

                            string sizeInfo = $"{totalReadBytes / 1024 / 1024}MB / {totalBytes.Value / 1024 / 1024}MB";

                            progress?.Report(new SyncProgressReport
                            {
                                Status = $"Загрузка ({processedFiles}/{totalFiles}): {fileName} ({sizeInfo})",
                                Percentage = currentOverallPercent
                            });
                        }
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    
                }
            }

            progress?.Report(new SyncProgressReport { Status = "Завершено", Percentage = 100 });
            return manifest;
        }

        private string CalculateSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
        public bool IsInstalled(string packSlug, string profilesPath)
        {
            string modpackDir = Path.Combine(profilesPath, packSlug);
            return Directory.Exists(modpackDir) && Directory.GetFileSystemEntries(modpackDir).Length > 0;
        }

        public void DeleteModpackFiles(string modpackSlug, string profilesPath)
        {
            try
            {
                string packDirectory = Path.Combine(profilesPath, modpackSlug);
                if (Directory.Exists(packDirectory))
                {
                    Directory.Delete(packDirectory, true);
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Не удалось удалить файлы сборки с диска: {ex.Message}");
            }
        }
    }
}