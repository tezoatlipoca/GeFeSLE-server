using System.Text.Json.Serialization;

public class TokenData
{
    [JsonPropertyName("access_token")]
    public string ?AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string ?TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string ?Scope { get; set; }

    [JsonPropertyName("created_at")]
    public int CreatedAt { get; set; }
}