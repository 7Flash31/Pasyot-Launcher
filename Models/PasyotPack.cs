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

        [JsonPropertyName("modpack")]
        public string Modpack { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("loader")]
        public string Loader { get; set; } = ""; 

        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("manifest_url")]
        public string ManifestUrl { get; set; } = "";
    }
}
