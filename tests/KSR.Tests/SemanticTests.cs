using KSR.AST;
using KSR.Diagnostics;
using KSR.Semantic;
using Xunit;

namespace KSR.Tests;

public class SemanticTests
{
    [Theory]
    [InlineData("", "List<Int>")]
    [InlineData("use ksr.collections", "List<Int>")]
    [InlineData("use System", "List<Int>")]
    [InlineData("", "Int[]")]
    [InlineData("use System", "Int[]")]
    [InlineData("", "Box<Int>")]
    [InlineData("use System", "Box<Int>")]
    [InlineData("use System", "Unresolved")]
    [InlineData("", "Unresolved<Int>")]
    public void KnownOrUnresolvedTypesDoNotBecomeDynamicForMissingMembers(string import, string type)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(import
            + "\ninterface Box<T> { fun get(): T }\nfun f(value: " + type + ") {\n    value.missing()\n}", "boundary.ksr");
        Assert.NotNull(result.Program);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal($"Unknown member 'missing' on type '{type}'", diagnostic.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("boundary.ksr", diagnostic.SourceFile);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(11, diagnostic.Column);
    }

    [Theory]
    [InlineData("fun f(value: Any) { val n: Int = value.missing().another }")]
    [InlineData("fun f(value: Any?) { val n: Int? = value?.missing() }")]
    [InlineData("use ksr.text\nfun f() { val n: Int = Text.split(\"a,b\", \",\").externalMember() }")]
    [InlineData("fun f() { val external = new ExternalApi()\nexternal.deferred().another }")]
    [InlineData("fun f() { val callback = { value -> value.deferred() } }")]
    [InlineData("fun f() { val value: Any = [1, 2]\nvalue.missing() }")]
    [InlineData("struct Box(value: Any)\nfun f(box: Box) { box.value.missing() }")]
    public void ExplicitAndDemonstratedDynamicFlowsRemainDynamic(string source)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(source, "dynamic.ksr");
        Assert.NotNull(result.Program);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("use ksr.collections\nfun f(xs: List<Int>) {\n    xs.filter { true }.missing()\n}", "List<Int>", 3, 24)]
    [InlineData("fun f(xs: Int[]) {\n    xs.length.missing()\n}", "Int", 2, 15)]
    [InlineData("fun f(xs: String) {\n    xs.trim().missing()\n}", "String", 2, 15)]
    [InlineData("interface Box<T> { fun get(): T }\nfun f(xs: Box<Int>) {\n    xs.get().missing()\n}", "Int", 3, 14)]
    public void KnownMemberResultsDoNotBecomeDynamic(string source, string type, int line, int column)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(source, "results.ksr");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal($"Unknown member 'missing' on type '{type}'", diagnostic.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("results.ksr", diagnostic.SourceFile);
        Assert.Equal(line, diagnostic.Line);
        Assert.Equal(column, diagnostic.Column);
    }

    [Theory]
    [InlineData("List<Int>", "xs.count")]
    [InlineData("Int[]", "xs.length")]
    [InlineData("List<Int>", "xs.first()")]
    public void KnownMemberTypesStillRejectIncompatibleAssignments(string type, string expression)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze("use ksr.collections\nfun f(xs: " + type
            + ") {\n    val text: String = " + expression + "\n}", "assignment.ksr");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("Type mismatch: cannot assign 'Int' to 'String'", diagnostic.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("assignment.ksr", diagnostic.SourceFile);
        Assert.Equal(3, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
    }

    [Theory]
    [InlineData("fun f(xs: Int[]) { val n: Int = xs.length }")]
    [InlineData("fun f(xs: List<Int>) { val n: Int = xs.count }")]
    [InlineData("fun f(xs: List<Int>) { val text: String = xs.toString() }")]
    [InlineData("fun f(xs: Map<String, Int>) { val n: Int = xs.getHashCode() }")]
    [InlineData("use ksr.collections\nfun f(xs: List<Int>) { val n: Int = xs.first()\nval rest: List<Int> = xs.filter { true } }")]
    public void SupportedKnownMembersRetainConcreteTypes(string source)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(source, "known.ksr");
        Assert.NotNull(result.Program);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData("add(\"nope\", 2)", "argument", 9)]
    [InlineData("add(z = 1, b = 2)", "Unknown named argument", 9)]
    [InlineData("add(a = 1, a = 2)", "Duplicate argument", 16)]
    [InlineData("add(1, a = 2)", "Duplicate argument", 12)]
    [InlineData("add(a = 1, 2)", "Positional argument", 16)]
    [InlineData("add(b = \"nope\", a = 1)", "argument", 9)]
    public void InvalidArgumentsProduceLocatedDiagnostics(string call, string message, int column)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(
            "fun add(a: Int, b: Int): Int { return a + b }\nfun main() {\n    " + call + "\n}", "arguments.ksr");
        Assert.NotNull(result.Program);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains(message, StringComparison.OrdinalIgnoreCase)
            && d.SourceFile == "arguments.ksr" && d.Line == 3 && d.Column == column
            && d.Severity == DiagnosticSeverity.Error);
    }

    [Theory]
    [InlineData("fun f(): Int {}")]
    [InlineData("fun f(x: Bool): Int { if (x) { return 1 } }")]
    [InlineData("fun f(x: Bool): Int { while (x) { return 1 } }")]
    [InlineData("fun f(): Int { val callback = { -> return 1 } }")]
    [InlineData("fun Int.f(): Int {}")]
    public void MissingReturnPathReportsFunctionDeclaration(string declaration)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze("\n    " + declaration, "returns.ksr");
        Assert.NotNull(result.Program);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("Not all paths return a value of type 'Int'", diagnostic.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("returns.ksr", diagnostic.SourceFile);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
    }

    [Theory]
    [InlineData("User", "u.missing", 7)]
    [InlineData("User?", "u?.missing", 8)]
    [InlineData("User", "u.missing()", 7)]
    public void UnknownMemberProducesLocatedDiagnostic(string type, string access, int column)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(
            "struct User(name: String)\nfun f(u: " + type + ") {\n    " + access + "\n}", "members.ksr");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("Unknown member 'missing'", diagnostic.Message);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("members.ksr", diagnostic.SourceFile);
        Assert.Equal(3, diagnostic.Line);
        Assert.Equal(column, diagnostic.Column);
    }

    [Theory]
    [InlineData("fun add(a: Int, b: Int = 2): Int { return a + b }\nfun f() { add(b = 2, a = 1)\nadd(1) }")]
    [InlineData("fun f(a: Bool, b: Bool): Int { if (a) { if (b) { return 1 } else { return 2 } } else { return 3 } }")]
    [InlineData("fun f(a: Bool): Int { if (a) { return 1 }\nreturn 2 }")]
    [InlineData("fun f(x: Any) { x.arbitrary().field }")]
    [InlineData("struct User(name: String)\nfun User.greet(n: Int): String { return this.name }\nfun f(u: User) { println(u.greet(1)) }")]
    [InlineData("interface Named { fun name(): String }\nstruct User(value: String)\nimplement Named for User { fun name(): String { return this.value } }\nfun f(u: User, n: Named) { println(u.name())\nprintln(n.name()) }")]
    [InlineData("fun <T> identity(x: T): T { return x }\nfun f() { val n: Int = identity(1) }")]
    public void ValidCallsMembersAndReturnPathsRemainAccepted(string source)
    {
        Assert.Empty(Analyze(source));
    }

    [Theory]
    [InlineData("struct Box(value: Int)\nfun f() { Box(\"bad\") }", "argument")]
    [InlineData("struct Box(value: Int)\nfun f() { new Box(\"bad\") }", "argument")]
    [InlineData("struct Box(value: Int)\nfun Box.add(n: Int): Int { return this.value + n }\nfun f(b: Box) { b.add(\"bad\") }", "argument")]
    [InlineData("interface Adder { fun add(n: Int): Int }\nfun f(a: Adder) { a.add(\"bad\") }", "argument")]
    [InlineData("fun f(s: String) { s.missing }", "Unknown member")]
    [InlineData("fun f(n: Int) { n.missing() }", "Unknown member")]
    [InlineData("fun f(x: Int?) {}\nfun main() { f(\"bad\") }", "argument")]
    [InlineData("fun f(x: Int) {}\nfun main() { f(null) }", "argument")]
    [InlineData("fun f(x: List<Int>) {}\nfun main() { f([\"bad\"]) }", "argument")]
    public void KnownSignaturesAndMembersRejectInvalidValues(string source, string message)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(source, "types.ksr");
        Assert.NotNull(result.Program);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("[1]", "Int")]
    [InlineData("1 + 2", "String")]
    [InlineData("-1", "String")]
    [InlineData("new Box(1)", "Int")]
    public void CompoundArgumentErrorsRetainExpressionLocation(string argument, string type)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(
            "struct Box(n: Int)\nfun take(x: " + type + ") {}\nfun main() {\n    take(" + argument + ")\n}", "compound.ksr");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("argument", diagnostic.Message);
        Assert.Equal("compound.ksr", diagnostic.SourceFile);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(4, diagnostic.Line);
        Assert.Equal(10, diagnostic.Column);
    }

    [Theory]
    [InlineData("fun <T> identity(x: T): T { return x }\nfun f() { val xs: List<List<Int>> = identity([[1]]) }")]
    [InlineData("fun <T> identity(x: List<T>): List<T> { return x }\nfun f() { val xs: List<List<Int>> = identity([[1]]) }")]
    [InlineData("interface Box<T> { fun get(): T }\nfun f(b: Box<Int>) { val x: Int = b.get() }")]
    [InlineData("struct Box(value: Int)\nfun f(b: Box) { val text: String = b.toString() }")]
    public void KnownGenericAndInheritedMembersRemainUsable(string source)
    {
        Assert.Empty(Analyze(source));
    }

    [Fact]
    public void GenericInterfaceDoesNotMakeMissingMembersDynamic()
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(
            "interface Box<T> { fun get(): T }\nfun f(b: Box<Int>) { b.missing() }", "generic.ksr");
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Unknown member 'missing'"));
    }

    [Theory]
    [InlineData("interface Box<T> { fun put(value: T) }\nfun f(b: Box<Int>) { b.put(\"bad\") }")]
    [InlineData("interface Adder { fun add(n: Int): Int }\nstruct Box(value: Int)\nimplement Adder for Box { fun add(n: Int): Int { return this.value + n } }\nfun f(b: Box) { b.add(n = \"bad\") }")]
    public void MemberCallsUseResolvedParameterTypes(string source)
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze(source, "members.ksr");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("argument", diagnostic.Message);
    }

    [Fact]
    public void MethodResultRetainsItsTypeForFollowingArgumentCheck()
    {
        var result = KSR.Analysis.KsrAnalyzer.Analyze("""
            struct Box(value: Int)
            fun Box.text(): String { return "text" }
            fun take(n: Int) {}
            fun f(b: Box) { take(b.text()) }
            """, "members.ksr");
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Contains("argument", diagnostic.Message);
        Assert.Contains("String", diagnostic.Message);
    }

    private static List<string> Analyze(string src)
    {
        var tokens = new Lexer.Lexer(src).Tokenize();
        var program = new Parser.Parser(tokens).Parse();
        var analyzer = new SemanticAnalyzer();
        analyzer.Analyze(program);
        return analyzer.Errors.ToList();
    }

    [Fact]
    public void DiagnosticsExposeSemanticErrorWithoutParsingFormattedText()
    {
        var program = KsrHelper.Parse("fun f() {\n    val x: Int = \"str\"\n}", "semantic.ksr");
        var analyzer = new SemanticAnalyzer();

        analyzer.Analyze(program, "semantic.ksr");

        var diagnostic = Assert.Single(analyzer.Diagnostics);
        Assert.Equal("Type mismatch: cannot assign 'String' to 'Int'", diagnostic.Message);
        Assert.Equal("semantic.ksr", diagnostic.SourceFile);
        Assert.Equal(2, diagnostic.Line);
        Assert.Equal(5, diagnostic.Column);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
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
