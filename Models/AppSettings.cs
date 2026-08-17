using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pasyot_Launcher.Models; 

namespace Pasyot_Launcher
{
    public class AppSettings
    {
        public string ProfilesPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".pasyot", "profiles");
        public int RamMb { get; set; } = 4096;
        public string ServerUrl { get; set; } = "https://pasyot.com";
        public string JavaArgs { get; set; } = string.Empty;
        public string EnvVars { get; set; } = string.Empty;

        public List<PasyotPack> InstalledPacks { get; set; } = new List<PasyotPack>();
        public string SelectedSlug { get; set; } = string.Empty;

        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ".pasyot",
            "settings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    string json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch
            {
            }

            return new AppSettings();
        }

        public void Save()
        {
            string dir = Path.GetDirectoryName(SettingsFilePath)!;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }
    }
}