namespace KSR.Semantic;

public enum SymbolKind { Variable, Parameter, Function, Struct, Interface, Sealed }

public record Symbol(
    string Name,
    SymbolKind Kind,
    bool IsMutable,
    object? Metadata = null 
);

public class SymbolTable
{
    private readonly List<Dictionary<string, Symbol>> _scopes = new();

    public SymbolTable()
    {
        EnterScope(); // Global scope
    }

    public void EnterScope() => _scopes.Add(new Dictionary<string, Symbol>(StringComparer.Ordinal));

    public void ExitScope()
    {
        if (_scopes.Count > 1) _scopes.RemoveAt(_scopes.Count - 1);
    }

    public bool Declare(string name, SymbolKind kind, bool isMutable, object? metadata = null)
    {
        var current = _scopes[^1];
        if (current.ContainsKey(name)) return false;
        current[name] = new Symbol(name, kind, isMutable, metadata);
        return true;
    }

    public Symbol? Resolve(string name)
    {
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            if (_scopes[i].TryGetValue(name, out var sym)) return sym;
        }
        return null;
    }

    public bool IsInCurrentScope(string name) => _scopes[^1].ContainsKey(name);
}
