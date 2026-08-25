using Pasyot_Launcher.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pasyot_Launcher.Views
{
    public partial class ModpackProfile : UserControl
    {
        private static readonly SolidColorBrush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

        public PasyotPack? PackData { get; private set; }

        public event EventHandler<ModpackProfile>? OnSelected;
        public event EventHandler<ModpackProfile>? OnDelete;
        public event EventHandler<ModpackProfile>? OnOpenSettings;

        public ModpackProfile()
        {
            InitializeComponent();
        }

        private static readonly Brush DefaultIconBrush = (Brush)Application.Current.Resources["SurfaceAltBrush"];

        public void Init(PasyotPack pack)
        {
            PackData = pack;
            IconEllipse.Fill = DefaultIconBrush;
            ModpackName.Text = pack.Name;

            MinecraftVersion.Text = !string.IsNullOrWhiteSpace(pack.Minecraft)
                ? pack.Minecraft
                : $"v{pack.Version}";
            MinecraftVersion.ToolTip = $"Версия сборки: {pack.Version}";

            if (string.IsNullOrWhiteSpace(pack.Loader))
            {
                LoaderBadge.Visibility = Visibility.Collapsed;
            }
            else
            {
                LoaderBadgeText.Text = pack.Loader.ToUpperInvariant();
                LoaderBadge.Visibility = Visibility.Visible;
            }
        }

        public void SetSelected(bool isSelected)
        {
            CardBorder.BorderBrush = isSelected ? SelectedBorderBrush : Brushes.Transparent;
            CardBorder.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
            CardBorder.Margin = isSelected ? new Thickness(2) : new Thickness(4);
        }

        public void SetIcon(BitmapImage icon)
        {
            IconEllipse.Fill = new ImageBrush(icon) { Stretch = Stretch.UniformToFill };
        }

        public void SetUpdateAvailable(bool isAvailable)
        {
            UpdateBadge.Visibility = isAvailable ? Visibility.Visible : Visibility.Collapsed;
        }

        public void SetLocallyModifiedFiles(IReadOnlyList<string> files)
        {
            if (files.Count == 0)
            {
                LocalChangesBadge.Visibility = Visibility.Collapsed;
                return;
            }

            LocalChangesBadgeText.Text = files.Count == 1 ? "1 ФАЙЛ ИЗМЕНЁН" : $"{files.Count} ФАЙЛОВ ИЗМЕНЕНО";
            LocalChangesBadge.ToolTip = "Изменено локально:\n" + string.Join("\n", files.Take(30)) +
                (files.Count > 30 ? $"\n… и ещё {files.Count - 30}" : "");
            LocalChangesBadge.Visibility = Visibility.Visible;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OnDelete?.Invoke(this, this);
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            OnOpenSettings?.Invoke(this, this);
        }

        private void CardBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnSelected?.Invoke(this, this);
        }
    }
}