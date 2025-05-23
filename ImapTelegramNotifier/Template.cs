using System.Text.Json.Serialization;


namespace ImapTelegramNotifier
{
    public class Template
    {
        [JsonPropertyName("header")]
        public string? Header { get; set; }

        [JsonPropertyName("subheader")]
        public string? Subheader { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
        [JsonPropertyName("footer")]
        public string? Footer { get; set; } = null;
        [JsonPropertyName("formats")]
        public (string name, string format)[]? Formats { get; set; }
    }
}