using Pasyot_Launcher.Models;

namespace Pasyot_Launcher.Services
{
    internal class AuthService
    {
        public static string AccessToken { get; private set; } = string.Empty;
        public static string RefreshToken { get; private set; } = string.Empty;
        public static UserProfile? CurrentUser { get; private set; }

        public static bool IsLoggedIn => !string.IsNullOrEmpty(AccessToken);

        public static void SaveSession(string accessToken, string refreshToken, UserProfile profile)
        {
            AccessToken = accessToken;
            RefreshToken = refreshToken;
            CurrentUser = profile;

            SecureStorage.SaveSession(new UserSession
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken
            });
        }
    }
}