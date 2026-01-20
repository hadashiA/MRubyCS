namespace MRubyCS.Compiler;

public enum DiagnosticSeverity
{
    Warning,
    Error,
    GeneratorWarning,
    GeneratorError,
}

public class DiagnosticsDescriptor
{
    public DiagnosticSeverity Severity { get; init; }
    public int Line { get; init; }
    public int Column { get; init; }
    public string? Message { get; init; }

    public override string ToString() => $"{Severity}: {Message} ({Line}:{Column})";
}
