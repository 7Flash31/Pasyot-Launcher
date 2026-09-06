using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Pasyot_Launcher.Services
{
    internal sealed class SyncBaseline
    {
        private readonly string _path;
        private readonly ConcurrentDictionary<string, string> _hashes;

        private SyncBaseline(string path, Dictionary<string, string> initial)
        {
            _path = path;
            _hashes = new ConcurrentDictionary<string, string>(initial, StringComparer.OrdinalIgnoreCase);
        }

        public static SyncBaseline Load(string modpackDir, string packName)
        {
            string path = StatePath(modpackDir);
            var data = TryReadFrom(path);

            if (data == null)
            {
                data = TryReadFrom(LegacyStatePath(packName));
            }

            return new SyncBaseline(path, data ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        private static Dictionary<string, string>? TryReadFrom(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
                return loaded != null ? new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase) : null;
            }
            catch
            {
                return null;
            }
        }

        private static string StatePath(string modpackDir) => Path.Combine(modpackDir, ".pasyot", "sync-state.json");

        private static string LegacyStatePath(string packName)
        {
            string dir = Path.Combine(Path.GetDirectoryName(AppSettings.SettingsFilePath)!, "sync-state");
            return Path.Combine(dir, SanitizeFileName(packName) + ".json");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        public string? Get(string relativePath) => _hashes.TryGetValue(relativePath, out var hash) ? hash : null;

        public void Set(string relativePath, string sha256) => _hashes[relativePath] = sha256;

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                string tmp = _path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(_hashes));
                File.Move(tmp, _path, overwrite: true);
            }
            catch
            {
            }
        }
    }
}
