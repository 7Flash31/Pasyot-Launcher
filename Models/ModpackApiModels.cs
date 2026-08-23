using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Models
{
    public class ModpackSummary
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("loader")]
        public string? Loader { get; set; }

        [JsonPropertyName("minecraft")]
        public string? Minecraft { get; set; }

        [JsonPropertyName("latest_version")]
        public int LatestVersion { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }
    }

    public class ManifestGroup
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("optional")]
        public bool Optional { get; set; }

        [JsonPropertyName("files")]
        public int Files { get; set; }

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }
    }

    public class ManifestModel
    {
        [JsonPropertyName("format")]
        public int Format { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("loader")]
        public string? Loader { get; set; }

        [JsonPropertyName("minecraft")]
        public string? Minecraft { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("groups")]
        public List<ManifestGroup> Groups { get; set; } = new();

        [JsonPropertyName("file_count")]
        public int FileCount { get; set; }

        [JsonPropertyName("total_bytes")]
        public long TotalBytes { get; set; }

        [JsonPropertyName("created_at")]
        public string? CreatedAt { get; set; }

        [JsonPropertyName("bundle")]
        public ManifestBundle? Bundle { get; set; }

        [JsonPropertyName("files")]
        public List<ManifestFile> Files { get; set; } = new();
    }

    public class ManifestBundle
    {
        [JsonPropertyName("format")]
        public string Format { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("files")]
        public int Files { get; set; }

        [JsonPropertyName("bytes")]
        public long Bytes { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    public class ManifestFile
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("group")]
        public string Group { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";

        [JsonPropertyName("optional")]
        public bool Optional { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("bundled")]
        public bool Bundled { get; set; }
    }
}
