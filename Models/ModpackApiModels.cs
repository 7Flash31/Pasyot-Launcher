using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Models
{
    public class ModpackInfo
    {
        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";

        [JsonPropertyName("loader")]
        public string Loader { get; set; } = "";

        [JsonPropertyName("latest_version")]
        public int LatestVersion { get; set; }
    }

    public class ManifestModel
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("loader")]
        public string Loader { get; set; } = "";

        [JsonPropertyName("files")]
        public List<ManifestFile> Files { get; set; } = new();
    }

    public class ManifestFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }
}
