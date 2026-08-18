using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Pasyot_Launcher.Models;

namespace Pasyot_Launcher.Views
{
    public partial class AddModpackWindow : Window
    {
        private readonly HashSet<string> _existingSlugs;

        public PasyotPack? SelectedPack { get; private set; }

        public AddModpackWindow(IEnumerable<string>? existingSlugs = null)
        {
            InitializeComponent();
            _existingSlugs = new HashSet<string>(existingSlugs ?? Enumerable.Empty<string>());
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Pasyot Pack (*.pasyotpack)|*.pasyotpack|Все файлы (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var pack = ReadPasyotPack(dialog.FileName);

                PackPathTextBox.Text = dialog.FileName;
                ModpackNameText.Text = pack.Name;
                ModpackLoaderText.Text = string.IsNullOrWhiteSpace(pack.Loader) ? "—" : pack.Loader;
                ModpackMinecraftText.Text = string.IsNullOrWhiteSpace(pack.Minecraft) ? "—" : pack.Minecraft;
                ModpackVersionText.Text = pack.Version.ToString();
                ModpackServerText.Text = pack.Server;

                InfoPanel.Visibility = Visibility.Visible;
                AddButton.IsEnabled = true;

                SelectedPack = pack;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Не удалось прочитать файл сборки:\n{ex.Message}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                ClearInfo();
            }
        }

        private PasyotPack ReadPasyotPack(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Файл не найден");

            string json = File.ReadAllText(path);

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true 
            };

            var pack = JsonSerializer.Deserialize<PasyotPack>(json, options);

            if (pack == null)
                throw new Exception("Файл пустой или имеет неверный формат");

            if (pack.Format <= 0)
                throw new Exception("Неподдерживаемая или отсутствующая версия format");

            if (string.IsNullOrWhiteSpace(pack.Name))
                throw new Exception("В файле отсутствует поле name");

            if (_existingSlugs.Contains(pack.Name))
                throw new Exception($"Сборка \"{pack.Name}\" уже добавлена в лаунчер");

            if (string.IsNullOrWhiteSpace(pack.Manifest))
                throw new Exception("В файле отсутствует поле manifest");

            if (string.IsNullOrWhiteSpace(pack.Server))
                throw new Exception("В файле отсутствует поле server");

            if (pack.Version <= 0)
                throw new Exception("Некорректный номер версии");

            return pack;
        }

        private void ClearInfo()
        {
            PackPathTextBox.Text = "Файл не выбран";
            ModpackNameText.Text = "—";
            ModpackLoaderText.Text = "—";
            ModpackMinecraftText.Text = "—";
            ModpackVersionText.Text = "—";
            ModpackServerText.Text = "—";
            InfoPanel.Visibility = Visibility.Collapsed;
            AddButton.IsEnabled = false;
            SelectedPack = null;
        }

        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPack == null)
            {
                MessageBox.Show("Сначала выберите файл сборки", "Ошибка",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
