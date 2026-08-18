using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Installer.Forge;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using Pasyot_Launcher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using static Pasyot_Launcher.Services.ModpackSyncService;

namespace Pasyot_Launcher.Services
{
    public class MinecraftService
    {
        private readonly HttpClient _httpClient;

        public MinecraftService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<Process> LaunchAsync(PasyotPack pack, AppSettings settings, string playerName, string? loader, string? minecraftVersion, IProgress<SyncProgressReport>? progress = null)
        {
            string gameDirectory = Path.Combine(settings.ProfilesPath, pack.Name);
            var path = new MinecraftPath(gameDirectory);
            var launcher = new MinecraftLauncher(path);

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

            progress?.Report(new SyncProgressReport { Status = "Подготовка параметров запуска...", Percentage = 90 });
            var launchOptions = new MLaunchOption
            {
                MaximumRamMb = settings.RamMb,
                Session = MSession.CreateOfflineSession(string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName),
                Path = path
            };

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
    }
}