using System.Text.RegularExpressions;
using PrettyPrompt.Highlighting;

namespace MRubyCS.Compiler.Cli.Repl;

static class RubySyntaxHighlighter
{
    static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "def", "end", "class", "module", "if", "elsif", "else", "unless",
        "while", "until", "for", "do", "begin", "rescue", "ensure", "raise",
        "return", "yield", "break", "next", "redo", "retry",
        "then", "when", "case", "in",
        "and", "or", "not",
        "nil", "true", "false", "self",
        "require", "include", "extend", "attr_reader", "attr_writer", "attr_accessor",
        "puts", "print", "p",
        "lambda", "proc", "block_given?",
        "super", "__method__", "__FILE__", "__LINE__",
    };

    static readonly Regex TokenPattern = new(
        @"(?<comment>\#.*$)" +
        @"|(?<string>""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')" +
        @"|(?<symbol>:\w+)" +
        @"|(?<number>\b(?:0[xX][0-9a-fA-F_]+|0[bB][01_]+|0[oO]?[0-7_]+|\d[\d_]*(?:\.\d[\d_]*)?(?:[eE][+-]?\d+)?)\b)" +
        @"|(?<instancevar>@@?\w+)" +
        @"|(?<globalvar>\$\w+)" +
        @"|(?<constant>\b[A-Z]\w*\b)" +
        @"|(?<ident>\b\w+[?!]?\b)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static IReadOnlyCollection<FormatSpan> Highlight(string text)
    {
        var spans = new List<FormatSpan>();

        foreach (Match match in TokenPattern.Matches(text))
        {
            AnsiColor? color = null;

            if (match.Groups["comment"].Success)
            {
                color = AnsiColor.BrightBlack;
            }
            else if (match.Groups["string"].Success)
            {
                color = AnsiColor.Green;
            }
            else if (match.Groups["symbol"].Success)
            {
                color = AnsiColor.Yellow;
            }
            else if (match.Groups["number"].Success)
            {
                color = AnsiColor.Cyan;
            }
            else if (match.Groups["instancevar"].Success)
            {
                color = AnsiColor.Red;
            }
            else if (match.Groups["globalvar"].Success)
            {
                color = AnsiColor.Red;
            }
            else if (match.Groups["constant"].Success)
            {
                color = AnsiColor.Blue;
            }
            else if (match.Groups["ident"].Success)
            {
                if (Keywords.Contains(match.Value))
                {
                    color = AnsiColor.Magenta;
                }
            }

            if (color is not null)
            {
                spans.Add(new FormatSpan(match.Index, match.Length, color.Value));
            }
        }

        return spans;
    }
}
