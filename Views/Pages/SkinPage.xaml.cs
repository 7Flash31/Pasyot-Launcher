using Microsoft.Win32;
using Pasyot_Launcher.Services;
using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace Pasyot_Launcher.Views.Pages
{
    public partial class SkinPage : UserControl
    {
        private readonly SkinService _skinService;
        private string? _selectedFilePath;
        private bool _loaded;

        public event EventHandler<string>? OnError;
        public event EventHandler<string>? OnSuccess;

        public SkinPage(HttpClient httpClient)
        {
            InitializeComponent();
            _skinService = new SkinService(httpClient);
        }

        public async void EnsureLoaded()
        {
            if (_loaded && PasyotBackendAuth.CurrentUser != null)
            {
                await RefreshSkinAsync();
                return;
            }

            SetState(connect: true);

            var user = await PasyotBackendAuth.EnsureAuthenticatedAsync();
            if (user == null)
            {
                SetState(connect: true);
                return;
            }

            _loaded = true;
            await RefreshSkinAsync();
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!AuthService.IsLoggedIn)
            {
                OnError?.Invoke(this, "Сначала войдите в лаунчер через Vedrow");
                SetState(connect: true);
                return;
            }

            SetState(loading: true);
            var user = await PasyotBackendAuth.ExchangeAsync();
            if (user == null)
            {
                OnError?.Invoke(this, "Не удалось подключиться к аккаунту, попробуйте ещё раз");
                SetState(connect: true);
                return;
            }

            _loaded = true;
            await RefreshSkinAsync();
        }

        private async Task RefreshSkinAsync()
        {
            SetState(loading: true);

            try
            {
                var skin = await _skinService.GetMySkinAsync(AuthService.BackendSessionToken);
                SetState(editor: true);

                if (skin == null)
                {
                    NoSkinText.Visibility = Visibility.Visible;
                    SkinModelText.Visibility = Visibility.Collapsed;
                    SkinPreviewImage.Source = null;
                }
                else
                {
                    NoSkinText.Visibility = Visibility.Collapsed;
                    SkinModelText.Visibility = Visibility.Visible;
                    SkinModelText.Text = $"Модель: {(skin.Model == "slim" ? "тонкие руки (Alex)" : "классика (Steve)")}";
                    LoadPreview(PasyotBackendAuth.CurrentUser!.Username);
                }
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Не удалось загрузить данные скина: {ex.Message}");
                SetState(editor: true);
            }
        }

        private void LoadPreview(string username)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(SkinService.TextureUrl(username), UriKind.Absolute);
            bitmap.EndInit();
            SkinPreviewImage.Source = bitmap;
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "PNG (*.png)|*.png",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true) return;

            _selectedFilePath = dialog.FileName;
            FilePathTextBox.Text = dialog.FileName;
            UploadButton.IsEnabled = true;
        }

        private async void UploadButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedFilePath == null) return;

            string model = SlimModelRadio.IsChecked == true ? "slim" : "classic";

            UploadButton.IsEnabled = false;
            try
            {
                await _skinService.UploadSkinAsync(AuthService.BackendSessionToken, _selectedFilePath, model);
                OnSuccess?.Invoke(this, "Скин сохранён");
                await RefreshSkinAsync();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Не удалось сохранить скин: {ex.Message}");
            }
            finally
            {
                UploadButton.IsEnabled = _selectedFilePath != null;
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            string username = ImportUsernameTextBox.Text.Trim();
            if (string.IsNullOrEmpty(username)) return;

            ImportButton.IsEnabled = false;
            try
            {
                await _skinService.ImportSkinAsync(AuthService.BackendSessionToken, username);
                OnSuccess?.Invoke(this, $"Скин импортирован от «{username}»");
                await RefreshSkinAsync();
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Не удалось импортировать скин: {ex.Message}");
            }
            finally
            {
                ImportButton.IsEnabled = true;
            }
        }

        private void SetState(bool connect = false, bool loading = false, bool editor = false)
        {
            ConnectPanel.Visibility = connect ? Visibility.Visible : Visibility.Collapsed;
            LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
            EditorPanel.Visibility = editor ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
