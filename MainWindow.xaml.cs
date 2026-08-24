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

            _ = RefreshUpdateBadgeAsync(modpack, pack);

            return modpack;
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
        }

        private async Task CheckModpackStatusAsync(PasyotPack pack)
        {
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
                _homePage.SetPlayButtonState("Скачать", serverOnline);
                return;
            }

            if (!serverOnline)
            {
                _homePage.SetPlayButtonState("Запустить (Офлайн)", true);
                return;
            }

            _homePage.SetPlayButtonState(
                latestVersion.HasValue && latestVersion.Value > pack.Version ? "Обновить" : "Запустить",
                true);
        }

        private async void LaunchSelectedModpack()
        {
            if (_selectedModpack == null)
            {
                ShowToast("Выберите сборку из списка!", ToastType.Info);
                return;
            }

            try
            {
                _homePage.SetPlayButtonState("Подготовка...", false);
                _homePage.SetActionsEnabled(false);

                _homePage.ShowProgress(true);
                _homePage.SetProgress(0, "Подготовка...");

                var progress = new Progress<SyncProgressReport>(report =>
                {
                    _homePage.SetProgress(report.Percentage, report.Status);
                });

                ManifestModel? manifest;
                try
                {
                    manifest = await _syncService.SyncAsync(_selectedModpack.PackData, _appSettings.ProfilesPath, progress);
                }
                catch (HttpRequestException) when (_syncService.IsInstalled(_selectedModpack.PackData.Name, _appSettings.ProfilesPath))
                {
                    manifest = null;
                    ShowToast("Нет соединения с сервером — запуск офлайн на уже установленных файлах", ToastType.Info);
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
                    progress
                );

                _homePage.SetPlayButtonState("Запустить", true);
                ShowToast($"«{_selectedModpack.PackData.Name}» запущен", ToastType.Success);

                _ = MonitorGameProcessAsync(gameProcess, _selectedModpack.PackData.Name);
            }
            catch (Exception ex)
            {
                ShowToast($"Произошла ошибка: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                _homePage.ShowProgress(false);
                _homePage.SetActionsEnabled(true);
                if (_selectedModpack != null)
                {
                    _ = CheckModpackStatusAsync(_selectedModpack.PackData);
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

                var manifest = await _syncService.SyncAsync(pack, _appSettings.ProfilesPath, progress);

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
            }
            catch (Exception ex)
            {
                ShowToast($"Ошибка при проверке файлов: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                _homePage.ShowProgress(false);
                _homePage.SetActionsEnabled(true);
                _ = CheckModpackStatusAsync(pack);
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
