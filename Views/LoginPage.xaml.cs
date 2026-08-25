using System;
using System.Collections.Generic;
using System.Linq;
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
using System.Diagnostics;
using Pasyot_Launcher.Services;

namespace Pasyot_Launcher
{
    public partial class LoginPage : Page
    {
        public LoginPage()
        {
            InitializeComponent();
        }


        private async void LoginBtn_Click(object sender, RoutedEventArgs e)
        {
            LoginBtn.IsEnabled = false;
            LoginBtn.Content = "Ожидание авторизации...";
            LoginStatusText.Visibility = Visibility.Visible;
            ReopenSiteBtn.Visibility = Visibility.Visible;

            try
            {
                await VedrowAuth.StartAuthAsync();
            }
            catch (Exception ex)
            {
                (Application.Current.MainWindow as MainWindow)?.ShowToast(
                    $"Ошибка авторизации: {ex.Message}", ToastType.Error, ex.ToString());
            }
            finally
            {
                LoginBtn.IsEnabled = true;
                LoginBtn.Content = "Войти через Vedrow";
                LoginStatusText.Visibility = Visibility.Collapsed;
                ReopenSiteBtn.Visibility = Visibility.Collapsed;
            }
        }

        private void ReopenSiteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!VedrowAuth.ReopenAuthPage())
            {
                (Application.Current.MainWindow as MainWindow)?.ShowToast(
                    "Нет активной сессии авторизации для повторного открытия.", ToastType.Error);
            }
        }
    }
}
