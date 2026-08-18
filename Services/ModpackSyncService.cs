using Pasyot_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
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

        public static string ResolveManifestUrl(PasyotPack pack)
        {
            if (!string.IsNullOrWhiteSpace(pack.Manifest))
                return pack.Manifest;

            return $"{pack.Server.TrimEnd('/')}/modpacks/{pack.Name}/manifest";
        }

        public async Task<int?> GetLatestVersionAsync(PasyotPack pack)
        {
            string url = $"{pack.Server.TrimEnd('/')}/modpacks/{pack.Name}";
            var summary = await _httpClient.GetFromJsonAsync<ModpackSummary>(url);
            return summary?.LatestVersion;
        }

        public Task<ManifestModel?> SyncAsync(PasyotPack pack, string profilesPath, IProgress<SyncProgressReport>? progress = null)
            => SyncAsync(pack, profilesPath, progress, allowManifestRetry: true);

        private async Task<ManifestModel?> SyncAsync(PasyotPack pack, string profilesPath, IProgress<SyncProgressReport>? progress, bool allowManifestRetry)
        {
            string manifestUrl = ResolveManifestUrl(pack);

            progress?.Report(new SyncProgressReport { Status = "Получение манифеста...", Percentage = 0 });

            var manifest = await _httpClient.GetFromJsonAsync<ManifestModel>(manifestUrl);
            if (manifest == null || manifest.Files == null) return null;

            string modpackDir = Path.Combine(profilesPath, pack.Name);
            int totalFiles = manifest.Files.Count;
            int processedFiles = 0;
            var missingFiles = new List<string>();

            foreach (var file in manifest.Files)
            {
                processedFiles++;
                double basePercent = totalFiles > 0 ? ((double)(processedFiles - 1) / totalFiles) * 100 : 0;
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

                string fileUrl = BuildFileUrl(pack.Server.TrimEnd('/'), file);
                string tempPath = destinationPath + ".tmp";

                try
                {
                    progress?.Report(new SyncProgressReport
                    {
                        Status = $"Загрузка ({processedFiles}/{totalFiles}): {fileName}",
                        Percentage = basePercent
                    });

                    using (var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        long? totalBytes = response.Content.Headers.ContentLength;
                        using var downloadStream = await response.Content.ReadAsStreamAsync();
                        using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                        {
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
                                    double currentOverallPercent = basePercent + (fileProgress * (100.0 / Math.Max(totalFiles, 1)));

                                    string sizeInfo = $"{totalReadBytes / 1024 / 1024}MB / {totalBytes.Value / 1024 / 1024}MB";

                                    progress?.Report(new SyncProgressReport
                                    {
                                        Status = $"Загрузка ({processedFiles}/{totalFiles}): {fileName} ({sizeInfo})",
                                        Percentage = currentOverallPercent
                                    });
                                }
                            }
                        }
                    }

                    string downloadedHash = CalculateSha256(tempPath);
                    if (!downloadedHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tempPath);
                        throw new IOException($"Хэш файла {fileName} не совпал после загрузки — файл повреждён");
                    }

                    File.Move(tempPath, destinationPath, overwrite: true);
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    TryDeleteTemp(tempPath);

                    if (allowManifestRetry)
                    {
                        return await SyncAsync(pack, profilesPath, progress, allowManifestRetry: false);
                    }

                    missingFiles.Add(fileName);
                }
                catch
                {
                    TryDeleteTemp(tempPath);
                    throw;
                }
            }

            if (missingFiles.Count > 0)
            {
                throw new Exception($"Не удалось загрузить {missingFiles.Count} файл(ов): {string.Join(", ", missingFiles)}");
            }

            CleanupRemovedFiles(modpackDir, manifest);

            progress?.Report(new SyncProgressReport { Status = "Завершено", Percentage = 100 });
            return manifest;
        }

        private static void CleanupRemovedFiles(string modpackDir, ManifestModel manifest)
        {
            if (manifest.Groups == null || manifest.Groups.Count == 0) return;

            var keepPaths = new HashSet<string>(
                manifest.Files.Select(f => Path.GetFullPath(Path.Combine(modpackDir, f.Path))),
                StringComparer.OrdinalIgnoreCase);

            foreach (var group in manifest.Groups)
            {
                bool isRoot = string.IsNullOrEmpty(group.Name);
                string groupDir = isRoot ? modpackDir : Path.Combine(modpackDir, group.Name);
                if (!Directory.Exists(groupDir)) continue;

                var searchOption = isRoot ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;

                foreach (string filePath in Directory.EnumerateFiles(groupDir, "*", searchOption))
                {
                    if (filePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!keepPaths.Contains(Path.GetFullPath(filePath)))
                    {
                        try { File.Delete(filePath); }
                        catch {  }
                    }
                }
            }
        }

        private static void TryDeleteTemp(string tempPath)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }

        private static string BuildFileUrl(string baseUrl, ManifestFile file)
        {
            if (!string.IsNullOrWhiteSpace(file.Url))
                return file.Url;

            return $"{baseUrl}/objects/{file.Sha256}";
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
