using System.Text;
using lattice;

namespace Lattice.Tests;

/// <summary>
/// End-to-end integration tests — runs TextIR programs through the AST
/// pipeline (parse → lower → execute) and asserts on observed output.
/// </summary>
public class AstIntegrationTests : IDisposable
{
    private readonly StringWriter _output;
    private readonly TextWriter _original;

    public AstIntegrationTests()
    {
        _original = Console.Out;
        _output = new StringWriter(new StringBuilder());
        Console.SetOut(_output);
    }

    public void Dispose()
    {
        Console.SetOut(_original);
        _output.Dispose();
    }

    // ── helpers ──────────────────────────────────────────────────────────

    private string Run(string textIr)
    {
        _output.GetStringBuilder().Clear();
        var rt = new IRRuntime(textIr, IRRuntime.InputFormat.Ast);
        rt.Run();
        return _output.ToString();
    }

    private string[] Lines(string textIr) =>
        Run(textIr)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .ToArray();

    // ── Hello World ───────────────────────────────────────────────────────

    [Fact]
    public void HelloWorld_PrintsToConsole()
    {
        var lines = Lines(
            "module HelloWorld\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldstr \"Hello, World!\"\n" +
            "    call System.Console.WriteLine(string) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("Hello, World!", lines);
    }

    // ── Field access ──────────────────────────────────────────────────────

    [Fact]
    public void FieldAccess_ConstructorSetsField_MethodReadsIt()
    {
        var lines = Lines(
            "module FieldTest\n" +
            "class Counter {\n" +
            "  private field count: int32\n" +
            "  constructor(n: int32) {\n" +
            "    ldarg this\n" +
            "    ldarg n\n" +
            "    stfld Counter.count\n" +
            "    ret\n" +
            "  }\n" +
            "  method GetCount() -> int32 {\n" +
            "    ldarg this\n" +
            "    ldfld Counter.count\n" +
            "    ret\n" +
            "  }\n" +
            "}\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 7\n" +
            "    newobj Counter\n" +
            "    callvirt Counter.GetCount() -> int32\n" +
            "    call System.Console.WriteLine(int32) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("7", lines);
    }

    // ── Static fields ─────────────────────────────────────────────────────

    [Fact]
    public void StaticField_StoreAndLoad_Works()
    {
        var lines = Lines(
            "module StaticTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 99\n" +
            "    stsfld Program.answer\n" +
            "    ldsfld Program.answer\n" +
            "    call System.Console.WriteLine(int32) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("99", lines);
    }

    // ── If / else ────────────────────────────────────────────────────────

    [Fact]
    public void If_TrueBranch_ExecutesWhenConditionMet()
    {
        var lines = Lines(
            "module IfTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 1\n" +
            "    if (stack) {\n" +
            "      ldstr \"yes\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("yes", lines);
    }

    [Fact]
    public void If_FalseBranch_SkippedWhenConditionNotMet()
    {
        var lines = Lines(
            "module IfTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 0\n" +
            "    if (stack) {\n" +
            "      ldstr \"yes\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.DoesNotContain("yes", lines);
    }

    [Fact]
    public void IfElse_ElseBranch_ExecutesWhenConditionFalse()
    {
        var lines = Lines(
            "module IfTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 0\n" +
            "    if (stack) {\n" +
            "      ldstr \"then\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    } else {\n" +
            "      ldstr \"else\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.DoesNotContain("then", lines);
        Assert.Contains("else", lines);
    }

    // ── Binary conditions ────────────────────────────────────────────────

    [Fact]
    public void If_BinaryCondition_Equality_Works()
    {
        var lines = Lines(
            "module IfTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    local x: int32\n" +
            "    ldc 5\n" +
            "    stloc x\n" +
            "    if (x == 5) {\n" +
            "      ldstr \"equal\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    } else {\n" +
            "      ldstr \"not equal\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("equal", lines);
        Assert.DoesNotContain("not equal", lines);
    }

    [Fact]
    public void If_BinaryCondition_LessThan_Works()
    {
        var lines = Lines(
            "module IfTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    local n: int32\n" +
            "    ldc 3\n" +
            "    stloc n\n" +
            "    if (n < 10) {\n" +
            "      ldstr \"small\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("small", lines);
    }

    // ── While loop ───────────────────────────────────────────────────────

    [Fact]
    public void While_Counts_CorrectIterations()
    {
        var lines = Lines(
            "module WhileTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    local i: int32\n" +
            "    ldc 0\n" +
            "    stloc i\n" +
            "    while (i < 3) {\n" +
            "      ldloc i\n" +
            "      call System.Console.WriteLine(int32) -> void\n" +
            "      ldloc i\n" +
            "      ldc 1\n" +
            "      add\n" +
            "      stloc i\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        // Should print 0, 1, 2 — filter out the IR call log lines
        var nums = lines.Where(l => l is "0" or "1" or "2" or "3").ToArray();
        Assert.Contains("0", nums);
        Assert.Contains("1", nums);
        Assert.Contains("2", nums);
        Assert.DoesNotContain("3", nums);
    }

    [Fact]
    public void While_NeverEnters_WhenConditionFalseFromStart()
    {
        _output.GetStringBuilder().Clear();
        var rt = new IRRuntime(
            "module WhileTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    local i: int32\n" +
            "    ldc 10\n" +
            "    stloc i\n" +
            "    while (i < 5) {\n" +
            "      ldstr \"inside\"\n" +
            "      call System.Console.WriteLine(string) -> void\n" +
            "    }\n" +
            "    ret\n" +
            "  }\n" +
            "}\n",
            IRRuntime.InputFormat.Ast);
        rt.Run();
        var output = _output.ToString();
        Assert.DoesNotContain("inside", output);
    }

    // ── Method calls ─────────────────────────────────────────────────────

    [Fact]
    public void Method_CanCallOtherMethod_InSameClass()
    {
        var lines = Lines(
            "module CallTest\n" +
            "class Program {\n" +
            "  static method Greet() -> void {\n" +
            "    ldstr \"greetings\"\n" +
            "    call System.Console.WriteLine(string) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "  static method Main() -> void {\n" +
            "    call Program.Greet() -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("greetings", lines);
    }

    [Fact]
    public void Method_ReturnValue_CanBeUsed()
    {
        var lines = Lines(
            "module RetTest\n" +
            "class Program {\n" +
            "  static method MakeNumber() -> int32 {\n" +
            "    ldc 42\n" +
            "    ret\n" +
            "  }\n" +
            "  static method Main() -> void {\n" +
            "    call Program.MakeNumber() -> int32\n" +
            "    call System.Console.WriteLine(int32) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("42", lines);
    }

    // ── Arithmetic ────────────────────────────────────────────────────────

    [Fact]
    public void Arithmetic_Add_ProducesCorrectResult()
    {
        var lines = Lines(
            "module ArithTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 10\n" +
            "    ldc 32\n" +
            "    add\n" +
            "    call System.Console.WriteLine(int32) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("42", lines);
    }

    [Fact]
    public void Arithmetic_Sub_ProducesCorrectResult()
    {
        var lines = Lines(
            "module ArithTest\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldc 100\n" +
            "    ldc 58\n" +
            "    sub\n" +
            "    call System.Console.WriteLine(int32) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("42", lines);
    }

    // ── Interface / polymorphism ──────────────────────────────────────────

    [Fact]
    public void Interface_Dispatch_ViaCallvirt()
    {
        var lines = Lines(
            "module IfaceTest\n" +
            "interface IGreeter {\n" +
            "  method Greet() -> void\n" +
            "}\n" +
            "class HelloGreeter : IGreeter {\n" +
            "  method Greet() -> void implements IGreeter.Greet {\n" +
            "    ldstr \"hello from interface\"\n" +
            "    call System.Console.WriteLine(string) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    newobj HelloGreeter\n" +
            "    callvirt HelloGreeter.Greet() -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        Assert.Contains("hello from interface", lines);
    }

    // ── LoadModule(ModuleNode) overload ──────────────────────────────────

    [Fact]
    public void LoadModule_WithAstNode_Works()
    {
        _output.GetStringBuilder().Clear();

        var ast = ObjectIR.AST.TextIrParser.ParseModule(
            "module Direct\n" +
            "class Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldstr \"from ast node\"\n" +
            "    call System.Console.WriteLine(string) -> void\n" +
            "    ret\n" +
            "  }\n" +
            "}\n");

        var rt = new IRRuntime();
        rt.LoadModule(ast);
        rt.Run();

        Assert.Contains("from ast node", _output.ToString());
    }

    // ── CallMethod API ────────────────────────────────────────────────────

    [Fact]
    public void CallMethod_ReturnsCorrectValue()
    {
        _output.GetStringBuilder().Clear();

        var rt = new IRRuntime(
            "module Api\n" +
            "class Math {\n" +
            "  static method Square(n: int32) -> int32 {\n" +
            "    ldarg n\n" +
            "    ldarg n\n" +
            "    mul\n" +
            "    ret\n" +
            "  }\n" +
            "}\n",
            IRRuntime.InputFormat.Ast);

        var result = rt.CallMethod<long>("Math.Square", 6L);
        Assert.Equal(36L, result);
    }
}
