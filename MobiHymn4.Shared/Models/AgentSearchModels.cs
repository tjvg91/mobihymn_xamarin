using Newtonsoft.Json;

namespace MobiHymn4.Shared.Models;

public class AgentSearchResponse
{
    [JsonProperty("query")]
    public string? Query { get; set; }

    [JsonProperty("results")]
    public List<AgentSearchResult> Results { get; set; } = new();

    [JsonProperty("fallback")]
    public bool Fallback { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}

public class AgentSearchResult
{
    [JsonProperty("number")]
    public string? Number { get; set; }

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("firstLine")]
    public string? FirstLine { get; set; }

    [JsonProperty("reason")]
    public string? Reason { get; set; }

    [JsonProperty("score")]
    public double Score { get; set; }
}

public class AgentChatResponse
{
    [JsonProperty("reply")]
    public string? Reply { get; set; }

    [JsonProperty("replyFormat")]
    public string? ReplyFormat { get; set; }

    [JsonProperty("suggestions")]
    public List<AgentSearchResult> Suggestions { get; set; } = new();

    [JsonProperty("sessionId")]
    public string? SessionId { get; set; }

    [JsonProperty("fallback")]
    public bool Fallback { get; set; }

    [JsonProperty("message")]
    public string? Message { get; set; }
}

public class AgentChatMessageDto
{
    [JsonProperty("role")]
    public string Role { get; set; } = "user";

    [JsonProperty("content")]
    public string Content { get; set; } = "";
}
