using MRubyCS.Compiler;
using PrettyPrompt;
using PrettyPrompt.Highlighting;

namespace MRubyCS.Compiler.Cli.Repl;

class ReplSession
{
    readonly MRubyState mrb;
    readonly MRubyCompiler compiler;

    ReplSession(MRubyState mrb, MRubyCompiler compiler)
    {
        this.mrb = mrb;
        this.compiler = compiler;
    }

    public static ReplSession Create()
    {
        var mrb = MRubyState.Create();
        var compiler = MRubyCompiler.Create(mrb);
        return new ReplSession(mrb, compiler);
    }

    public async Task RunAsync()
    {
        var lineNo = 1;
        var callbacks = new RubyPromptCallbacks(mrb);

        await using var prompt = new Prompt(
            callbacks: callbacks,
            configuration: new PromptConfiguration(
                prompt: new FormattedString($"irb(main):{lineNo:D3}> ")));

        while (true)
        {
            var result = await prompt.ReadLineAsync();

            if (!result.IsSuccess)
                continue;

            var input = result.Text.Trim();
            if (input is "exit" or "quit")
                break;

            if (string.IsNullOrWhiteSpace(input))
            {
                lineNo++;
                continue;
            }

            try
            {
                var irep = compiler.Compile(input);
                var value = mrb.Execute(irep);
                var inspected = mrb.Inspect(value);
                Console.WriteLine($"=> {inspected}");
            }
            catch (MRubyCompileException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"SyntaxError: {ex.Message}");
                Console.ResetColor();
            }
            catch (MRubyRaiseException ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"{ex.Message}");
                Console.ResetColor();
            }

            lineNo++;
        }
    }

}
