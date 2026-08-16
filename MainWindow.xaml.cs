using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.ProcessBuilder;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Pasyot_Launcher
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void LaunchMinecraftBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                LaunchMinecraftBtn.IsEnabled = false;
                await LaunchMinecraftAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Произошла ошибка: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LaunchMinecraftBtn.IsEnabled = true;
            }

        }


        private async Task LaunchMinecraftAsync()
        {
            var path = "C:\\Users\\arter\\Desktop\\profiles\\one";

            var launcher = new MinecraftLauncher(path);

            launcher.FileProgressChanged += (sender, args) =>
            {
                FileProgressBar.Value = args.ProgressedTasks;
            };

            launcher.ByteProgressChanged += (sender, args) =>
            {
                double percent = args.TotalBytes > 0
                    ? (double)args.ProgressedBytes / args.TotalBytes * 100
                    : 0;
                //Console.WriteLine($"Скачано: {percent:F1}%");
                ByteProgressBar.Value = (int)percent;
            };

            string version = "1.20.1";   // любая версия
            await launcher.InstallAsync(version);

            var session = MSession.CreateOfflineSession("sosyat");   // любой ник

            var option = new MLaunchOption
            {
                Session = session,
                MaximumRamMb = 4096,          // максимум ОЗУ
                                              // MinimumRamMb = 1024,       // минимум (опционально)
                                              // ServerIp = "play.твойсервер.ru",  // сразу подключаться к серверу
                                              // ServerPort = 25565,
                                              // FullScreen = true,
            };

            // --- Сборка процесса и запуск ---
            var process = await launcher.BuildProcessAsync(version, option);
            process.Start();
        }


    }
}