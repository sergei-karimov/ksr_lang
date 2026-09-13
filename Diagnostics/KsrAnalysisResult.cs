using KSR.AST;

namespace KSR.Diagnostics;

public sealed class KsrAnalysisResult
{
    public ProgramNode? Program { get; }
    public IReadOnlyList<KsrDiagnostic> Diagnostics { get; }
    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public KsrAnalysisResult(ProgramNode? Program, IEnumerable<KsrDiagnostic> Diagnostics)
    {
        this.Program = Program;
        this.Diagnostics = Array.AsReadOnly(Diagnostics.ToArray());
    }
}
