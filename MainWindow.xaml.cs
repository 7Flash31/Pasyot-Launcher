using Pasyot_Launcher.Models;
using Pasyot_Launcher.Services;
using Pasyot_Launcher.Views;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static Pasyot_Launcher.Services.ModpackSyncService;

namespace Pasyot_Launcher
{
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

            _appSettings = SettingsService.GetConfig();

            LoadSavedModpacks(); 
            _ = UpdateUiState();
            VedrowAuth.OnAuthCompleted += LoginSucces;
        }

        private void LoadSavedModpacks()
        {
            ModpackStackPanel.Children.Clear();

            if (_appSettings.InstalledPacks == null || _appSettings.InstalledPacks.Count == 0)
            {
                LaunchMinecraftBtn.IsEnabled = false;
                return;
            }

            ModpackProfile? packToSelect = null;

            foreach (var pack in _appSettings.InstalledPacks)
            {
                var profile = CreateModpackProfileUI(pack);
                ModpackStackPanel.Children.Add(profile);

                if (pack.Modpack == _appSettings.SelectedSlug)
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

            return modpack;
        }

        private void DeleteModpack(ModpackProfile profile)
        {
            var pack = profile.PackData;

            var result = MessageBox.Show(
                $"Вы действительно хотите удалить сборку \"{pack.Modpack}\" и все её файлы с диска?",
                "Удаление сборки",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                _syncService.DeleteModpackFiles(pack.Modpack, _appSettings.ProfilesPath);

                var installedPack = _appSettings.InstalledPacks.FirstOrDefault(p => p.Modpack == pack.Modpack);
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
                        _appSettings.SelectedSlug = null;
                        LaunchMinecraftBtn.Content = "Запустить";
                        LaunchMinecraftBtn.IsEnabled = false;
                    }
                }

                SettingsService.SaveConfig(_appSettings);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при удалении сборки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SelectModpack(ModpackProfile profile)
        {
            _selectedModpack = profile;
            _appSettings.SelectedSlug = profile.PackData.Modpack;
            SettingsService.SaveConfig(_appSettings);

            LaunchMinecraftBtn.IsEnabled = true;

            _ = CheckModpackStatusAsync(profile.PackData);
        }

        private async Task CheckModpackStatusAsync(PasyotPack pack)
        {
            if (!_syncService.IsInstalled(pack.Modpack, _appSettings.ProfilesPath))
            {
                LaunchMinecraftBtn.Content = "Скачать";
                LaunchMinecraftBtn.IsEnabled = true; 
                return;
            }

            try
            {
                string baseUrl = pack.Server.TrimEnd('/');
                string manifestUrl = !string.IsNullOrEmpty(pack.ManifestUrl)
                    ? pack.ManifestUrl
                    : $"{baseUrl}/modpacks/{pack.Modpack}/manifest";

                var manifest = await HttpClient.GetFromJsonAsync<ManifestModel>(manifestUrl);

                if (manifest != null && manifest.Version > pack.Version)
                {
                    LaunchMinecraftBtn.Content = "Обновить";
                }
                else
                {
                    LaunchMinecraftBtn.Content = "Запустить";
                }
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
                MessageBox.Show("Выберите сборку из списка!", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                LaunchMinecraftBtn.IsEnabled = false;

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

                var manifest = await _syncService.SyncAsync(_selectedModpack.PackData, _appSettings.ProfilesPath, progress);

                string loader = !string.IsNullOrEmpty(manifest?.Loader)
                    ? manifest.Loader
                    : _selectedModpack.PackData.Loader;

                await _minecraftService.LaunchAsync(
                    _selectedModpack.PackData,
                    _appSettings,
                    ProfileText.Text,
                    loader,
                    progress 
                );

                LaunchMinecraftBtn.Content = "Запустить";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                DownloadProgressPanel.Visibility = Visibility.Collapsed;
                LaunchMinecraftBtn.IsEnabled = true;
            }
        }

        private void AddModpackButton_Click(object sender, RoutedEventArgs e)
        {
            var window = new AddModpackWindow { Owner = this };

            if (window.ShowDialog() == true && window.SelectedPack != null)
            {
                var pack = window.SelectedPack;

                if (!_appSettings.InstalledPacks.Any(p => p.Modpack == pack.Modpack))
                {
                    _appSettings.InstalledPacks.Add(pack);
                }

                var modpackUI = CreateModpackProfileUI(pack);
                ModpackStackPanel.Children.Add(modpackUI);

                SelectModpack(modpackUI);
                SettingsService.SaveConfig(_appSettings);
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            ModpackSettings modpackSettings = new ModpackSettings();
            modpackSettings.Show();
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
        }
    }
}