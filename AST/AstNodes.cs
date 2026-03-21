namespace KSR.AST;

// ═══════════════════════════════════════════════════════════════════════════════
//  Base types
// ═══════════════════════════════════════════════════════════════════════════════

public abstract record AstNode;
public abstract record Stmt : AstNode;
public abstract record Expr  : AstNode;

// ═══════════════════════════════════════════════════════════════════════════════
//  Helpers (not nodes — just data bags used inside other nodes)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>A KSR type reference, e.g. "Int", "String?", "User?"</summary>
public record TypeRef(string Name, bool Nullable);

/// <summary>A named + typed parameter used in fun / data-class headers.</summary>
public record Parameter(string Name, TypeRef Type);

// ═══════════════════════════════════════════════════════════════════════════════
//  Top-level declarations
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>Root of a KSR compilation unit.</summary>
public record ProgramNode(List<AstNode> Declarations) : AstNode;

/// <summary>use Raylib_cs  →  using Raylib_cs;</summary>
public record UseDecl(string Namespace) : AstNode;

/// <summary>data class Foo(x: Int, y: String)</summary>
public record DataClassDecl(string Name, List<Parameter> Properties) : AstNode;

/// <summary>fun foo(a: Int): String { … }</summary>
public record FunctionDecl(
    string          Name,
    List<Parameter> Parameters,
    TypeRef?        ReturnType,
    Block           Body) : AstNode;

/// <summary>
/// Extension function: fun Type.method(params): RetType { … }
/// Compiles to a C# static extension method inside KsrProgram.
/// Inside the body 'this' refers to the receiver (compiled as 'self').
/// </summary>
public record ExtFunctionDecl(
    string          ReceiverType,
    string          MethodName,
    List<Parameter> Parameters,
    TypeRef?        ReturnType,
    Block           Body) : AstNode;

// ═══════════════════════════════════════════════════════════════════════════════
//  Statements
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>{ stmt* }</summary>
public record Block(List<Stmt> Statements) : AstNode;

/// <summary>val x: Type = expr  (immutable, type optional)</summary>
public record ValDecl(string Name, TypeRef? Type, Expr Value) : Stmt;

/// <summary>var x: Type = expr  (mutable, type optional)</summary>
public record VarDecl(string Name, TypeRef? Type, Expr Value) : Stmt;

/// <summary>x = expr  (reassignment — only valid for var bindings)</summary>
public record AssignStmt(string Name, Expr Value) : Stmt;

/// <summary>x += expr  |  x -= expr</summary>
public record CompoundAssignStmt(string Name, string Op, Expr Value) : Stmt;

/// <summary>name[index] = expr  (indexed assignment)</summary>
public record IndexAssignStmt(string Name, Expr Index, Expr Value) : Stmt;

/// <summary>return expr?</summary>
public record ReturnStmt(Expr? Value) : Stmt;

/// <summary>if (cond) { … } else { … }</summary>
public record IfStmt(Expr Condition, Block Then, Block? Else) : Stmt;

/// <summary>while (cond) { … }</summary>
public record WhileStmt(Expr Condition, Block Body) : Stmt;

/// <summary>for (x in iterable) { … }  — iterable may be a RangeExpr or a collection</summary>
public record ForInStmt(string VarName, Expr Iterable, Block Body) : Stmt;

/// <summary>Any expression used as a statement.</summary>
public record ExprStmt(Expr Expression) : Stmt;

// ═══════════════════════════════════════════════════════════════════════════════
//  Expressions
// ═══════════════════════════════════════════════════════════════════════════════

public record IntLiteral(int Value)       : Expr;
public record StringLiteral(string Value) : Expr;
public record BoolLiteral(bool Value)     : Expr;
public record NullLiteral()               : Expr;

/// <summary>
/// A string template: "Hello, ${name}! You are ${age} years old."
/// Holds an ordered list of literal text chunks and embedded expressions.
/// </summary>
public record StringTemplateExpr(List<StringPart> Parts) : Expr;

public abstract record StringPart;
public record LiteralPart(string Text)  : StringPart;
public record ExprPart(Expr Expression) : StringPart;

/// <summary>The receiver inside an extension function body.</summary>
public record ThisExpr() : Expr;

/// <summary>A bare name: foo, User, counter …</summary>
public record IdentifierExpr(string Name) : Expr;

/// <summary>callee(arg1, arg2, …)</summary>
public record CallExpr(Expr Callee, List<Expr> Arguments) : Expr;

/// <summary>target.member</summary>
public record MemberAccessExpr(Expr Target, string Member) : Expr;

/// <summary>target?.member</summary>
public record SafeCallExpr(Expr Target, string Member) : Expr;

/// <summary>left ?: right</summary>
public record ElvisExpr(Expr Left, Expr Right) : Expr;

/// <summary>Binary operation: +  -  *  /  %  ==  !=  &lt;  &gt;  &lt;=  &gt;=  &amp;&amp;  ||</summary>
public record BinaryExpr(Expr Left, string Op, Expr Right) : Expr;

/// <summary>target[index]</summary>
public record IndexExpr(Expr Target, Expr Index) : Expr;

/// <summary>new Bool[size]  →  new bool[size]</summary>
public record NewArrayExpr(TypeRef ElementType, Expr Size) : Expr;

/// <summary>
/// Lambda literal.
/// <list type="bullet">
///   <item>{ it }        → (it =&gt; it)           — implicit "it" parameter</item>
///   <item>{ x -&gt; expr } → (x =&gt; expr)          — single named parameter</item>
///   <item>{ x, y -&gt; e } → ((x, y) =&gt; e)        — multiple named parameters</item>
///   <item>{ -&gt; expr }   → (() =&gt; expr)          — zero parameters</item>
/// </list>
/// </summary>
public record LambdaExpr(List<string> Params, Expr Body) : Expr;

/// <summary>new Random()  →  new Random()   (for non-data-class types)</summary>
public record NewObjectExpr(string TypeName, List<Expr> Arguments) : Expr;

/// <summary>Unary operation: !  -</summary>
public record UnaryExpr(string Op, Expr Operand) : Expr;

/// <summary>
/// start..end  (inclusive)  or  start..&lt;end  (exclusive).
/// Used as the iterable of a ForInStmt; also valid as a standalone value.
/// </summary>
public record RangeExpr(Expr Start, Expr End, bool Inclusive) : Expr;
