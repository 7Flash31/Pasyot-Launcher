using Pasyot_Launcher.Models;
using Pasyot_Launcher.Services;
using Pasyot_Launcher.Views;
using Pasyot_Launcher.Views.Pages;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Pasyot_Launcher.Services.ModpackSyncService;

namespace Pasyot_Launcher
{
    internal enum ToastType
    {
        Info,
        Success,
        Error
    }

    public partial class MainWindow : Window
    {
        private static readonly HttpClient HttpClient = new HttpClient();
        private readonly MinecraftService _minecraftService;
        private readonly ModpackSyncService _syncService;

        private readonly HomePage _homePage;
        private readonly LibraryPage _libraryPage;
        private readonly SkinPage _skinPage;
        private readonly SettingsPage _settingsPage;
        private bool _shellReady;

        private AppSettings _appSettings;
        private ModpackProfile? _selectedModpack;
        private bool _selectedPackUpdateAvailable;

        private Process? _runningGameProcess;
        private string? _runningGamePackName;

        public MainWindow()
        {
            InitializeComponent();
            _minecraftService = new MinecraftService(HttpClient);
            _syncService = new ModpackSyncService(HttpClient);

            _appSettings = AppSettings.Instance;

            _homePage = new HomePage();
            _homePage.PlayRequested += (s, e) => LaunchSelectedModpack();
            _homePage.OpenFolderRequested += (s, e) => OpenSelectedModpackFolder();
            _homePage.VerifyRequested += (s, e) => VerifySelectedModpack();
            _homePage.RefreshServerStatusRequested += (s, e) => RefreshSelectedModpackStatus();
            _homePage.ConnectRequested += (s, e) => LaunchSelectedModpack(connectDirectly: true);

            _libraryPage = new LibraryPage();
            _libraryPage.AddModpackRequested += (s, e) => AddModpack();

            _skinPage = new SkinPage(HttpClient);
            _skinPage.OnError += (s, msg) => ShowToast(msg, ToastType.Error);
            _skinPage.OnSuccess += (s, msg) => ShowToast(msg, ToastType.Success);

            _settingsPage = new SettingsPage(_appSettings);
            _settingsPage.OnSaved += (s, e) =>
            {
                _homePage.RefreshRamChip();
                ShowToast("Настройки сохранены", ToastType.Success);
            };
            _settingsPage.OnError += (s, msg) => ShowToast(msg, ToastType.Error);

            _shellReady = true;
            PageHost.Content = _homePage;

            LoadSavedModpacks();
            _ = UpdateUiState();
            VedrowAuth.OnAuthCompleted += LoginSucces;
        }

        private string? SelectedModpackDir =>
            _selectedModpack == null ? null : Path.Combine(_appSettings.ProfilesPath, _selectedModpack.PackData.Name);

        private void LoadSavedModpacks()
        {
            _libraryPage.ItemsPanel.Children.Clear();

            if (_appSettings.InstalledPacks == null || _appSettings.InstalledPacks.Count == 0)
            {
                _libraryPage.SetEmpty(true);
                _homePage.SetSelectedPack(null);
                _homePage.SetPlayButtonState("Запустить", false);
                return;
            }

            _libraryPage.SetEmpty(false);

            ModpackProfile? packToSelect = null;

            foreach (var pack in _appSettings.InstalledPacks)
            {
                var profile = CreateModpackProfileUI(pack);
                _libraryPage.ItemsPanel.Children.Add(profile);

                if (pack.Name == _appSettings.SelectedSlug)
                {
                    packToSelect = profile;
                }
            }

            if (packToSelect != null)
            {
                SelectModpack(packToSelect);
            }
            else if (_libraryPage.ItemsPanel.Children.Count > 0 && _libraryPage.ItemsPanel.Children[0] is ModpackProfile firstProfile)
            {
                SelectModpack(firstProfile);
            }
        }

        private ModpackProfile CreateModpackProfileUI(PasyotPack pack)
        {
            var modpack = new ModpackProfile();
            modpack.Init(pack);
            modpack.OnSelected += (s, profile) => SelectModpack(profile);

            modpack.OnDelete += (s, profile) => DeleteModpack(profile);
            modpack.OnOpenSettings += (s, profile) => OpenModpackSettings(profile);

            _ = RefreshUpdateBadgeAsync(modpack, pack);
            _ = RefreshLocalChangesBadgeAsync(modpack, pack);
            _ = LoadModpackIconAsync(modpack, pack);

            return modpack;
        }

        private static async Task LoadModpackIconAsync(ModpackProfile ui, PasyotPack pack)
        {
            var icon = await IconCache.GetAsync(HttpClient, pack);
            if (icon != null) ui.SetIcon(icon);
        }

        private async Task RefreshLocalChangesBadgeAsync(ModpackProfile ui, PasyotPack pack)
        {
            if (!_syncService.IsInstalled(pack.Name, _appSettings.ProfilesPath))
            {
                ui.SetLocallyModifiedFiles(Array.Empty<string>());
                return;
            }

            try
            {
                var modified = await _syncService.GetLocallyModifiedFilesAsync(pack, _appSettings.ProfilesPath);
                ui.SetLocallyModifiedFiles(modified);
            }
            catch
            {
                // Offline or server unreachable - leave whatever the badge already shows.
            }
        }

        private async Task RefreshUpdateBadgeAsync(ModpackProfile ui, PasyotPack pack)
        {
            if (!_syncService.IsInstalled(pack.Name, _appSettings.ProfilesPath))
            {
                ui.SetUpdateAvailable(false);
                return;
            }

            try
            {
                int? latestVersion = await _syncService.GetLatestVersionAsync(pack);
                ui.SetUpdateAvailable(latestVersion.HasValue && latestVersion.Value > pack.Version);
            }
            catch
            {
                ui.SetUpdateAvailable(false);
            }
        }

        private void DeleteModpack(ModpackProfile profile)
        {
            var pack = profile.PackData;

            var result = MessageBox.Show(
                $"Вы действительно хотите удалить сборку \"{pack.Name}\" и все её файлы с диска?",
                "Удаление сборки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _syncService.DeleteModpackFiles(pack.Name, _appSettings.ProfilesPath);

                var installedPack = _appSettings.InstalledPacks.FirstOrDefault(p => p.Name == pack.Name);
                if (installedPack != null)
                {
                    _appSettings.InstalledPacks.Remove(installedPack);
                }

                _libraryPage.ItemsPanel.Children.Remove(profile);

                if (_selectedModpack == profile)
                {
                    _selectedModpack = null;

                    if (_libraryPage.ItemsPanel.Children.Count > 0 && _libraryPage.ItemsPanel.Children[0] is ModpackProfile firstProfile)
                    {
                        SelectModpack(firstProfile);
                    }
                    else
                    {
                        _appSettings.SelectedSlug = string.Empty;
                        _homePage.SetSelectedPack(null);
                        _homePage.SetPlayButtonState("Запустить", false);
                        _libraryPage.SetEmpty(true);
                    }
                }

                _appSettings.Save();

                ShowToast($"Сборка «{pack.Name}» удалена", ToastType.Success);
            }
            catch (Exception ex)
            {
                ShowToast($"Не удалось удалить сборку: {ex.Message}", ToastType.Error, ex.ToString());
            }
        }

        private void SelectModpack(ModpackProfile profile)
        {
            _selectedModpack?.SetSelected(false);
            _selectedModpack = profile;
            _selectedModpack.SetSelected(true);

            _appSettings.SelectedSlug = profile.PackData.Name;
            _appSettings.Save();

            _homePage.SetSelectedPack(profile.PackData);
            _homePage.SetPlayButtonState("Проверка...", false);
            _homePage.SetServerStatusChecking();

            _ = CheckModpackStatusAsync(profile.PackData);
            _ = LoadHomeIconAsync(profile.PackData);
        }

        private async Task LoadHomeIconAsync(PasyotPack pack)
        {
            var icon = await IconCache.GetAsync(HttpClient, pack);
            if (icon != null && _selectedModpack?.PackData.Name == pack.Name)
            {
                _homePage.SetIcon(icon);
            }
        }

        private void RefreshSelectedModpackStatus()
        {
            if (_selectedModpack == null) return;

            _homePage.SetServerStatusChecking();
            _ = CheckModpackStatusAsync(_selectedModpack.PackData);
        }

        private async Task CheckModpackStatusAsync(PasyotPack pack)
        {
            if (_runningGameProcess != null && !_runningGameProcess.HasExited && _runningGamePackName == pack.Name)
            {
                _homePage.SetPlayButtonState("Игра запущена", true);
                return;
            }

            bool installed = _syncService.IsInstalled(pack.Name, _appSettings.ProfilesPath);

            int? latestVersion = null;
            bool serverOnline;
            try
            {
                latestVersion = await _syncService.GetLatestVersionAsync(pack);
                serverOnline = true;
            }
            catch
            {
                serverOnline = false;
            }

            if (_selectedModpack == null || _selectedModpack.PackData.Name != pack.Name)
                return;

            _homePage.SetServerStatus(serverOnline);

            if (!installed)
            {
                _selectedPackUpdateAvailable = false;
                _homePage.SetPlayButtonState("Скачать", serverOnline);
                return;
            }

            if (!serverOnline)
            {
                _selectedPackUpdateAvailable = false;
                _homePage.SetPlayButtonState("Запустить (Офлайн)", true);
                return;
            }

            _selectedPackUpdateAvailable = latestVersion.HasValue && latestVersion.Value > pack.Version;
            _homePage.SetPlayButtonState(_selectedPackUpdateAvailable ? "Обновить" : "Запустить", true);
        }

        private async void LaunchSelectedModpack(bool connectDirectly = false)
        {
            if (_selectedModpack == null)
            {
                ShowToast("Выберите сборку из списка!", ToastType.Info);
                return;
            }

            if (_runningGameProcess != null && !_runningGameProcess.HasExited)
            {
                var confirmResult = MessageBox.Show(
                    $"«{_runningGamePackName}» уже запущен(а). Точно хотите запустить ещё раз?",
                    "Игра уже запущена",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes) return;
            }

            bool wasInstalled = _syncService.IsInstalled(_selectedModpack.PackData.Name, _appSettings.ProfilesPath);

            try
            {
                _homePage.SetPlayButtonState("Подготовка...", false);
                _homePage.SetActionsEnabled(false);

                if (wasInstalled && _selectedPackUpdateAvailable)
                {
                    await ShowUpdateChangelogAsync(_selectedModpack.PackData);
                }

                _homePage.ShowProgress(true);
                _homePage.SetProgress(0, "Подготовка...");

                var syncProgress = WrapProgress(relabelAsUpdate: wasInstalled);
                var launchProgress = new Progress<SyncProgressReport>(report =>
                {
                    _homePage.SetProgress(report.Percentage, report.Status);
                });

                ManifestModel? manifest;
                var syncOutcome = new ModpackSyncService.SyncOutcome();
                try
                {
                    manifest = await SyncWithRetryAsync(_selectedModpack.PackData, syncProgress, outcome: syncOutcome);
                }
                catch (HttpRequestException) when (_syncService.IsInstalled(_selectedModpack.PackData.Name, _appSettings.ProfilesPath))
                {
                    manifest = null;
                    ShowToast("Нет соединения с сервером — запуск офлайн на уже установленных файлах", ToastType.Info);
                }

                if (syncOutcome.PreservedFiles > 0)
                {
                    ShowToast($"{syncOutcome.PreservedFiles} файл(ов) не обновлены — изменены локально", ToastType.Info);
                }

                if (manifest != null)
                {
                    _selectedModpack.PackData.Version = manifest.Version;

                    if (!string.IsNullOrWhiteSpace(manifest.Loader))
                        _selectedModpack.PackData.Loader = manifest.Loader;

                    if (!string.IsNullOrWhiteSpace(manifest.Minecraft))
                        _selectedModpack.PackData.Minecraft = manifest.Minecraft;

                    _selectedModpack.Init(_selectedModpack.PackData);
                    _selectedModpack.SetUpdateAvailable(false);
                    _appSettings.Save();
                }

                string? loader = !string.IsNullOrEmpty(manifest?.Loader)
                    ? manifest.Loader
                    : _selectedModpack.PackData.Loader;

                string? minecraftVersion = !string.IsNullOrEmpty(manifest?.Minecraft)
                    ? manifest.Minecraft
                    : _selectedModpack.PackData.Minecraft;

                Process gameProcess = await _minecraftService.LaunchAsync(
                    _selectedModpack.PackData,
                    _appSettings,
                    ProfileText.Text,
                    loader,
                    minecraftVersion,
                    launchProgress,
                    connectDirectly
                );

                _runningGameProcess = gameProcess;
                _runningGamePackName = _selectedModpack.PackData.Name;
                _homePage.SetPlayButtonState("Игра запущена", true);
                ShowToast($"«{_selectedModpack.PackData.Name}» запущен", ToastType.Success);

                _ = MonitorGameProcessAsync(gameProcess, _selectedModpack.PackData.Name);
            }
            catch (OperationCanceledException)
            {
                ShowToast("Загрузка отменена", ToastType.Info);
            }
            catch (Exception ex)
            {
                ShowToast($"Произошла ошибка: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                _homePage.ShowProgress(false);
                _homePage.HideSyncError();
                _homePage.SetActionsEnabled(true);
                if (_selectedModpack != null)
                {
                    _ = CheckModpackStatusAsync(_selectedModpack.PackData);
                    _ = RefreshLocalChangesBadgeAsync(_selectedModpack, _selectedModpack.PackData);
                }
            }
        }

        private void OpenSelectedModpackFolder()
        {
            if (_selectedModpack == null)
            {
                ShowToast("Выберите сборку из списка!", ToastType.Info);
                return;
            }

            string modpackDir = SelectedModpackDir!;

            if (!Directory.Exists(modpackDir))
            {
                ShowToast("Сборка ещё не установлена — папки не существует", ToastType.Info);
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(modpackDir) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                ShowToast($"Не удалось открыть папку: {ex.Message}", ToastType.Error, ex.ToString());
            }
        }

        private async void VerifySelectedModpack()
        {
            if (_selectedModpack == null)
            {
                ShowToast("Выберите сборку из списка!", ToastType.Info);
                return;
            }

            var pack = _selectedModpack.PackData;

            if (!_syncService.IsInstalled(pack.Name, _appSettings.ProfilesPath))
            {
                ShowToast("Сборка ещё не установлена — сначала запустите её", ToastType.Info);
                return;
            }

            var confirmResult = MessageBox.Show(
                "Проверка целостности приведёт файлы сборки точно к версии с сервера — включая локальные правки конфигов, если они есть. Моды/библиотеки при этом всегда приводятся в соответствие сборке. Миры, добавленные текстурпаки и шейдеры не затрагиваются.\n\nПродолжить?",
                "Проверка файлов",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            try
            {
                _homePage.SetPlayButtonEnabled(false);
                _homePage.SetActionsEnabled(false);

                _homePage.ShowProgress(true);
                _homePage.SetProgress(0, "Подготовка...");

                var progress = new Progress<SyncProgressReport>(report =>
                {
                    _homePage.SetProgress(report.Percentage, report.Status);
                });

                var outcome = new ModpackSyncService.SyncOutcome();
                var manifest = await SyncWithRetryAsync(pack, progress, strict: true, outcome: outcome);

                if (manifest != null)
                {
                    pack.Version = manifest.Version;

                    if (!string.IsNullOrWhiteSpace(manifest.Loader))
                        pack.Loader = manifest.Loader;

                    if (!string.IsNullOrWhiteSpace(manifest.Minecraft))
                        pack.Minecraft = manifest.Minecraft;

                    _selectedModpack.Init(pack);
                    _selectedModpack.SetUpdateAvailable(false);
                    _appSettings.Save();
                }

                ShowToast($"Проверка «{pack.Name}» завершена — все файлы на месте", ToastType.Success);
                if (outcome.PreservedFiles > 0)
                {
                    ShowToast($"{outcome.PreservedFiles} файл(ов) оставлены без изменений — изменены локально", ToastType.Info);
                }
            }
            catch (OperationCanceledException)
            {
                ShowToast("Проверка отменена", ToastType.Info);
            }
            catch (Exception ex)
            {
                ShowToast($"Ошибка при проверке файлов: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                _homePage.ShowProgress(false);
                _homePage.HideSyncError();
                _homePage.SetActionsEnabled(true);
                _ = CheckModpackStatusAsync(pack);
                if (_selectedModpack != null) _ = RefreshLocalChangesBadgeAsync(_selectedModpack, pack);
            }
        }

        private void OpenModpackSettings(ModpackProfile profile)
        {
            var window = new ModpackSettingsWindow(profile.PackData, _syncService, _appSettings) { Owner = this };

            if (window.ShowDialog() == true && window.ResetRequested)
            {
                _ = ReinstallModpackFromScratchAsync(profile);
            }
        }

        private async Task ReinstallModpackFromScratchAsync(ModpackProfile profile)
        {
            var pack = profile.PackData;

            if (_runningGameProcess != null && !_runningGameProcess.HasExited && _runningGamePackName == pack.Name)
            {
                ShowToast($"«{pack.Name}» сейчас запущен(а) — сначала закройте игру", ToastType.Info);
                return;
            }

            var confirmResult = MessageBox.Show(
                $"Все файлы сборки «{pack.Name}» будут удалены с диска и загружены заново с сервера — включая миры, конфиги и любые локальные изменения. Это нельзя отменить.\n\nПродолжить?",
                "Переустановка с нуля",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirmResult != MessageBoxResult.Yes) return;

            if (_selectedModpack != profile)
            {
                SelectModpack(profile);
            }

            try
            {
                _homePage.SetPlayButtonState("Переустановка...", false);
                _homePage.SetActionsEnabled(false);
                _homePage.ShowProgress(true);
                _homePage.SetProgress(0, "Удаление старых файлов...");

                _syncService.DeleteModpackFiles(pack.Name, _appSettings.ProfilesPath);

                var progress = new Progress<SyncProgressReport>(report =>
                {
                    _homePage.SetProgress(report.Percentage, report.Status);
                });

                var manifest = await SyncWithRetryAsync(pack, progress);

                if (manifest != null)
                {
                    pack.Version = manifest.Version;

                    if (!string.IsNullOrWhiteSpace(manifest.Loader))
                        pack.Loader = manifest.Loader;

                    if (!string.IsNullOrWhiteSpace(manifest.Minecraft))
                        pack.Minecraft = manifest.Minecraft;

                    profile.Init(pack);
                    profile.SetUpdateAvailable(false);
                    _appSettings.Save();
                }

                ShowToast($"Сборка «{pack.Name}» переустановлена с нуля", ToastType.Success);
            }
            catch (OperationCanceledException)
            {
                ShowToast("Переустановка отменена", ToastType.Info);
            }
            catch (Exception ex)
            {
                ShowToast($"Ошибка при переустановке: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                _homePage.ShowProgress(false);
                _homePage.HideSyncError();
                _homePage.SetActionsEnabled(true);
                if (_selectedModpack != null)
                {
                    _ = CheckModpackStatusAsync(_selectedModpack.PackData);
                }
                _ = RefreshLocalChangesBadgeAsync(profile, pack);
            }
        }

        private async Task MonitorGameProcessAsync(Process process, string packName)
        {
            var startedAt = DateTime.UtcNow;

            try
            {
                await process.WaitForExitAsync();
            }
            catch
            {
                return;
            }

            try
            {
                Dispatcher.Invoke(() =>
                {
                    if (_runningGameProcess == process)
                    {
                        _runningGameProcess = null;
                        _runningGamePackName = null;
                        if (_selectedModpack != null)
                        {
                            _ = CheckModpackStatusAsync(_selectedModpack.PackData);
                        }
                    }
                });
            }
            catch
            {
            }

            bool crashedEarly = process.ExitCode != 0 && DateTime.UtcNow - startedAt < TimeSpan.FromSeconds(15);
            if (!crashedEarly) return;

            try
            {
                Dispatcher.Invoke(() => ShowToast(
                    $"«{packName}» неожиданно завершился (код {process.ExitCode}) вскоре после запуска. Проверьте логи в папке сборки.",
                    ToastType.Error));
            }
            catch
            {
            }
        }

        private IProgress<SyncProgressReport> WrapProgress(bool relabelAsUpdate)
        {
            return new Progress<SyncProgressReport>(report =>
            {
                string status = report.Status;
                if (relabelAsUpdate)
                {
                    if (status.StartsWith("Загрузка", StringComparison.Ordinal))
                        status = "Обновление" + status.Substring("Загрузка".Length);
                }
                _homePage.SetProgress(report.Percentage, status);
            });
        }

        private async Task<ManifestModel?> SyncWithRetryAsync(PasyotPack pack, IProgress<SyncProgressReport> progress, bool strict = false, ModpackSyncService.SyncOutcome? outcome = null)
        {
            while (true)
            {
                try
                {
                    var result = await _syncService.SyncAsync(pack, _appSettings.ProfilesPath, progress, strict, outcome);
                    _homePage.HideSyncError();
                    return result;
                }
                catch (HttpRequestException) when (_syncService.IsInstalled(pack.Name, _appSettings.ProfilesPath))
                {
                    throw;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
                {
                    bool retry = await WaitForRetryOrCancelAsync($"Ошибка загрузки: {ex.Message}");
                    if (!retry)
                    {
                        throw new OperationCanceledException("Загрузка отменена пользователем.");
                    }
                }
            }
        }

        private async Task<bool> WaitForRetryOrCancelAsync(string errorMessage)
        {
            _homePage.ShowSyncError(errorMessage);

            var tcs = new TaskCompletionSource<bool>();
            void RetryHandler(object? s, EventArgs e) => tcs.TrySetResult(true);
            void CancelHandler(object? s, EventArgs e) => tcs.TrySetResult(false);

            _homePage.RetryNowRequested += RetryHandler;
            _homePage.CancelRetryRequested += CancelHandler;

            try
            {
                for (int secondsLeft = 10; secondsLeft > 0; secondsLeft--)
                {
                    _homePage.SetSyncErrorCountdown(secondsLeft);
                    var delayTask = Task.Delay(1000);
                    var completed = await Task.WhenAny(delayTask, tcs.Task);
                    if (completed == tcs.Task) return await tcs.Task;
                }

                return true;
            }
            finally
            {
                _homePage.RetryNowRequested -= RetryHandler;
                _homePage.CancelRetryRequested -= CancelHandler;
                _homePage.HideSyncError();
            }
        }

        private async Task ShowUpdateChangelogAsync(PasyotPack pack)
        {
            try
            {
                var preview = await _syncService.PreviewUpdateAsync(pack, _appSettings.ProfilesPath);
                if (preview == null || preview.Value.ChangedFiles.Count == 0) return;

                var (manifest, changedFiles) = preview.Value;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Доступно обновление «{pack.Name}»: v{pack.Version} → v{manifest.Version}");

                if (!string.IsNullOrWhiteSpace(manifest.Notes))
                {
                    sb.AppendLine();
                    sb.AppendLine(manifest.Notes.Trim());
                }

                sb.AppendLine();
                sb.AppendLine($"Изменено файлов: {changedFiles.Count}");
                foreach (var file in changedFiles.Take(15))
                {
                    sb.AppendLine($" • {file.Path}");
                }
                if (changedFiles.Count > 15)
                {
                    sb.AppendLine($" …и ещё {changedFiles.Count - 15}");
                }

                MessageBox.Show(sb.ToString(), "Изменения в обновлении", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch
            {
            }
        }

        private void AddModpack(string? preloadPath = null)
        {
            var window = new AddModpackWindow(_appSettings.InstalledPacks.Select(p => p.Name), preloadPath) { Owner = this };

            if (window.ShowDialog() == true && window.SelectedPack != null)
            {
                var pack = window.SelectedPack;

                if (_appSettings.InstalledPacks.Any(p => p.Name == pack.Name))
                {
                    ShowToast($"Сборка «{pack.Name}» уже добавлена", ToastType.Info);
                    return;
                }

                _appSettings.InstalledPacks.Add(pack);

                var modpackUI = CreateModpackProfileUI(pack);
                _libraryPage.ItemsPanel.Children.Add(modpackUI);
                _libraryPage.SetEmpty(false);

                SelectModpack(modpackUI);
                _appSettings.Save();

                ShowToast($"Сборка «{pack.Name}» добавлена", ToastType.Success);
                NavHome.IsChecked = true;
            }
        }

        private void NavHome_Checked(object sender, RoutedEventArgs e)
        {
            if (!_shellReady) return;
            PageHost.Content = _homePage;
        }

        private void NavLibrary_Checked(object sender, RoutedEventArgs e)
        {
            if (!_shellReady) return;
            PageHost.Content = _libraryPage;
        }

        private void NavSkin_Checked(object sender, RoutedEventArgs e)
        {
            if (!_shellReady) return;
            PageHost.Content = _skinPage;
            _skinPage.EnsureLoaded();
        }

        private void NavSettings_Checked(object sender, RoutedEventArgs e)
        {
            if (!_shellReady) return;
            _settingsPage.LoadFromSettings();
            PageHost.Content = _settingsPage;
        }

        private void Window_DragEnter(object sender, DragEventArgs e)
        {
            e.Effects = IsPasyotPackDrag(e) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (!IsPasyotPackDrag(e)) return;

            string path = ((string[])e.Data.GetData(DataFormats.FileDrop)!)
                .First(f => f.EndsWith(".pasyotpack", StringComparison.OrdinalIgnoreCase));

            AddModpack(path);
        }

        private static bool IsPasyotPackDrag(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return false;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            return files.Any(f => f.EndsWith(".pasyotpack", StringComparison.OrdinalIgnoreCase));
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ProfileArea_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ProfileMenuPopup.IsOpen = !ProfileMenuPopup.IsOpen;
        }

        private void LogoutButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileMenuPopup.IsOpen = false;
            AuthService.Logout();
            ShowLoginFrame();
        }

        private async Task UpdateUiState()
        {
            UserSession? session = SecureStorage.LoadSession();

            if (session == null || string.IsNullOrEmpty(session.AccessToken))
            {
                ShowLoginFrame();
                return;
            }

            if (!string.IsNullOrEmpty(session.BackendSessionToken))
            {
                AuthService.RestoreBackendSessionToken(session.BackendSessionToken);
            }

            var (profile, isInvalidSession) = await VedrowAuth.ValidateAndGetProfileAsync(session);

            if (isInvalidSession)
            {
                SecureStorage.ClearSession();
                ShowLoginFrame();
            }
            else if (profile != null)
            {
                MainFrame.Visibility = Visibility.Collapsed;
                MainFrame.Content = null;
                SetupProfile(profile);
            }
            else
            {
                MainFrame.Visibility = Visibility.Collapsed;
                MainFrame.Content = null;
                SetupProfile(AuthService.CurrentUser ?? new UserProfile { Name = "Player (Offline)" });
            }
        }

        private void ShowLoginFrame()
        {
            MainFrame.Visibility = Visibility.Visible;
            MainFrame.Navigate(new LoginPage());
        }

        private void SetupProfile(UserProfile? profile)
        {
            if (!string.IsNullOrEmpty(profile?.AvatarUrl))
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(profile.AvatarUrl, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                ProfileIcon.Fill = new ImageBrush(bitmap) { Stretch = Stretch.UniformToFill };
            }

            ProfileText.Text = profile?.Name ?? "Player";
        }

        private void LoginSucces(object? sender, UserProfile userProfile)
        {
            MainFrame.Visibility = Visibility.Collapsed;
            MainFrame.Content = null;
            SetupProfile(userProfile);

            BringToFront();
        }

        private void BringToFront()
        {
            if (WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }

            Show();
            Activate();

            Topmost = true;
            Topmost = false;

            Focus();
        }

        internal void ShowToast(string message, ToastType type = ToastType.Info, string? copyText = null)
        {
            var (background, accent) = type switch
            {
                ToastType.Success => ("#1F3D2B", "#4ADE80"),
                ToastType.Error => ("#3D1F1F", "#FF6B6B"),
                _ => ("#1E1E22", "#0A84FF")
            };

            var card = new Border
            {
                Background = new BrushConverter().ConvertFromString(background) as Brush,
                BorderBrush = new BrushConverter().ConvertFromString(accent) as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 8, 0, 0),
                Opacity = 0
            };

            var content = new StackPanel { Orientation = Orientation.Horizontal };

            content.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = copyText != null ? 230 : 280
            });

            if (copyText != null)
            {
                var copyButton = new Button
                {
                    Content = "⧉",
                    Width = 24,
                    Height = 24,
                    Margin = new Thickness(8, 0, 0, 0),
                    Background = Brushes.Transparent,
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Скопировать текст ошибки"
                };
                copyButton.Click += (s, e) =>
                {
                    try { Clipboard.SetText(copyText); } catch { }
                };
                content.Children.Add(copyButton);
            }

            card.Child = content;
            ToastHost.Children.Add(card);

            card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(copyText != null ? 8 : 4) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
                fadeOut.Completed += (s2, e2) => ToastHost.Children.Remove(card);
                card.BeginAnimation(OpacityProperty, fadeOut);
            };
            timer.Start();
        }
    }
}
