using System;
using System.Text.Json.Serialization;

namespace haulages_bot.Models
{
    public class AuthResponse
    {
        [JsonPropertyName("access_token")]
        public string access_token { get; set; }

        [JsonPropertyName("token_type")]
        public string token_type { get; set; }

        [JsonPropertyName("refresh_token")]
        public string refresh_token { get; set; }

        [JsonPropertyName("expires_in")]
        public int expires_in { get; set; }
    }
}
