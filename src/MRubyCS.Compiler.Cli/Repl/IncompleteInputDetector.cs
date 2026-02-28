using MRubyCS.Compiler;

namespace MRubyCS.Compiler.Cli.Repl;

static class IncompleteInputDetector
{
    public static bool IsIncomplete(MRubyState state, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        // Check for unclosed string literals
        if (HasUnclosedString(text))
            return true;

        // Check for line continuation
        if (text.TrimEnd().EndsWith('\\'))
            return true;

        // Try compilation and check error message
        try
        {
            var compiler = MRubyCompiler.Create(state);
            compiler.Compile(text);
            return false;
        }
        catch (MRubyCompileException ex)
        {
            var msg = ex.Message;
            return msg.Contains("$end") || msg.Contains("unexpected end");
        }
        catch
        {
            return false;
        }
    }

    static bool HasUnclosedString(string text)
    {
        var inSingle = false;
        var inDouble = false;
        var escaped = false;

        foreach (var ch in text)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (ch == '\\' && (inSingle || inDouble))
            {
                escaped = true;
                continue;
            }

            if (ch == '"' && !inSingle)
            {
                inDouble = !inDouble;
            }
            else if (ch == '\'' && !inDouble)
            {
                inSingle = !inSingle;
            }
        }

        return inSingle || inDouble;
    }
}
