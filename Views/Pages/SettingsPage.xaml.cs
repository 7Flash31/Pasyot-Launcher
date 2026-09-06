using Microsoft.Win32;
using System;
using System.Windows;
using System.Windows.Controls;

namespace Pasyot_Launcher.Views.Pages
{
    public partial class SettingsPage : UserControl
    {
        private readonly AppSettings _settings;

        public event EventHandler? OnSaved;
        public event EventHandler<string>? OnError;

        public SettingsPage() : this(AppSettings.Instance)
        {
        }

        public SettingsPage(AppSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            long totalBytes = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            int totalRamMb = (int)(totalBytes / (1024 * 1024));
            RamSlider.Maximum = totalRamMb;
            MaxRamLabel.Text = $"{totalRamMb} МБ";

            LoadFromSettings();
        }

        public void LoadFromSettings()
        {
            ProfilesPathTextBox.Text = _settings.ProfilesPath;
            RamSlider.Value = Math.Min(Math.Max(_settings.RamMb, 1024), RamSlider.Maximum);

            JavaArgsTextBox.Text = _settings.JavaArgs;
            EnvVarsTextBox.Text = _settings.EnvVars;
        }

        private void BrowseProfilesFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { Multiselect = false };

            if (dialog.ShowDialog() == true)
            {
                ProfilesPathTextBox.Text = dialog.FolderName;
            }
        }

        private void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            LoadFromSettings();
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settings.ProfilesPath = ProfilesPathTextBox.Text;
                _settings.RamMb = (int)Math.Round(RamSlider.Value);
                _settings.JavaArgs = JavaArgsTextBox.Text;
                _settings.EnvVars = EnvVarsTextBox.Text;

                _settings.Save();

                OnSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                OnError?.Invoke(this, $"Ошибка сохранения настроек: {ex.Message}");
            }
        }
    }
}
