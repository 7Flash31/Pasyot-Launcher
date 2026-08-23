using Pasyot_Launcher.Models;
using Pasyot_Launcher.Services;
using Pasyot_Launcher.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

        private AppSettings _appSettings;
        private ModpackProfile? _selectedModpack;

        public MainWindow()
        {
            InitializeComponent();
            _minecraftService = new MinecraftService(HttpClient);
            _syncService = new ModpackSyncService(HttpClient);

            _appSettings = AppSettings.Instance;

            LoadSavedModpacks();
            _ = UpdateUiState();
            VedrowAuth.OnAuthCompleted += LoginSucces;
        }

        private void LoadSavedModpacks()
        {
            ModpackStackPanel.Children.Clear();

            if (_appSettings.InstalledPacks == null || _appSettings.InstalledPacks.Count == 0)
            {
                EmptyModpacksText.Visibility = Visibility.Visible;
                LaunchMinecraftBtn.IsEnabled = false;
                return;
            }

            EmptyModpacksText.Visibility = Visibility.Collapsed;

            ModpackProfile? packToSelect = null;

            foreach (var pack in _appSettings.InstalledPacks)
            {
                var profile = CreateModpackProfileUI(pack);
                ModpackStackPanel.Children.Add(profile);

                if (pack.Name == _appSettings.SelectedSlug)
                {
                    packToSelect = profile;
                }
            }

            if (packToSelect != null)
            {
                SelectModpack(packToSelect);
            }
            else if (ModpackStackPanel.Children.Count > 0 && ModpackStackPanel.Children[0] is ModpackProfile firstProfile)
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

                ModpackStackPanel.Children.Remove(profile);

                if (_selectedModpack == profile)
                {
                    _selectedModpack = null;

                    if (ModpackStackPanel.Children.Count > 0 && ModpackStackPanel.Children[0] is ModpackProfile firstProfile)
                    {
                        SelectModpack(firstProfile);
                    }
                    else
                    {
                        _appSettings.SelectedSlug = string.Empty;
                        LaunchMinecraftBtn.Content = "Запустить";
                        LaunchMinecraftBtn.IsEnabled = false;
                        EmptyModpacksText.Visibility = Visibility.Visible;
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

            LaunchMinecraftBtn.Content = "Проверка...";
            LaunchMinecraftBtn.IsEnabled = false;

            _ = CheckModpackStatusAsync(profile.PackData);
        }

        private async Task CheckModpackStatusAsync(PasyotPack pack)
        {
            if (!_syncService.IsInstalled(pack.Name, _appSettings.ProfilesPath))
            {
                LaunchMinecraftBtn.Content = "Скачать";
                LaunchMinecraftBtn.IsEnabled = true;
                return;
            }

            try
            {
                int? latestVersion = await _syncService.GetLatestVersionAsync(pack);

                LaunchMinecraftBtn.Content = latestVersion.HasValue && latestVersion.Value > pack.Version
                    ? "Обновить"
                    : "Запустить";
            }
            catch (HttpRequestException)
            {
                LaunchMinecraftBtn.Content = "Запустить (Офлайн)";
            }
            catch
            {
                LaunchMinecraftBtn.Content = "Запустить";
            }
            finally
            {
                LaunchMinecraftBtn.IsEnabled = true;
            }
        }

        private async void LaunchMinecraftBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpack == null)
            {
                ShowToast("Выберите сборку из списка!", ToastType.Info);
                return;
            }

            try
            {
                LaunchMinecraftBtn.IsEnabled = false;
                VerifyFilesButton.IsEnabled = false;

                DownloadProgressPanel.Visibility = Visibility.Visible;
                FileProgressBar.Value = 0;
                StatusTextBlock.Text = "Подготовка...";
                ProgressPercentTextBlock.Text = "0%";

                var progress = new Progress<SyncProgressReport>(report =>
                {
                    FileProgressBar.Value = report.Percentage;
                    ProgressPercentTextBlock.Text = $"{Math.Round(report.Percentage)}%";

                    if (!string.IsNullOrEmpty(report.Status))
                    {
                        StatusTextBlock.Text = report.Status;
                    }
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

                LaunchMinecraftBtn.Content = "Запустить";
                ShowToast($"«{_selectedModpack.PackData.Name}» запущен", ToastType.Success);

                _ = MonitorGameProcessAsync(gameProcess, _selectedModpack.PackData.Name);
            }
            catch (Exception ex)
            {
                ShowToast($"Произошла ошибка: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                LaunchMinecraftBtn.IsEnabled = true;
                VerifyFilesButton.IsEnabled = true;
            }
        }

        private void OpenModpackFolderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedModpack == null)
            {
                ShowToast("Выберите сборку из списка!", ToastType.Info);
                return;
            }

            string modpackDir = Path.Combine(_appSettings.ProfilesPath, _selectedModpack.PackData.Name);

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

        private async void VerifyFilesButton_Click(object sender, RoutedEventArgs e)
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
                LaunchMinecraftBtn.IsEnabled = false;
                VerifyFilesButton.IsEnabled = false;

                DownloadProgressPanel.Visibility = Visibility.Visible;
                FileProgressBar.Value = 0;
                StatusTextBlock.Text = "Подготовка...";
                ProgressPercentTextBlock.Text = "0%";

                var progress = new Progress<SyncProgressReport>(report =>
                {
                    FileProgressBar.Value = report.Percentage;
                    ProgressPercentTextBlock.Text = $"{Math.Round(report.Percentage)}%";

                    if (!string.IsNullOrEmpty(report.Status))
                    {
                        StatusTextBlock.Text = report.Status;
                    }
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
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                LaunchMinecraftBtn.IsEnabled = true;
                VerifyFilesButton.IsEnabled = true;
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

        private void AddModpackButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AddModpackWindow(_appSettings.InstalledPacks.Select(p => p.Name)) { Owner = this };

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
                ModpackStackPanel.Children.Add(modpackUI);
                EmptyModpacksText.Visibility = Visibility.Collapsed;

                SelectModpack(modpackUI);
                _appSettings.Save();

                ShowToast($"Сборка «{pack.Name}» добавлена", ToastType.Success);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ModpackSettings modpackSettings = new ModpackSettings { Owner = this };
            modpackSettings.Show();
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
                _ => ("#252525", "#007ACC")
            };

            var card = new Border
            {
                Background = new BrushConverter().ConvertFromString(background) as Brush,
                BorderBrush = new BrushConverter().ConvertFromString(accent) as Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
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
                    Cursor = Cursors.Hand,
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