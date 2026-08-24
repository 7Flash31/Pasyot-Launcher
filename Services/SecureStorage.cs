using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Pasyot_Launcher.Services
{
    public class UserSession
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;

        public string BackendSessionToken { get; set; } = string.Empty;
    }

    internal class SecureStorage
    {
        private static readonly string FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "pasyot-launcher",
            "session.bin"
        );

        public static void SaveSession(UserSession session)
        {
            try
            {
                string directory = Path.GetDirectoryName(FilePath)!;
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                string json = JsonSerializer.Serialize(session);
                byte[] rawData = Encoding.UTF8.GetBytes(json);
                byte[] encryptedData = ProtectedData.Protect(rawData, null, DataProtectionScope.CurrentUser);

                string tempPath = FilePath + ".tmp";
                File.WriteAllBytes(tempPath, encryptedData);
                File.Move(tempPath, FilePath, overwrite: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка сохранения сессии: {ex.Message}");
            }
        }

        public static UserSession? LoadSession()
        {
            if (!File.Exists(FilePath))
                return null;

            try
            {
                byte[] encryptedData = File.ReadAllBytes(FilePath);
                byte[] rawData = ProtectedData.Unprotect(encryptedData, null, DataProtectionScope.CurrentUser);
                string json = Encoding.UTF8.GetString(rawData);

                return JsonSerializer.Deserialize<UserSession>(json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка чтения сессии: {ex.Message}");
                return null;
            }
        }

        public static void ClearSession()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    File.Delete(FilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка удаления файла сессии: {ex.Message}");
            }
        }
    }
}