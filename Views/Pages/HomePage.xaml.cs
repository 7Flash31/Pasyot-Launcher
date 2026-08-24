using Pasyot_Launcher.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Pasyot_Launcher.Views.Pages
{
    public partial class HomePage : UserControl
    {
        public event EventHandler? PlayRequested;
        public event EventHandler? OpenFolderRequested;
        public event EventHandler? VerifyRequested;

        private bool _suppressRamChanged = true;

        public HomePage()
        {
            InitializeComponent();
            RefreshRamChip();
        }

        public void SetSelectedPack(PasyotPack? pack)
        {
            if (pack == null)
            {
                PackNameText.Text = "Сборка не выбрана";
                PackVersionText.Text = "—";
                LoaderBadge.Visibility = Visibility.Collapsed;
                OpenFolderButton.IsEnabled = false;
                VerifyButton.IsEnabled = false;
                SetServerStatus(null);
                return;
            }

            PackNameText.Text = pack.Name;
            PackVersionText.Text = !string.IsNullOrWhiteSpace(pack.Minecraft) ? pack.Minecraft : $"v{pack.Version}";

            if (string.IsNullOrWhiteSpace(pack.Loader))
            {
                LoaderBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                LoaderBadgeText.Text = pack.Loader.ToUpperInvariant();
                LoaderBadge.Visibility = Visibility.Visible;
            }

            OpenFolderButton.IsEnabled = true;
            VerifyButton.IsEnabled = true;
        }

        public void SetPlayButtonState(string content, bool enabled)
        {
            LaunchButton.Content = content;
            LaunchButton.IsEnabled = enabled;
        }

        public void SetPlayButtonEnabled(bool enabled)
        {
            LaunchButton.IsEnabled = enabled;
        }

        public void SetActionsEnabled(bool enabled)
        {
            VerifyButton.IsEnabled = enabled;
        }

        public void ShowProgress(bool visible)
        {
            DownloadProgressPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetProgress(double percent, string? status)
        {
            FileProgressBar.Value = percent;
            ProgressPercentTextBlock.Text = $"{Math.Round(percent)}%";

            if (!string.IsNullOrEmpty(status))
            {
                StatusTextBlock.Text = status;
            }
        }

        public void SetServerStatus(bool? online)
        {
            if (online == null)
            {
                ServerStatusPanel.Visibility = Visibility.Collapsed;
                return;
            }

            ServerStatusPanel.Visibility = Visibility.Visible;

            if (online == true)
            {
                ServerStatusDot.Fill = (Brush)FindResource("SuccessBrush");
                ServerStatusText.Text = "Сервер онлайн";
                ServerStatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
            else
            {
                ServerStatusDot.Fill = (Brush)FindResource("ErrorBrush");
                ServerStatusText.Text = "Сервер недоступен";
                ServerStatusText.Foreground = (Brush)FindResource("TextSecondaryBrush");
            }
        }

        public void SetServerStatusChecking()
        {
            ServerStatusPanel.Visibility = Visibility.Visible;
            ServerStatusDot.Fill = (Brush)FindResource("TextMutedBrush");
            ServerStatusText.Text = "Проверка сервера...";
            ServerStatusText.Foreground = (Brush)FindResource("TextMutedBrush");
        }

        public void RefreshRamChip()
        {
            int ramMb = AppSettings.Instance.RamMb;
            RamChipText.Text = $"{ramMb} МБ";

            _suppressRamChanged = true;
            RamPopupSlider.Maximum = Math.Max(ramMb, RamPopupSlider.Maximum);
            RamPopupSlider.Value = ramMb;
            RamPopupValueText.Text = $"{ramMb} МБ";
            _suppressRamChanged = false;
        }

        private void LaunchButton_Click(object sender, RoutedEventArgs e) => PlayRequested?.Invoke(this, EventArgs.Empty);
        private void OpenFolderButton_Click(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke(this, EventArgs.Empty);
        private void VerifyButton_Click(object sender, RoutedEventArgs e) => VerifyRequested?.Invoke(this, EventArgs.Empty);

        private void RamChipButton_Click(object sender, RoutedEventArgs e)
        {
            RefreshRamChip();
            RamPopup.IsOpen = true;
        }

        private void RamPopupSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppressRamChanged) return;

            int ramMb = (int)Math.Round(e.NewValue);
            RamPopupValueText.Text = $"{ramMb} МБ";
            RamChipText.Text = $"{ramMb} МБ";

            AppSettings.Instance.RamMb = ramMb;
            AppSettings.Instance.Save();
        }
    }
}
