using Pasyot_Launcher.Models;
using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

public class AppSettings
{
    public string ProfilesPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pasyot-launcher",
        "profiles");

    public int RamMb { get; set; } = 4096;

    public string JavaArgs { get; set; } = string.Empty;

    public string EnvVars { get; set; } = string.Empty;

    public List<PasyotPack> InstalledPacks { get; set; } = new();

    public string SelectedSlug { get; set; } = string.Empty;

    public static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "pasyot-launcher",
        "settings.json");

    private static AppSettings? _instance;
    private static readonly object _lock = new();

    public static AppSettings Instance
    {
        get
        {
            lock (_lock)
            {
                return _instance ??= LoadFromDisk();
            }
        }
    }

    public static AppSettings Reload()
    {
        lock (_lock)
        {
            _instance = LoadFromDisk();
            return _instance;
        }
    }

    private static AppSettings LoadFromDisk()
    {
        try
        {
            if (File.Exists(SettingsFilePath))
            {
                string json = File.ReadAllText(SettingsFilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json);

                if (loaded != null)
                    return loaded;

                Debug.WriteLine(
                    $"[AppSettings] Файл {SettingsFilePath} пуст или имеет неверный формат. " +
                    "Используются настройки по умолчанию.");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(
                $"[AppSettings] Не удалось прочитать {SettingsFilePath}: {ex.Message}. " +
                "Используются настройки по умолчанию.");
        }

        return new AppSettings();
    }

    public void Save()
    {
        string? directory = Path.GetDirectoryName(SettingsFilePath);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(
            this,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        File.WriteAllText(SettingsFilePath, json);

        lock (_lock)
        {
            _instance = this;
        }
    }
}