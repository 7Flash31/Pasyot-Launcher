using Pasyot_Launcher.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Services
{
    public class ModpackSyncService
    {
        private const int MaxParallelDownloads = 8;
        private const int CopyBufferSize = 81920;

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

        // Filled in by SyncAsync so the caller can tell the player "N files weren't touched -
        // they were changed locally" instead of that staying invisible.
        public sealed class SyncOutcome
        {
            public int PreservedFiles { get; set; }
        }

        private sealed class SyncProgressState
        {
            private readonly long _totalBytes;
            private readonly int _totalFiles;
            private readonly IProgress<SyncProgressReport>? _progress;
            private long _downloadedBytes;
            private int _completedFiles;
            private long _lastReportTicks;

            public SyncProgressState(long totalBytes, int totalFiles, IProgress<SyncProgressReport>? progress)
            {
                _totalBytes = totalBytes;
                _totalFiles = totalFiles;
                _progress = progress;
            }

            public void ReportBytes(string fileName, long delta)
            {
                long downloaded = Interlocked.Add(ref _downloadedBytes, delta);

                long nowTicks = Environment.TickCount64;
                long lastTicks = Interlocked.Read(ref _lastReportTicks);
                if (nowTicks - lastTicks < 100) return;
                if (Interlocked.CompareExchange(ref _lastReportTicks, nowTicks, lastTicks) != lastTicks) return;

                double percent = _totalBytes > 0 ? (double)downloaded / _totalBytes * 100 : 0;
                _progress?.Report(new SyncProgressReport
                {
                    Status = $"Загрузка ({Volatile.Read(ref _completedFiles)}/{_totalFiles}): {fileName}",
                    Percentage = Math.Min(percent, 100)
                });
            }

            public void ReportFileDone(string fileName)
            {
                int completed = Interlocked.Increment(ref _completedFiles);
                _progress?.Report(new SyncProgressReport
                {
                    Status = $"Загрузка ({completed}/{_totalFiles}): {fileName}",
                    Percentage = _totalBytes > 0 ? (double)Interlocked.Read(ref _downloadedBytes) / _totalBytes * 100 : (double)completed / _totalFiles * 100
                });
            }
        }

        public static string ResolveManifestUrl(PasyotPack pack)
        {
            if (!string.IsNullOrWhiteSpace(pack.Manifest))
                return ResolveAbsoluteUrl(pack, pack.Manifest, "манифеста");

            return ResolveAbsoluteUrl(pack, $"{RequireServer(pack)}/modpacks/{pack.Name}/manifest", "манифеста");
        }

        private static string RequireServer(PasyotPack pack)
        {
            if (string.IsNullOrWhiteSpace(pack.Server) || !Uri.TryCreate(pack.Server, UriKind.Absolute, out _))
            {
                throw new InvalidOperationException(
                    $"У сборки «{pack.Name}» не задан или некорректен адрес сервера (server: «{pack.Server}»).");
            }

            return pack.Server.TrimEnd('/');
        }

        private static string ResolveAbsoluteUrl(PasyotPack pack, string value, string what)
        {
            string trimmed = value.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
                return trimmed;

            if (!string.IsNullOrWhiteSpace(pack.Server) &&
                Uri.TryCreate(pack.Server, UriKind.Absolute, out var serverUri) &&
                Uri.TryCreate(serverUri, trimmed.TrimStart('/'), out var combined))
            {
                return combined.ToString();
            }

            throw new InvalidOperationException(
                $"Некорректный адрес {what} у сборки «{pack.Name}»: «{value}» — это не полный URL (https://...), " +
                "и его не удалось объединить с адресом сервера.");
        }

        public async Task<int?> GetLatestVersionAsync(PasyotPack pack)
        {
            string url = $"{RequireServer(pack)}/modpacks/{pack.Name}";
            var summary = await _httpClient.GetFromJsonAsync<ModpackSummary>(url);
            return summary?.LatestVersion;
        }

        public async Task<(ManifestModel Manifest, List<ManifestFile> ChangedFiles)?> PreviewUpdateAsync(PasyotPack pack, string profilesPath)
        {
            string manifestUrl = ResolveManifestUrl(pack);
            var manifest = await _httpClient.GetFromJsonAsync<ManifestModel>(manifestUrl).ConfigureAwait(false);
            if (manifest == null || manifest.Files == null) return null;

            string modpackDir = Path.Combine(profilesPath, pack.Name);
            var baseline = SyncBaseline.Load(modpackDir, pack.Name);
            var (changedFiles, _) = await FindPendingFilesAsync(modpackDir, manifest.Files, baseline, strict: false, null).ConfigureAwait(false);
            return (manifest, changedFiles);
        }

        // Files the sync would leave untouched on a normal (non-strict) run because they were
        // edited locally since the launcher last wrote them - i.e. exactly what "Проверить файлы"
        // would overwrite. Used to show the player what's been changed in their install.
        public async Task<List<string>> GetLocallyModifiedFilesAsync(PasyotPack pack, string profilesPath)
        {
            string modpackDir = Path.Combine(profilesPath, pack.Name);
            if (!Directory.Exists(modpackDir)) return new List<string>();

            string manifestUrl = ResolveManifestUrl(pack);
            var manifest = await _httpClient.GetFromJsonAsync<ManifestModel>(manifestUrl).ConfigureAwait(false);
            if (manifest == null || manifest.Files == null) return new List<string>();

            var baseline = SyncBaseline.Load(modpackDir, pack.Name);

            return await Task.Run(() =>
            {
                var modified = new ConcurrentBag<string>();
                Parallel.ForEach(manifest.Files, file =>
                {
                    if (ClassifyFile(modpackDir, file, baseline, strict: false) == FileSyncAction.PreserveLocal)
                    {
                        modified.Add(file.Path);
                    }
                });
                return modified.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            }).ConfigureAwait(false);
        }

        // strict: true forces every tracked file to match the manifest exactly, overwriting any
        // local edits (mod-caused drift included) instead of preserving them - used by the
        // "verify files" action. Worlds/resourcepacks/shaderpacks the player added on their own
        // are still never deleted, strict or not (see NeverCleanupGroups).
        // outcome, if given, is filled in with how many tracked files were left untouched because
        // they were changed locally - so the caller can tell the player instead of that staying invisible.
        public Task<ManifestModel?> SyncAsync(PasyotPack pack, string profilesPath, IProgress<SyncProgressReport>? progress = null, bool strict = false, SyncOutcome? outcome = null)
            => SyncAsync(pack, profilesPath, progress, allowManifestRetry: true, strict: strict, outcome: outcome);

        private async Task<ManifestModel?> SyncAsync(PasyotPack pack, string profilesPath, IProgress<SyncProgressReport>? progress, bool allowManifestRetry, bool strict, SyncOutcome? outcome)
        {
            string manifestUrl = ResolveManifestUrl(pack);

            progress?.Report(new SyncProgressReport { Status = "Проверка обновлений...", Percentage = 0 });

            var manifest = await _httpClient.GetFromJsonAsync<ManifestModel>(manifestUrl).ConfigureAwait(false);
            if (manifest == null || manifest.Files == null) return null;

            string modpackDir = Path.Combine(profilesPath, pack.Name);
            Directory.CreateDirectory(modpackDir);

            var baseline = SyncBaseline.Load(modpackDir, pack.Name);

            progress?.Report(new SyncProgressReport { Status = "Проверка локальных файлов...", Percentage = 0 });
            var (pending, preservedCount) = await FindPendingFilesAsync(modpackDir, manifest.Files, baseline, strict, progress).ConfigureAwait(false);
            if (outcome != null) outcome.PreservedFiles = preservedCount;

            if (pending.Count == 0)
            {
                CleanupRemovedFiles(modpackDir, manifest, baseline);
                baseline.Save();
                progress?.Report(new SyncProgressReport { Status = "Завершено", Percentage = 100 });
                return manifest;
            }

            if (manifest.Bundle != null && pending.Any(f => f.Bundled))
            {
                await TryApplyBundleAsync(pack, manifest.Bundle, modpackDir, pending, baseline, progress).ConfigureAwait(false);
                (pending, _) = await FindPendingFilesAsync(modpackDir, pending, baseline, strict, progress).ConfigureAwait(false);
            }

            if (pending.Count == 0)
            {
                CleanupRemovedFiles(modpackDir, manifest, baseline);
                baseline.Save();
                progress?.Report(new SyncProgressReport { Status = "Завершено", Percentage = 100 });
                return manifest;
            }

            long totalBytes = pending.Sum(f => f.Size);
            var state = new SyncProgressState(totalBytes, pending.Count, progress);
            var missingFiles = new List<string>();
            var missingLock = new object();
            int manifestStale = 0;
            using var cts = new CancellationTokenSource();
            using var semaphore = new SemaphoreSlim(MaxParallelDownloads);

            var downloadTasks = pending.Select(file => Task.Run(async () =>
            {
                try
                {
                    await semaphore.WaitAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                try
                {
                    await DownloadFileAsync(pack, modpackDir, file, state, baseline, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
                {
                    if (allowManifestRetry)
                    {
                        Interlocked.Exchange(ref manifestStale, 1);
                        cts.Cancel();
                    }
                    else
                    {
                        lock (missingLock) missingFiles.Add(Path.GetFileName(file.Path));
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToArray();

            await Task.WhenAll(downloadTasks).ConfigureAwait(false);

            if (Volatile.Read(ref manifestStale) == 1)
            {
                return await SyncAsync(pack, profilesPath, progress, allowManifestRetry: false, strict: strict, outcome: outcome).ConfigureAwait(false);
            }

            if (missingFiles.Count > 0)
            {
                throw new Exception($"Не удалось загрузить {missingFiles.Count} файл(ов): {string.Join(", ", missingFiles)}");
            }

            CleanupRemovedFiles(modpackDir, manifest, baseline);
            baseline.Save();

            progress?.Report(new SyncProgressReport { Status = "Завершено", Percentage = 100 });
            return manifest;
        }

        private enum FileSyncAction
        {
            UpToDate,
            NeedsDownload,
            PreserveLocal
        }

        // Classifies a manifest file against what's on disk and what we last wrote there
        // ourselves (the baseline). A local edit the player or a mod made since our last write
        // is left untouched instead of being clobbered by the server's version - unless strict
        // is set, in which case the manifest always wins (used by "verify files"). Engine/binary
        // content (see AlwaysStrictGroups) always behaves as if strict, in both directions: a
        // mod jar with a wrong hash is corruption/mismatch, never an intentional edit worth
        // keeping, so it's always corrected.
        private FileSyncAction ClassifyFile(string modpackDir, ManifestFile file, SyncBaseline baseline, bool strict)
        {
            string destinationPath = Path.Combine(modpackDir, file.Path);
            if (!File.Exists(destinationPath)) return FileSyncAction.NeedsDownload;

            string localHash = CalculateSha256(destinationPath);
            if (localHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                baseline.Set(file.Path, localHash);
                return FileSyncAction.UpToDate;
            }

            if (strict || AlwaysStrictGroups.Contains(file.Group)) return FileSyncAction.NeedsDownload;

            if (LooksCorrupted(destinationPath, file.Size))
            {
                // A zero-byte file or a .jar/.zip that won't even open as an archive is never a
                // deliberate edit worth keeping - always heal it, no matter what group it's in.
                return FileSyncAction.NeedsDownload;
            }

            string? lastSynced = baseline.Get(file.Path);
            if (lastSynced == null || lastSynced.Equals(localHash, StringComparison.OrdinalIgnoreCase))
            {
                // Never synced before (safe default: deliver official content), or unchanged
                // since we last wrote it - either way the server's new version applies.
                return FileSyncAction.NeedsDownload;
            }

            return FileSyncAction.PreserveLocal;
        }

        private static bool LooksCorrupted(string path, long expectedSize)
        {
            try
            {
                var info = new FileInfo(path);
                if (expectedSize > 0 && info.Length == 0) return true;

                string ext = Path.GetExtension(path);
                if (ext.Equals(".jar", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    using var archive = ZipFile.OpenRead(path);
                    _ = archive.Entries.Count;
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        private Task<(List<ManifestFile> Pending, int Preserved)> FindPendingFilesAsync(string modpackDir, IReadOnlyCollection<ManifestFile> files, SyncBaseline baseline, bool strict, IProgress<SyncProgressReport>? progress)
        {
            return Task.Run(() =>
            {
                var pending = new ConcurrentBag<ManifestFile>();
                int preserved = 0;
                int checkedCount = 0;
                long lastReportTicks = 0;

                Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, file =>
                {
                    switch (ClassifyFile(modpackDir, file, baseline, strict))
                    {
                        case FileSyncAction.NeedsDownload:
                            pending.Add(file);
                            break;
                        case FileSyncAction.PreserveLocal:
                            Interlocked.Increment(ref preserved);
                            break;
                    }

                    int done = Interlocked.Increment(ref checkedCount);
                    long nowTicks = Environment.TickCount64;
                    long lastTicks = Interlocked.Read(ref lastReportTicks);
                    if (nowTicks - lastTicks < 150) return;
                    if (Interlocked.CompareExchange(ref lastReportTicks, nowTicks, lastTicks) != lastTicks) return;

                    progress?.Report(new SyncProgressReport
                    {
                        Status = $"Проверка файлов ({done}/{files.Count})",
                        Percentage = files.Count > 0 ? (double)done / files.Count * 100 : 0
                    });
                });

                return (pending.ToList(), preserved);
            });
        }

        private async Task DownloadFileAsync(PasyotPack pack, string modpackDir, ManifestFile file, SyncProgressState state, SyncBaseline baseline, CancellationToken ct)
        {
            string fileName = Path.GetFileName(file.Path);
            string destinationPath = Path.Combine(modpackDir, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

            string fileUrl = BuildFileUrl(pack, file);
            string tempPath = destinationPath + ".tmp";

            try
            {
                using (var response = await _httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    using var downloadStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, true);

                    var buffer = new byte[CopyBufferSize];
                    int bytesRead;
                    while ((bytesRead = await downloadStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        state.ReportBytes(fileName, bytesRead);
                    }
                }

                string downloadedHash = CalculateSha256(tempPath);
                if (!downloadedHash.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tempPath);
                    throw new IOException($"Хэш файла {fileName} не совпал после загрузки — файл повреждён");
                }

                File.Move(tempPath, destinationPath, overwrite: true);
                baseline.Set(file.Path, file.Sha256);
                state.ReportFileDone(fileName);
            }
            catch
            {
                TryDeleteTemp(tempPath);
                throw;
            }
        }

        private static readonly TimeSpan BundleStageTimeout = TimeSpan.FromMinutes(5);

        private async Task TryApplyBundleAsync(PasyotPack pack, ManifestBundle bundle, string modpackDir, List<ManifestFile> pending, SyncBaseline baseline, IProgress<SyncProgressReport>? progress)
        {
            if (!string.Equals(bundle.Format, "tar+gzip", StringComparison.OrdinalIgnoreCase))
                return;

            string tempArchive = Path.Combine(Path.GetTempPath(), $"pasyot-bundle-{Guid.NewGuid():N}.tar.gz");
            string tempExtractDir = Path.Combine(Path.GetTempPath(), $"pasyot-bundle-{Guid.NewGuid():N}");

            using var cts = new CancellationTokenSource(BundleStageTimeout);
            CancellationToken ct = cts.Token;

            try
            {
                string bundleUrl = ResolveAbsoluteUrl(pack, bundle.Url, "бандла");

                using (var response = await _httpClient.GetAsync(bundleUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();

                    long totalBytes = bundle.Size > 0 ? bundle.Size : (response.Content.Headers.ContentLength ?? 0);

                    using var downloadStream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                    using var fileStream = new FileStream(tempArchive, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, true);

                    var buffer = new byte[CopyBufferSize];
                    long readSoFar = 0;
                    long lastReportTicks = 0;
                    int bytesRead;
                    while ((bytesRead = await downloadStream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                        readSoFar += bytesRead;

                        long nowTicks = Environment.TickCount64;
                        if (nowTicks - lastReportTicks < 100) continue;
                        lastReportTicks = nowTicks;

                        double percent = totalBytes > 0 ? Math.Min((double)readSoFar / totalBytes * 100, 100) : 0;
                        progress?.Report(new SyncProgressReport
                        {
                            Status = $"Загрузка общего пакета мелких файлов ({readSoFar / 1024 / 1024}МБ" +
                                      (totalBytes > 0 ? $" / {totalBytes / 1024 / 1024}МБ" : "") + ")",
                            Percentage = percent
                        });
                    }
                }

                if (!string.IsNullOrWhiteSpace(bundle.Sha256) &&
                    !CalculateSha256(tempArchive).Equals(bundle.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                progress?.Report(new SyncProgressReport { Status = "Загрузка: распаковка общего пакета мелких файлов...", Percentage = 100 });

                Directory.CreateDirectory(tempExtractDir);
                using (var gzip = new GZipStream(File.OpenRead(tempArchive), CompressionMode.Decompress))
                {
                    await TarFile.ExtractToDirectoryAsync(gzip, tempExtractDir, overwriteFiles: true, ct).ConfigureAwait(false);
                }

                foreach (var file in pending.Where(f => f.Bundled).ToList())
                {
                    string extractedPath = Path.Combine(tempExtractDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(extractedPath)) continue;
                    if (!CalculateSha256(extractedPath).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) continue;

                    string destinationPath = Path.Combine(modpackDir, file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
                    File.Move(extractedPath, destinationPath, overwrite: true);
                    baseline.Set(file.Path, file.Sha256);
                }
            }
            catch
            {
            }
            finally
            {
                TryDeleteTemp(tempArchive);
                try { if (Directory.Exists(tempExtractDir)) Directory.Delete(tempExtractDir, true); } catch { }
            }
        }

        // These groups are player content, never something the sync should delete just because
        // the server manifest doesn't happen to list a given file: worlds the player created or
        // played in, and resource/shader packs the player added on top of the official ones.
        private static readonly HashSet<string> NeverCleanupGroups = new(StringComparer.OrdinalIgnoreCase)
        {
            "saves", "resourcepacks", "shaderpacks"
        };

        // Engine/binary content: nobody hand-edits a mod jar, so a hash mismatch here is always
        // corruption or a stale copy, never an intentional change worth keeping - and a mod the
        // curator removed from the pack must actually disappear (stale/incompatible jars crash
        // or conflict), not linger because it happens to differ from some remembered baseline.
        private static readonly HashSet<string> AlwaysStrictGroups = new(StringComparer.OrdinalIgnoreCase)
        {
            "mods", "libraries", "versions", "assets"
        };

        private void CleanupRemovedFiles(string modpackDir, ManifestModel manifest, SyncBaseline baseline)
        {
            if (manifest.Groups == null || manifest.Groups.Count == 0) return;

            var keepPaths = new HashSet<string>(
                manifest.Files.Select(f => Path.GetFullPath(Path.Combine(modpackDir, f.Path))),
                StringComparer.OrdinalIgnoreCase);

            foreach (var group in manifest.Groups)
            {
                if (NeverCleanupGroups.Contains(group.Name)) continue;
                bool alwaysStrict = AlwaysStrictGroups.Contains(group.Name);

                bool isRoot = string.IsNullOrEmpty(group.Name);
                string groupDir = isRoot ? modpackDir : Path.Combine(modpackDir, group.Name);
                if (!Directory.Exists(groupDir)) continue;

                var searchOption = isRoot ? SearchOption.TopDirectoryOnly : SearchOption.AllDirectories;

                foreach (string filePath in Directory.EnumerateFiles(groupDir, "*", searchOption))
                {
                    if (filePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                    if (keepPaths.Contains(Path.GetFullPath(filePath))) continue;

                    if (!alwaysStrict && !WasShippedUnmodified(modpackDir, filePath, baseline))
                    {
                        // Not engine content, and either the player added this file themselves
                        // (no baseline record) or edited what we shipped (hash moved since) -
                        // leave it, same as any other locally-changed tracked file.
                        continue;
                    }

                    try { File.Delete(filePath); }
                    catch { }
                }
            }
        }

        // True only when we're sure this exact file is our own, untouched official copy - safe
        // to delete now that the curator dropped it from the pack.
        private bool WasShippedUnmodified(string modpackDir, string absoluteFilePath, SyncBaseline baseline)
        {
            string relativePath = Path.GetRelativePath(modpackDir, absoluteFilePath).Replace(Path.DirectorySeparatorChar, '/');
            string? lastSynced = baseline.Get(relativePath);
            if (lastSynced == null) return false;

            try
            {
                return lastSynced.Equals(CalculateSha256(absoluteFilePath), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static void TryDeleteTemp(string tempPath)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { }
        }

        private static string BuildFileUrl(PasyotPack pack, ManifestFile file)
        {
            if (!string.IsNullOrWhiteSpace(file.Url))
                return ResolveAbsoluteUrl(pack, file.Url, $"файла {file.Path}");

            return ResolveAbsoluteUrl(pack, $"{RequireServer(pack)}/objects/{file.Sha256}", $"файла {file.Path}");
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
