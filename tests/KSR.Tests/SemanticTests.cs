using KSR.AST;
using KSR.Semantic;
using Xunit;

namespace KSR.Tests;

public class SemanticTests
{
    private static List<string> Analyze(string src)
    {
        var tokens = new Lexer.Lexer(src).Tokenize();
        var program = new Parser.Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);
        return analyzer.Errors.ToList();
    }

    [Fact]
    public void Redeclaration_Variable_Errors()
    {
        var errors = Analyze("fun f() { val x = 1\n val x = 2 }");
        Assert.Single(errors);
        Assert.Contains("Variable 'x' is already defined", errors[0]);
    }

    [Fact]
    public void UndefinedIdentifier_Errors()
    {
        var errors = Analyze("fun f() { val x = y }");
        Assert.Single(errors);
        Assert.Contains("Undefined identifier 'y'", errors[0]);
    }

    [Fact]
    public void ReassignImmutable_Errors()
    {
        var errors = Analyze("fun f() { val x = 1\n x = 2 }");
        Assert.Single(errors);
        Assert.Contains("Cannot reassign to immutable variable 'x'", errors[0]);
    }

    [Fact]
    public void CompoundAssignImmutable_Errors()
    {
        var errors = Analyze("fun f() { val x = 1\n x += 2 }");
        Assert.Single(errors);
        Assert.Contains("Cannot use compound assignment on immutable variable 'x'", errors[0]);
    }

    [Fact]
    public void FunctionRedeclaration_Errors()
    {
        var errors = Analyze("fun f() {}\n fun f() {}");
        Assert.Single(errors);
        Assert.Contains("Redeclaration of function 'f'", errors[0]);
    }

    [Fact]
    public void Scopes_NestedBlocks_Work()
    {
        var errors = Analyze("fun f() { val x = 1\n { val x = 2 } }"); 
        Assert.Empty(errors);
    }

    [Fact]
    public void TypeMismatch_Assignment_Errors()
    {
        var errors = Analyze("fun f() { val x: Int = \"str\" }");
        Assert.Single(errors);
        Assert.Contains("Type mismatch: cannot assign 'String' to 'Int'", errors[0]);
    }

    [Fact]
    public void IfCondition_MustBeBool_Errors()
    {
        var errors = Analyze("fun f() { if (1) { } }");
        Assert.Single(errors);
        Assert.Contains("Condition must be Bool, but found 'Int'", errors[0]);
    }

    [Fact]
    public void WhileCondition_MustBeBool_Errors()
    {
        var errors = Analyze("fun f() { while (\"str\") { } }");
        Assert.Single(errors);
        Assert.Contains("Condition must be Bool, but found 'String'", errors[0]);
    }

    [Fact]
    public void Nullability_Violation_Errors()
    {
        var errors = Analyze("fun f() { val x: String = null }"); // null is Any?
        Assert.Single(errors);
        Assert.Contains("Type mismatch: cannot assign 'Any' to 'String'", errors[0]);
    }

    [Fact]
    public void Nullability_Correct_Works()
    {
        var errors = Analyze("fun f() { val x: String? = null }");
        Assert.Empty(errors);
    }

    [Fact]
    public void Shadowing_VariableInNestedBlock_Works()
    {
        var errors = Analyze("fun f() { val x = 1\n { val x = \"str\" } }");
        Assert.Empty(errors);
    }

    [Fact]
    public void Shadowing_LoopVar_Works()
    {
        var errors = Analyze("fun f() { val x = 1\n for (x in [1, 2, 3]) { println(x) } }");
        Assert.Empty(errors);
    }

    [Fact]
    public void Shadowing_Parameter_Errors()
    {
        // Many languages forbid declaring a local variable with the same name as a parameter in the same scope
        var errors = Analyze("fun f(x: Int) { val x = 2 }");
        Assert.Single(errors);
        Assert.Contains("Variable 'x' is already defined", errors[0]);
    }

    [Fact]
    public void NestedGenerics_Compatibility_Works()
    {
        // val list: List<List<Int>> = [[1]]
        var errors = Analyze("fun f() { val x: List<List<Int>> = [[1]] }");
        Assert.Empty(errors);
    }

    [Fact]
    public void NestedGenerics_Mismatch_Errors()
    {
        var errors = Analyze("fun f() { val x: List<List<Int>> = [1] }");
        Assert.Single(errors);
        Assert.Contains("Type mismatch: cannot assign 'List<Int>' to 'List<List<Int>>'", errors[0]);
    }

    [Fact]
    public void This_OutsideMethod_Errors()
    {
        var errors = Analyze("fun f() { val x = this }");
        Assert.Single(errors);
        Assert.Contains("'this' is only available", errors[0]);
    }

    [Fact]
    public void This_InExtension_Works()
    {
        var errors = Analyze("fun Int.double(): Int { return this * 2 }");
        Assert.Empty(errors);
    }

    [Fact]
    public void FunctionParameter_AsImmutable_Errors()
    {
        var errors = Analyze("fun f(x: Int) { x = 2 }");
        Assert.Single(errors);
        Assert.Contains("Cannot reassign to immutable variable 'x'", errors[0]);
    }

    [Fact]
    public void Recursion_SameName_Works()
    {
        var errors = Analyze("fun factorial(n: Int): Int { if (n == 0) { return 1 } return n * factorial(n - 1) }");
        Assert.Empty(errors);
    }

    [Fact]
    public void Closure_Capture_Works()
    {
        var errors = Analyze("fun f() { val x = 1\n val lambda = { y -> x + y } }");
        Assert.Empty(errors);
    }

    [Fact]
    public void Closure_Shadowing_Works()
    {
        var errors = Analyze("fun f() { val x = 1\n val lambda = { x -> x + 1 } }");
        Assert.Empty(errors);
    }

    [Fact]
    public void Use_UndefinedFunction_Errors()
    {
        var errors = Analyze("fun f() { g() }");
        Assert.Single(errors);
        Assert.Contains("Undefined identifier 'g'", errors[0]);
    }

    [Fact]
    public void Elvis_TypeInference_Works()
    {
        var errors = Analyze("fun f(s: String?): String { val res = s ?: \"default\"\n return res }");
        Assert.Empty(errors);
    }

    [Fact]
    public void SafeCall_Result_IsNullable()
    {
        var errors = Analyze("struct User(name: String)\n fun f(u: User?): String { return u?.name }");
        Assert.Single(errors);
        // Note: Our current IsCompatible is simple, u?.name is String? but return is String
        Assert.Contains("Type mismatch", errors[0]);
    }

    [Fact]
    public void Sealed_DuplicateVariant_Errors()
    {
        var errors = Analyze("sealed Shape { struct Circle(r: Double)\n struct Circle(r: Double) }");
        Assert.Single(errors);
        Assert.Contains("Redeclaration of struct 'Circle'", errors[0]);
    }

    [Fact]
    public void Struct_Redeclaration_AcrossGlobal_Errors()
    {
        var errors = Analyze("struct Foo(x: Int)\n struct Foo(y: Int)");
        Assert.Single(errors);
        Assert.Contains("Redeclaration of struct 'Foo'", errors[0]);
    }

    [Fact]
    public void Function_ArgumentCount_Mismatch_Errors()
    {
        // This requires enhancing SemanticAnalyzer to check argument counts
        var errors = Analyze("fun add(a: Int, b: Int): Int { return a + b }\n fun f() { add(1) }");
        // For now, our analyzer doesn't check this, but we should add the test to drive the feature.
        // I will update the analyzer after this test fails.
        Assert.Single(errors);
        Assert.Contains("Expected 2 arguments but found 1", errors[0]);
    }
}
