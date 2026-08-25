using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.Installers;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using Pasyot_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using static Pasyot_Launcher.Services.ModpackSyncService;

namespace Pasyot_Launcher.Services
{
    public class MinecraftService
    {
        private const int MaxDownloaderThreads = 16;
        private const int MaxCheckerThreads = 4;
        private const int DownloaderBoundedCapacity = 2048;

        private readonly HttpClient _httpClient;

        static MinecraftService()
        {
            // CmlLib.Core.Installer.Forge opens an ad-wall page in the default browser after every
            // Forge/NeoForge install (ForgeInstaller.showAd -> Process.Start(ForgeAdUrl)). Blanking the
            // field makes that Process.Start throw on an empty filename, which the library swallows itself.
            try
            {
                typeof(ForgeInstaller)
                    .GetField(nameof(ForgeInstaller.ForgeAdUrl), BindingFlags.Public | BindingFlags.Static)
                    ?.SetValue(null, string.Empty);
            }
            catch
            {
            }
        }

        public MinecraftService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<Process> LaunchAsync(PasyotPack pack, AppSettings settings, string playerName, string? loader, string? minecraftVersion, IProgress<SyncProgressReport>? progress = null, bool connectDirectly = false)
        {
            string gameDirectory = Path.Combine(settings.ProfilesPath, pack.Name);
            var path = new MinecraftPath(gameDirectory);

            var launcherParameters = MinecraftLauncherParameters.CreateDefault(path, _httpClient);
            launcherParameters.GameInstaller = new ParallelGameInstaller(MaxCheckerThreads, MaxDownloaderThreads, DownloaderBoundedCapacity, _httpClient);

            var launcher = new MinecraftLauncher(launcherParameters);

            launcher.FileProgressChanged += (sender, e) =>
            {
                double percent = e.TotalTasks > 0
                    ? ((double)e.ProgressedTasks / e.TotalTasks) * 100
                    : 0;
                progress?.Report(new SyncProgressReport
                {
                    Status = $"Загрузка ресурсов ({e.ProgressedTasks}/{e.TotalTasks}): {e.Name}",
                    Percentage = percent
                });
            };

            launcher.ByteProgressChanged += (sender, e) =>
            {
                if (e.TotalBytes > 0)
                {
                    double percent = ((double)e.ProgressedBytes / e.TotalBytes) * 100;
                    progress?.Report(new SyncProgressReport
                    {
                        Status = $"Загрузка компонентов... ({e.ProgressedBytes / 1024 / 1024}MB / {e.TotalBytes / 1024 / 1024}MB)",
                        Percentage = percent
                    });
                }
            };

            string? mcVersion = !string.IsNullOrWhiteSpace(minecraftVersion) ? minecraftVersion : pack.Minecraft;
            if (string.IsNullOrWhiteSpace(mcVersion))
            {
                throw new InvalidOperationException(
                    $"Для сборки «{pack.Name}» не указана версия Minecraft. Обновите .pasyotpack или задайте minecraft у сборки на сервере.");
            }

            string versionName = mcVersion;
            string loaderType = (loader ?? pack.Loader ?? "").ToLower().Trim();

            if (loaderType == "fabric")
            {
                progress?.Report(new SyncProgressReport { Status = "Проверка и установка Fabric Loader...", Percentage = 0 });
                var fabricInstaller = new FabricInstaller(_httpClient);
                var loaders = await fabricInstaller.GetLoaders(mcVersion);
                var latestLoader = loaders?.FirstOrDefault();
                if (latestLoader != null)
                {
                    versionName = $"fabric-loader-{latestLoader.Version}-{mcVersion}";
                }
            }
            else if (loaderType == "forge" || loaderType == "neoforge")
            {
                progress?.Report(new SyncProgressReport { Status = $"Проверка и установка {loaderType}...", Percentage = 0 });
                var forgeInstaller = new ForgeInstaller(launcher);
                versionName = await forgeInstaller.Install(mcVersion, new ForgeInstallOptions
                {
                    SkipIfAlreadyInstalled = true
                });
            }

            progress?.Report(new SyncProgressReport { Status = "Загрузка игровых библиотек и ассетов...", Percentage = 0 });
            await launcher.InstallAsync(versionName);

            EnsureCustomSkinLoaderConfig(gameDirectory, pack.Server);

            if (!string.IsNullOrWhiteSpace(pack.ServerIp))
            {
                EnsureServerInList(gameDirectory, pack.Name, pack.ServerIp);
            }

            progress?.Report(new SyncProgressReport { Status = "Подготовка параметров запуска...", Percentage = 90 });
            string resolvedPlayerName = string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName;
            var launchOptions = new MLaunchOption
            {
                MaximumRamMb = settings.RamMb,
                Session = new MSession(resolvedPlayerName, "access_token", OfflinePlayerUuid(resolvedPlayerName))
                {
                    UserType = "msa"
                },
                Path = path
            };

            if (connectDirectly && !string.IsNullOrWhiteSpace(pack.ServerIp))
            {
                (string host, int port) = ParseServerAddress(pack.ServerIp);
                launchOptions.ServerIp = host;
                launchOptions.ServerPort = port;
            }

            if (!string.IsNullOrWhiteSpace(settings.JavaArgs))
            {
                var argsList = settings.JavaArgs
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Select(arg => new MArgument(arg))
                    .ToList();
                launchOptions.ExtraJvmArguments = argsList;
            }

            progress?.Report(new SyncProgressReport { Status = "Запуск Minecraft...", Percentage = 100 });
            Process process = await launcher.BuildProcessAsync(versionName, launchOptions);
            process.Start();
            return process;
        }

        private static string? FindCustomSkinLoaderConfig(string gameDirectory)
        {
            string known = Path.Combine(gameDirectory, "CustomSkinLoader", "CustomSkinLoader.json");
            if (File.Exists(known)) return known;

            // Fallback for other mod versions/layouts: only ever descend into folders whose
            // name already suggests they belong to the skin mod, never the whole profile
            // (which would also walk saves/, resourcepacks/, mods/ - thousands of files).
            IEnumerable<string> topLevelRoots = new[] { gameDirectory, Path.Combine(gameDirectory, "config") }
                .Where(Directory.Exists);

            IEnumerable<string> candidateDirs = topLevelRoots
                .SelectMany(Directory.EnumerateDirectories)
                .Where(d => Path.GetFileName(d).Contains("skinloader", StringComparison.OrdinalIgnoreCase));

            foreach (string dir in candidateDirs)
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(File.ReadAllText(file));
                        if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                            doc.RootElement.TryGetProperty("loadlist", out _))
                        {
                            return file;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return null;
        }

        private static bool HasCustomSkinLoaderMod(string gameDirectory)
        {
            string modsDir = Path.Combine(gameDirectory, "mods");
            if (!Directory.Exists(modsDir)) return false;

            return Directory.EnumerateFiles(modsDir, "*.jar")
                .Any(f => Path.GetFileName(f).Contains("customskinloader", StringComparison.OrdinalIgnoreCase));
        }

        private static void EnsureCustomSkinLoaderConfig(string gameDirectory, string backendServer)
        {
            if (string.IsNullOrWhiteSpace(backendServer)) return;

            string? configPath = FindCustomSkinLoaderConfig(gameDirectory);

            // On the very first launch the mod hasn't run yet, so it hasn't written its own
            // config - patching "nothing" would silently skip our entry for that whole session.
            // Seed a minimal config ourselves in that case so Pasyot skins work from launch #1.
            bool seedingNewConfig = configPath == null;
            if (seedingNewConfig)
            {
                if (!HasCustomSkinLoaderMod(gameDirectory)) return;
                configPath = Path.Combine(gameDirectory, "CustomSkinLoader", "CustomSkinLoader.json");
            }

            string root = backendServer.TrimEnd('/') + "/customskinapi/";

            try
            {
                JsonObject? config;

                if (seedingNewConfig)
                {
                    config = new JsonObject
                    {
                        ["loadlist"] = new JsonArray
                        {
                            new JsonObject { ["name"] = "Pasyot", ["type"] = "CustomSkinAPI", ["root"] = root },
                            new JsonObject { ["name"] = "Mojang", ["type"] = "MojangAPI" }
                        }
                    };
                }
                else
                {
                    config = JsonNode.Parse(File.ReadAllText(configPath!)) as JsonObject;
                    if (config == null) return;

                    if (config["loadlist"] is not JsonArray loadlist)
                    {
                        loadlist = new JsonArray();
                        config["loadlist"] = loadlist;
                    }

                    JsonObject? existing = loadlist
                        .OfType<JsonObject>()
                        .FirstOrDefault(e => (string?)e["name"] == "Pasyot");

                    if (existing != null)
                    {
                        existing["type"] = "CustomSkinAPI";
                        existing["root"] = root;
                    }
                    else
                    {
                        loadlist.Insert(0, new JsonObject
                        {
                            ["name"] = "Pasyot",
                            ["type"] = "CustomSkinAPI",
                            ["root"] = root
                        });
                    }
                }

                Directory.CreateDirectory(Path.GetDirectoryName(configPath!)!);
                string tmpPath = configPath + ".tmp";
                File.WriteAllText(tmpPath, config.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                File.Move(tmpPath, configPath!, overwrite: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnsureCustomSkinLoaderConfig] {ex}");
            }
        }

        private static void EnsureServerInList(string gameDirectory, string packName, string serverIp)
        {
            try
            {
                string serversDatPath = Path.Combine(gameDirectory, "servers.dat");
                NbtServerList.AddOrUpdateServer(serversDatPath, packName, serverIp);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EnsureServerInList] {ex}");
            }
        }

        private static (string Host, int Port) ParseServerAddress(string serverIp)
        {
            string address = serverIp.Trim();
            int colonIndex = address.LastIndexOf(':');

            if (colonIndex > 0 && int.TryParse(address[(colonIndex + 1)..], out int port))
            {
                return (address[..colonIndex], port);
            }

            return (address, 25565);
        }

        private static string OfflinePlayerUuid(string username)
        {
            using var md5 = MD5.Create();
            byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("OfflinePlayer:" + username));
            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }
}