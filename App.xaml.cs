using Pasyot_Launcher.Services;
using System.Windows;

namespace Pasyot_Launcher
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            UserSession? session = SecureStorage.LoadSession();
            if (session != null)
            {
                AuthService.RestoreTokens(session.AccessToken, session.RefreshToken);
            }
        }
    }
}