using Pasyot_Launcher.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Pasyot_Launcher.Views
{
    public partial class ModpackProfile : UserControl
    {
        private static readonly SolidColorBrush SelectedBorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x7A, 0xCC));

        public PasyotPack? PackData { get; private set; }

        public event EventHandler<ModpackProfile>? OnSelected;
        public event EventHandler<ModpackProfile>? OnDelete;

        public ModpackProfile()
        {
            InitializeComponent();
        }

        public void Init(PasyotPack pack)
        {
            PackData = pack;
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
            // Margin компенсирует толщину рамки, чтобы карточка не "прыгала" в списке при выборе.
            CardBorder.BorderBrush = isSelected ? SelectedBorderBrush : Brushes.Transparent;
            CardBorder.BorderThickness = isSelected ? new Thickness(2) : new Thickness(0);
            CardBorder.Margin = isSelected ? new Thickness(2) : new Thickness(4);
        }

        public void SetUpdateAvailable(bool isAvailable)
        {
            UpdateBadge.Visibility = isAvailable ? Visibility.Visible : Visibility.Collapsed;
        }


        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true; 
            OnDelete?.Invoke(this, this);
        }

        private void CardBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            OnSelected?.Invoke(this, this);
        }
    }
}