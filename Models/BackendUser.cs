using System.Text.Json.Serialization;

namespace Pasyot_Launcher.Models
{
    public class BackendUser
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("email")]
        public string Email { get; set; } = "";

        [JsonPropertyName("avatar_url")]
        public string? AvatarUrl { get; set; }

        [JsonPropertyName("is_admin")]
        public bool IsAdmin { get; set; }
    }

    public class SkinInfo
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; } = "";
    }
}
