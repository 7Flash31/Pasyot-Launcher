using Pasyot_Launcher.Models;

namespace Pasyot_Launcher.Services
{
    internal class AuthService
    {
        public static string AccessToken { get; private set; } = string.Empty;
        public static string RefreshToken { get; private set; } = string.Empty;
        public static string BackendSessionToken { get; private set; } = string.Empty;
        public static UserProfile? CurrentUser { get; private set; }

        public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

        public static void RestoreTokens(string accessToken, string refreshToken)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
        }

        public static void RestoreBackendSessionToken(string token)
        {
            BackendSessionToken = token;
        }

        public static void SaveSession(string accessToken, string refreshToken, UserProfile profile)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            CurrentUser = profile;

            SecureStorage.SaveSession(new UserSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                BackendSessionToken = BackendSessionToken
            });
        }

        public static void SaveBackendSessionToken(string token)
        {
            BackendSessionToken = token;

            SecureStorage.SaveSession(new UserSession
            {
                AccessToken = AccessToken,
                RefreshToken = RefreshToken,
                BackendSessionToken = token
            });
        }

        public static void Logout()
        {
            AccessToken = string.Empty;
            RefreshToken = string.Empty;
            BackendSessionToken = string.Empty;
            CurrentUser = null;

            SecureStorage.ClearSession();
        }
    }
}