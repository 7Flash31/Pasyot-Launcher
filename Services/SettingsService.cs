using System;
using System.IO;
using System.Text.Json;

namespace Pasyot_Launcher.Services
{
    public class SettingsService
    {
        private static readonly string ConfigFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "pasyot-launcher"
        );

        private static readonly string ConfigFilePath = Path.Combine(ConfigFolder, "config.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true 
        };

        public static AppSettings GetConfig()
        {
            try
            {
                if (File.Exists(ConfigFilePath))
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config Error: {ex.Message}");
            }

            return new AppSettings();
        }

        public static void SaveConfig(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(ConfigFolder))
                {
                    Directory.CreateDirectory(ConfigFolder);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(ConfigFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Config Error: {ex.Message}");
            }
        }
    }
}