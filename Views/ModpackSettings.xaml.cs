using Microsoft.Win32;
using System;
using System.Windows;

namespace Pasyot_Launcher.Views
{
    public partial class ModpackSettings : Window
    {
        private readonly AppSettings _settings;

        public ModpackSettings() : this(AppSettings.Instance)
        {
        }

        public ModpackSettings(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings ?? AppSettings.Instance;
            InitializeRamSlider();
            LoadSettings();
        }

        private void InitializeRamSlider()
        {
            long totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            int totalRamMb = (int)(totalBytes / (1024 * 1024));
            RamSlider.Maximum = totalRamMb;
            MaxRamLabel.Text = $"{totalRamMb} МБ";
        }

        private void LoadSettings()
        {
            ProfilesPathTextBox.Text = _settings.ProfilesPath;
            RamSlider.Value = Math.Min(Math.Max(_settings.RamMb, 1024), RamSlider.Maximum);
            ServerUrlTextBox.Text = _settings.ServerUrl;
            JavaArgsTextBox.Text = _settings.JavaArgs;
            EnvVarsTextBox.Text = _settings.EnvVars;
        }

        private void BrowseProfilesFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                ProfilesPathTextBox.Text = dialog.FolderName;
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.ProfilesPath = ProfilesPathTextBox.Text;
                _settings.RamMb = (int)Math.Round(RamSlider.Value);
                _settings.ServerUrl = ServerUrlTextBox.Text;
                _settings.JavaArgs = JavaArgsTextBox.Text;
                _settings.EnvVars = EnvVarsTextBox.Text;

                _settings.Save();

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Ошибка сохранения настроек:\n\n{ex}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}