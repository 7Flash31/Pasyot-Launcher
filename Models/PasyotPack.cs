using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Models
{
    public class PasyotPack
    {
        [JsonPropertyName("format")]
        public int Format { get; set; }

        [JsonPropertyName("server")]
        public string Server { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("loader")]
        public string? Loader { get; set; }

        [JsonPropertyName("minecraft")]
        public string? Minecraft { get; set; }

        [JsonPropertyName("server_ip")]
        public string? ServerIp { get; set; }

        [JsonPropertyName("icon_sha256")]
        public string? IconSha256 { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("manifest")]
        public string Manifest { get; set; } = "";
    }
}
