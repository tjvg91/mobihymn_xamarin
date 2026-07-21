using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Maui.Controls;

namespace MobiHymn4.Utils
{
    /// <summary>
    /// Lightweight markdown for agent chat bubbles: paragraphs, - bullets, **bold**.
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

        public static FormattedString ToFormatted(string markdown, Color textColor)
        {
            var formatted = new FormattedString();
            if (string.IsNullOrEmpty(markdown))
            {
                formatted.Spans.Add(MakeSpan(string.Empty, textColor, bold: false));
                return formatted;
            }

            var text = Normalize(markdown);
            var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("- "))
                    line = "• " + line[2..];

                AppendInline(formatted, line, textColor);
                if (i < lines.Length - 1)
                    formatted.Spans.Add(MakeSpan("\n", textColor, bold: false));
            }

            return formatted;
        }

        static void AppendInline(FormattedString formatted, string line, Color textColor)
        {
            if (string.IsNullOrEmpty(line))
            {
                formatted.Spans.Add(MakeSpan(string.Empty, textColor, bold: false));
                return;
            }

            var index = 0;
            foreach (Match match in BoldToken.Matches(line))
            {
                if (match.Index > index)
                    formatted.Spans.Add(MakeSpan(line[index..match.Index], textColor, bold: false));

                formatted.Spans.Add(MakeSpan(match.Groups[1].Value, textColor, bold: true));
                index = match.Index + match.Length;
            }

            if (index < line.Length)
                formatted.Spans.Add(MakeSpan(line[index..], textColor, bold: false));
            else if (index == 0)
                formatted.Spans.Add(MakeSpan(line, textColor, bold: false));
        }

        static Span MakeSpan(string text, Color color, bool bold) => new()
        {
            Text = text ?? string.Empty,
            TextColor = color,
            FontAttributes = bold ? FontAttributes.Bold : FontAttributes.None
        };

        static string Normalize(string markdown)
        {
            var text = markdown.Replace("\\n", "\n").Replace("\\\"", "\"");
            text = Regex.Replace(text, @"\n{3,}", "\n\n");
            return text.Trim();
        }
    }
}
