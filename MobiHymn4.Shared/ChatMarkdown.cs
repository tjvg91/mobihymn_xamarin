using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace MobiHymn4.Shared;

/// <summary>
/// Lightweight markdown for agent chat bubbles: paragraphs, - bullets, **bold**.
/// Mirrors MAUI ChatMarkdown (plain + HTML for web).
/// </summary>
public static class ChatMarkdown
{
    static readonly Regex BoldToken = new(@"\*\*(.+?)\*\*", RegexOptions.Singleline | RegexOptions.Compiled);

    public static string ToPlain(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var text = Normalize(markdown);
        text = BoldToken.Replace(text, "$1");
        text = Regex.Replace(text, @"(?m)^-\s+", "• ");
        return text;
    }

    /// <summary>Safe HTML for Blazor MarkupString (bold + bullet lists).</summary>
    public static string ToHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var text = Normalize(markdown);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        var inList = false;

        void CloseList()
        {
            if (!inList) return;
            sb.Append("</ul>");
            inList = false;
        }

        foreach (var raw in lines)
        {
            var line = raw;
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                if (!inList)
                {
                    sb.Append("<ul class=\"selah-md-list\">");
                    inList = true;
                }

                sb.Append("<li>");
                AppendInlineHtml(sb, line[2..]);
                sb.Append("</li>");
                continue;
            }

            CloseList();

            if (string.IsNullOrWhiteSpace(line))
            {
                sb.Append("<div class=\"selah-md-gap\"></div>");
                continue;
            }

            sb.Append("<p class=\"selah-md-p\">");
            AppendInlineHtml(sb, line);
            sb.Append("</p>");
        }

        CloseList();
        return sb.ToString();
    }

    static void AppendInlineHtml(StringBuilder sb, string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        var index = 0;
        foreach (Match match in BoldToken.Matches(line))
        {
            if (match.Index > index)
                sb.Append(WebUtility.HtmlEncode(line[index..match.Index]));

            sb.Append("<strong>");
            sb.Append(WebUtility.HtmlEncode(match.Groups[1].Value));
            sb.Append("</strong>");
            index = match.Index + match.Length;
        }

        if (index < line.Length)
            sb.Append(WebUtility.HtmlEncode(line[index..]));
        else if (index == 0)
            sb.Append(WebUtility.HtmlEncode(line));
    }

    static string Normalize(string markdown)
    {
        var text = markdown.Replace("\\n", "\n").Replace("\\\"", "\"");
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
