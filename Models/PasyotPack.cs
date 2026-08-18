using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Pasyot_Launcher.Models
{
    // Формат файла .pasyotpack — см. API.md, раздел "GET /modpacks/{name}/pack".
    public class PasyotPack
    {
        [JsonPropertyName("format")]
        public int Format { get; set; }

        [JsonPropertyName("server")]
        public string Server { get; set; } = "";

        // Единственный идентификатор сборки: он же slug, он же имя папки на диске.
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("loader")]
        public string? Loader { get; set; }

        [JsonPropertyName("minecraft")]
        public string? Minecraft { get; set; }

        [JsonPropertyName("version")]
        public int Version { get; set; }

        // Готовый абсолютный адрес манифеста — собирать его самим не нужно.
        [JsonPropertyName("manifest")]
        public string Manifest { get; set; } = "";
    }
}
