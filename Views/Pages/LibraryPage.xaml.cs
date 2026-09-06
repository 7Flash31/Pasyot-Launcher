using System;
using System.Windows;
using System.Windows.Controls;

namespace Pasyot_Launcher.Views.Pages
{
    public partial class LibraryPage : UserControl
    {
        public event EventHandler? AddModpackRequested;

        public Panel ItemsPanel => ItemsHost;

        public LibraryPage()
        {
            InitializeComponent();
        }

        public void SetEmpty(bool isEmpty)
        {
            EmptyStateText.Visibility = isEmpty ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddModpackButton_Click(object sender, RoutedEventArgs e) => AddModpackRequested?.Invoke(this, EventArgs.Empty);
    }
}
