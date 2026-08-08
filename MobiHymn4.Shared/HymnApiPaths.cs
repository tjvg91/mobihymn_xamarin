namespace MobiHymn4.Shared;

public static class HymnApiPaths
{
    /// <summary>Upstream hymn server (Host proxies this; browser never calls it directly).</summary>
    public const string UpstreamBase = "http://157.230.9.81";

    public const string LyricsQuery = "/hymn/tim.dna?q=";
    public const string Stream = "/hymn/api/hymns.dna?stream=1";
    public const string Meta = "/hymn/api/hymns.dna?meta=1";
    public const string Changes = "/hymn/api/changes.py";
    public const string AgentSearch = "/hymn/api/agent.cgi/search";
    public const string AgentChat = "/hymn/api/agent.cgi/chat";
    public const string Audio = "/hymn/audio/gccsatx/";

    /// <summary>Same-origin paths used by the Blazor client (Host reverse-proxies).</summary>
    public const string ClientProxyPrefix = "/api/hymn";

    public static string ClientLyricsUrl(string number) =>
        $"{ClientProxyPrefix}/tim.dna?q={Uri.EscapeDataString(number)}";

    /// <summary>JSON hymn record with metadata (author, metre, tune, …).</summary>
    public static string ClientHymnJsonUrl(string number) =>
        $"{ClientProxyPrefix}/api/hymns.dna?q={Uri.EscapeDataString(number)}";

    public static string ClientMetaUrl => $"{ClientProxyPrefix}/api/hymns.dna?meta=1";

    public static string ClientChangesUrl => $"{ClientProxyPrefix}/api/changes.py";

    public static string ClientHymnsPageUrl(int offset, int limit) =>
        $"{ClientProxyPrefix}/api/hymns.dna?offset={offset}&limit={limit}";

    public static string ClientAgentSearchUrl => $"{ClientProxyPrefix}/api/agent.cgi/search";
    public static string ClientAgentChatUrl => $"{ClientProxyPrefix}/api/agent.cgi/chat";
    public static string ClientAudioUrl(string number) =>
        $"{ClientProxyPrefix}/audio/gccsatx/{Uri.EscapeDataString(number)}.mp3";
}
