using Pasyot_Launcher.Models;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace Pasyot_Launcher.Services
{
    // Downloads and decodes a modpack's icon once, then serves it from memory for every card/page
    // that asks for the same sha256 - icons don't change without a new hash, so there's nothing to
    // invalidate.
    internal static class IconCache
    {
        private static readonly ConcurrentDictionary<string, Task<BitmapImage?>> Cache = new();

        public static Task<BitmapImage?> GetAsync(HttpClient httpClient, PasyotPack pack)
        {
            if (string.IsNullOrWhiteSpace(pack.IconSha256) || string.IsNullOrWhiteSpace(pack.Server))
                return Task.FromResult<BitmapImage?>(null);

            string url = pack.Server.TrimEnd('/') + "/objects/" + pack.IconSha256;
            return Cache.GetOrAdd(pack.IconSha256, _ => LoadAsync(httpClient, url));
        }

        private static async Task<BitmapImage?> LoadAsync(HttpClient httpClient, string url)
        {
            try
            {
                byte[] bytes = await httpClient.GetByteArrayAsync(url);

                using var stream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                return null;
            }
        }
    }
}
