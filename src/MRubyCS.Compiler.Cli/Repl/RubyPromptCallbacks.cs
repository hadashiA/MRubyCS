using PrettyPrompt;
using PrettyPrompt.Consoles;
using PrettyPrompt.Highlighting;

namespace MRubyCS.Compiler.Cli.Repl;

class RubyPromptCallbacks(MRubyState state) : PromptCallbacks
{
    protected override Task<IReadOnlyCollection<FormatSpan>> HighlightCallbackAsync(
        string text, CancellationToken cancellationToken)
    {
        return Task.FromResult(RubySyntaxHighlighter.Highlight(text));
    }

    protected override Task<KeyPress> TransformKeyPressAsync(
        string text, int caret, KeyPress keyPress, CancellationToken cancellationToken)
    {
        if (keyPress.ConsoleKeyInfo.Key == ConsoleKey.Enter
            && keyPress.ConsoleKeyInfo.Modifiers == 0
            && !string.IsNullOrWhiteSpace(text)
            && IncompleteInputDetector.IsIncomplete(state, text))
        {
            return Task.FromResult(new KeyPress(
                new ConsoleKeyInfo('\0', ConsoleKey.Enter, shift: true, alt: false, control: false)));
        }

        return Task.FromResult(keyPress);
    }
}
