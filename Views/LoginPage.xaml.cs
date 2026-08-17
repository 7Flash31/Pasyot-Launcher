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
            try
            {
                await VedrowAuth.StartAuthAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }
    }
}
