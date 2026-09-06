using Pasyot_Launcher.Models;
using Pasyot_Launcher.Services;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace Pasyot_Launcher.Views
{
    public partial class ModpackSettingsWindow : Window
    {
        private readonly PasyotPack _pack;
        private readonly ModpackSyncService _syncService;
        private readonly AppSettings _appSettings;

        public bool ResetRequested { get; private set; }

        public ModpackSettingsWindow(PasyotPack pack, ModpackSyncService syncService, AppSettings appSettings)
        {
            InitializeComponent();

            _pack = pack;
            _syncService = syncService;
            _appSettings = appSettings;

            PackNameText.Text = pack.Name;
            PackSubtitleText.Text = string.Join(" · ", new[]
            {
                !string.IsNullOrWhiteSpace(pack.Minecraft) ? pack.Minecraft : $"v{pack.Version}",
                pack.Loader
            }, StringSplitOptions.RemoveEmptyEntries);

            Loaded += async (s, e) => await LoadChangedFilesAsync();
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadChangedFilesAsync();

        private async Task LoadChangedFilesAsync()
        {
            if (!_syncService.IsInstalled(_pack.Name, _appSettings.ProfilesPath))
            {
                ChangedFilesList.Visibility = Visibility.Collapsed;
                StatusText.Text = "Сборка ещё не установлена";
                StatusText.Visibility = Visibility.Visible;
                ResetButton.IsEnabled = false;
                return;
            }

            RefreshButton.IsEnabled = false;
            ResetButton.IsEnabled = false;
            ChangedFilesList.Visibility = Visibility.Collapsed;
            StatusText.Text = "Проверка файлов...";
            StatusText.Visibility = Visibility.Visible;

            try
            {
                var modified = await _syncService.GetLocallyModifiedFilesAsync(_pack, _appSettings.ProfilesPath);

                if (modified.Count == 0)
                {
                    StatusText.Text = "Нет изменённых файлов — всё совпадает со сборкой на сервере";
                    StatusText.Visibility = Visibility.Visible;
                    ChangedFilesList.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ChangedFilesHeaderText.Text = $"Изменённые локально файлы ({modified.Count})";
                    ChangedFilesList.ItemsSource = modified;
                    ChangedFilesList.Visibility = Visibility.Visible;
                    StatusText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Не удалось проверить файлы: {ex.Message}";
                StatusText.Visibility = Visibility.Visible;
                ChangedFilesList.Visibility = Visibility.Collapsed;
            }
            finally
            {
                RefreshButton.IsEnabled = true;
                ResetButton.IsEnabled = true;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            ResetRequested = true;
            DialogResult = true;
            Close();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
