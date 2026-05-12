namespace KSR.CodeGen;

public static class NameUtils
{
    private static readonly HashSet<string> Keywords = new()
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while"
    };

    /// <summary>
    /// Escapes a name if it conflicts with a C# keyword by prefixing it with '@'.
    /// </summary>
    public static string Escape(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        return Keywords.Contains(name) ? "@" + name : name;
    }
}
