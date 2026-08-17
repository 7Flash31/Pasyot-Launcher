using Pasyot_Launcher.Models;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Pasyot_Launcher.Views
{
    public partial class ModpackProfile : UserControl
    {
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
            MinecraftVersion.Text = $"v{pack.Version}";
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