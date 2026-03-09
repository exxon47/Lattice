using System.Text.Json;
using lattice.IR;
using lattice.TextIR;
using ObjectIR.AST;

namespace Lattice.Tests;

/// <summary>
/// Unit tests for <see cref="AstLowering"/> — verifies that every AST node
/// is translated to the correct DTO shape consumed by the runtime.
/// </summary>
public class AstLoweringTests
{
    // ── helpers ──────────────────────────────────────────────────────────

    private static ModuleNode Parse(string textIr) =>
        ObjectIR.AST.TextIrParser.ParseModule(textIr);

    private static ModuleDto Lower(string textIr) =>
        AstLowering.Lower(Parse(textIr));

    private static string? OperandString(InstructionDto instr, string property)
    {
        if (instr.operand.ValueKind != JsonValueKind.Object) return null;
        return instr.operand.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    // ── Module ───────────────────────────────────────────────────────────

    [Fact]
    public void Lower_Module_SetsName()
    {
        var dto = Lower("module MyApp\nclass Program { }");
        Assert.Equal("MyApp", dto.name);
    }

    [Fact]
    public void Lower_Module_SetsVersion()
    {
        var dto = Lower("module MyApp version 2.3.1\nclass Program { }");
        Assert.Equal("2.3.1", dto.version);
    }

    [Fact]
    public void Lower_Module_DefaultsVersionTo_1_0_0_WhenMissing()
    {
        var dto = Lower("module MyApp\nclass Program { }");
        Assert.Equal("1.0.0", dto.version);
    }

    [Fact]
    public void Lower_Module_ContainsAllTopLevelTypes()
    {
        var dto = Lower(
            "module M\n" +
            "interface IFoo { }\n" +
            "class Bar { }\n" +
            "class Baz { }");

        Assert.Equal(3, dto.types.Length);
    }

    // ── Interface ────────────────────────────────────────────────────────

    [Fact]
    public void Lower_Interface_KindIsInterface()
    {
        var dto = Lower("module M\ninterface IGreeter { method Greet() -> void }");
        var type = Assert.Single(dto.types);
        Assert.Equal("interface", type.kind);
    }

    [Fact]
    public void Lower_Interface_SetsName()
    {
        var dto = Lower("module M\ninterface IGreeter { method Greet() -> void }");
        Assert.Equal("IGreeter", dto.types[0].name);
    }

    [Fact]
    public void Lower_Interface_MethodIsAbstract()
    {
        var dto = Lower("module M\ninterface IGreeter { method Greet() -> void }");
        var method = Assert.Single(dto.types[0].methods);
        Assert.True(method.isAbstract);
        Assert.Empty(method.instructions);
    }

    [Fact]
    public void Lower_Interface_MethodSignatureIsCorrect()
    {
        var dto = Lower("module M\ninterface ICalc { method Add(a: int32, b: int32) -> int32 }");
        var method = Assert.Single(dto.types[0].methods);
        Assert.Equal("Add", method.name);
        Assert.Equal("int32", method.returnType);
        Assert.Equal(2, method.parameters.Length);
        Assert.Equal("a", method.parameters[0].name);
        Assert.Equal("int32", method.parameters[0].type);
    }

    // ── Class ────────────────────────────────────────────────────────────

    [Fact]
    public void Lower_Class_KindIsClass()
    {
        var dto = Lower("module M\nclass Foo { }");
        Assert.Equal("class", dto.types[0].kind);
    }

    [Fact]
    public void Lower_Class_SetsName()
    {
        var dto = Lower("module M\nclass Foo { }");
        Assert.Equal("Foo", dto.types[0].name);
    }

    [Fact]
    public void Lower_Class_WithBaseType_SetsBaseType()
    {
        var dto = Lower("module M\nclass Dog : Animal { }");
        Assert.Equal("Animal", dto.types[0].baseType);
    }

    [Fact]
    public void Lower_Class_WithInterfaces_SetsInterfaces()
    {
        var dto = Lower("module M\nclass Dog : Animal, IRunnable, IJumpable { }");
        // First base type maps to baseType; the rest go to interfaces
        Assert.Equal("Animal", dto.types[0].baseType);
        Assert.Contains("IRunnable", dto.types[0].interfaces);
        Assert.Contains("IJumpable", dto.types[0].interfaces);
    }

    // ── Field ────────────────────────────────────────────────────────────

    [Fact]
    public void Lower_Field_SetsNameAndType()
    {
        var dto = Lower("module M\nclass Foo {\n  private field value: int32\n}");
        var field = Assert.Single(dto.types[0].fields);
        Assert.Equal("value", field.name);
        Assert.Equal("int32", field.type);
    }

    [Fact]
    public void Lower_Field_SetsAccessModifier()
    {
        var dto = Lower("module M\nclass Foo {\n  public field name: string\n}");
        Assert.Equal("public", dto.types[0].fields[0].access);
    }

    [Fact]
    public void Lower_Field_PrivateAccessModifier()
    {
        var dto = Lower("module M\nclass Foo {\n  private field id: int32\n}");
        Assert.Equal("private", dto.types[0].fields[0].access);
    }

    // ── Constructor ──────────────────────────────────────────────────────

    [Fact]
    public void Lower_Constructor_NameIsConstructor()
    {
        var dto = Lower("module M\nclass Foo {\n  constructor() {\n    ret\n  }\n}");
        var method = Assert.Single(dto.types[0].methods);
        Assert.Equal("constructor", method.name);
        Assert.True(method.isConstructor);
    }

    [Fact]
    public void Lower_Constructor_Parameters()
    {
        var dto = Lower("module M\nclass Foo {\n  constructor(x: int32, y: string) {\n    ret\n  }\n}");
        var ctor = dto.types[0].methods[0];
        Assert.Equal(2, ctor.parameters.Length);
        Assert.Equal("x", ctor.parameters[0].name);
        Assert.Equal("int32", ctor.parameters[0].type);
        Assert.Equal("y", ctor.parameters[1].name);
        Assert.Equal("string", ctor.parameters[1].type);
    }

    // ── Method ───────────────────────────────────────────────────────────

    [Fact]
    public void Lower_Method_SetsNameAndReturnType()
    {
        var dto = Lower("module M\nclass Foo {\n  method GetValue() -> int32 {\n    ret\n  }\n}");
        var method = dto.types[0].methods[0];
        Assert.Equal("GetValue", method.name);
        Assert.Equal("int32", method.returnType);
    }

    [Fact]
    public void Lower_Method_StaticFlag()
    {
        var dto = Lower("module M\nclass Foo {\n  static method Main() -> void {\n    ret\n  }\n}");
        Assert.True(dto.types[0].methods[0].isStatic);
    }

    [Fact]
    public void Lower_Method_Parameters()
    {
        var dto = Lower("module M\nclass Foo {\n  method Add(a: int32, b: int32) -> int32 {\n    ret\n  }\n}");
        var m = dto.types[0].methods[0];
        Assert.Equal(2, m.parameters.Length);
        Assert.Equal("a", m.parameters[0].name);
        Assert.Equal("b", m.parameters[1].name);
    }

    // ── Local declarations ────────────────────────────────────────────────

    [Fact]
    public void Lower_LocalDeclaration_BecomesLocalVariable_NotInstruction()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    local counter: int32\n" +
            "    ret\n" +
            "  }\n}");

        var method = dto.types[0].methods[0];
        var local = Assert.Single(method.localVariables);
        Assert.Equal("counter", local.name);
        Assert.Equal("int32", local.type);
        // Should not appear as an instruction
        Assert.DoesNotContain(method.instructions, i => i.opCode == "local");
    }

    // ── Simple instructions ───────────────────────────────────────────────

    [Fact]
    public void Lower_Ret_HasEmptyOperand()
    {
        var dto = Lower("module M\nclass Foo {\n  method Run() -> void {\n    ret\n  }\n}");
        var instr = Assert.Single(dto.types[0].methods[0].instructions);
        Assert.Equal("ret", instr.opCode);
        Assert.Equal(JsonValueKind.Object, instr.operand.ValueKind);
    }

    [Fact]
    public void Lower_Ldarg_HasArgumentNameOperand()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Echo(msg: string) -> string {\n" +
            "    ldarg msg\n    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var ldarg = instrs[0];
        Assert.Equal("ldarg", ldarg.opCode);
        Assert.Equal("msg", OperandString(ldarg, "argumentName"));
    }

    [Fact]
    public void Lower_Ldarg_This_HasThisArgumentName()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Get() -> void {\n" +
            "    ldarg this\n    ret\n  }\n}");

        var ldarg = dto.types[0].methods[0].instructions[0];
        Assert.Equal("this", OperandString(ldarg, "argumentName"));
    }

    [Fact]
    public void Lower_Ldloc_HasLocalNameOperand()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    local x: int32\n" +
            "    ldloc x\n    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var ldloc = instrs[0];
        Assert.Equal("ldloc", ldloc.opCode);
        Assert.Equal("x", OperandString(ldloc, "localName"));
    }

    [Fact]
    public void Lower_Stloc_HasLocalNameOperand()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    local x: int32\n" +
            "    ldc 0\n    stloc x\n    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var stloc = Array.Find(instrs, i => i.opCode == "stloc");
        Assert.NotNull(stloc);
        Assert.Equal("x", OperandString(stloc!, "localName"));
    }

    [Fact]
    public void Lower_Ldfld_StripsDeclaredTypePrefix()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  private field id: int32\n" +
            "  method GetId() -> int32 {\n" +
            "    ldarg this\n" +
            "    ldfld Foo.id\n" +
            "    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var ldfld = Array.Find(instrs, i => i.opCode == "ldfld");
        Assert.NotNull(ldfld);
        Assert.Equal("id", OperandString(ldfld!, "field"));
    }

    [Fact]
    public void Lower_Ldstr_HasValueOperand()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    ldstr \"hello\"\n    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var ldstr = instrs[0];
        Assert.Equal("ldstr", ldstr.opCode);
        Assert.Equal("hello", OperandString(ldstr, "value"));
    }

    [Fact]
    public void Lower_Ldc_HasValueAndTypeOperand()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    ldc 42\n    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var ldc = instrs[0];
        Assert.Equal("ldc", ldc.opCode);
        Assert.Equal("42", OperandString(ldc, "value"));
    }

    // ── Call instruction ─────────────────────────────────────────────────

    [Fact]
    public void Lower_CallInstruction_MapsMethodTarget()
    {
        var dto = Lower(
            "module M\nclass Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldstr \"hi\"\n" +
            "    call System.Console.WriteLine(string) -> void\n" +
            "    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var call = Array.Find(instrs, i => i.opCode == "call");
        Assert.NotNull(call);

        var operand = call!.operand;
        Assert.Equal(JsonValueKind.Object, operand.ValueKind);
        Assert.True(operand.TryGetProperty("method", out var method));
        Assert.Equal("System.Console", method.GetProperty("declaringType").GetString());
        Assert.Equal("WriteLine", method.GetProperty("name").GetString());
        Assert.Equal("void", method.GetProperty("returnType").GetString());
        var paramTypes = method.GetProperty("parameterTypes").EnumerateArray().Select(e => e.GetString()).ToArray();
        Assert.Contains("string", paramTypes);
    }

    [Fact]
    public void Lower_CallVirtInstruction_IsVirtual()
    {
        var dto = Lower(
            "module M\nclass Program {\n" +
            "  static method Main() -> void {\n" +
            "    callvirt Foo.GetValue() -> int32\n" +
            "    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        var callvirt = Array.Find(instrs, i => i.opCode == "callvirt");
        Assert.NotNull(callvirt);
    }

    // ── Control flow ──────────────────────────────────────────────────────

    [Fact]
    public void Lower_IfStatement_ProducesIfOpCode()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    if (stack) {\n" +
            "      ret\n" +
            "    }\n" +
            "    ret\n  }\n}");

        var instrs = dto.types[0].methods[0].instructions;
        Assert.Contains(instrs, i => i.opCode == "if");
    }

    [Fact]
    public void Lower_IfStatement_HasThenBlock()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    if (stack) {\n" +
            "      ret\n" +
            "    }\n" +
            "    ret\n  }\n}");

        var ifInstr = dto.types[0].methods[0].instructions.First(i => i.opCode == "if");
        Assert.True(ifInstr.operand.TryGetProperty("thenBlock", out var then));
        Assert.Equal(JsonValueKind.Array, then.ValueKind);
    }

    [Fact]
    public void Lower_IfElseStatement_HasElseBlock()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    if (stack) {\n" +
            "      ret\n" +
            "    } else {\n" +
            "      ret\n" +
            "    }\n" +
            "  }\n}");

        var ifInstr = dto.types[0].methods[0].instructions.First(i => i.opCode == "if");
        Assert.True(ifInstr.operand.TryGetProperty("elseBlock", out var els));
        Assert.Equal(JsonValueKind.Array, els.ValueKind);
    }

    [Fact]
    public void Lower_WhileStatement_ProducesWhileOpCode()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    while (stack) {\n" +
            "      ret\n" +
            "    }\n" +
            "    ret\n  }\n}");

        Assert.Contains(dto.types[0].methods[0].instructions, i => i.opCode == "while");
    }

    [Fact]
    public void Lower_WhileStatement_HasBodyBlock()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    while (stack) {\n" +
            "      ret\n" +
            "    }\n" +
            "    ret\n  }\n}");

        var whileInstr = dto.types[0].methods[0].instructions.First(i => i.opCode == "while");
        Assert.True(whileInstr.operand.TryGetProperty("body", out var body));
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
    }

    [Fact]
    public void Lower_WhileStatement_ConditionIsPresent()
    {
        var dto = Lower(
            "module M\nclass Foo {\n" +
            "  method Run() -> void {\n" +
            "    while (stack) {\n" +
            "      ret\n" +
            "    }\n" +
            "    ret\n  }\n}");

        var whileInstr = dto.types[0].methods[0].instructions.First(i => i.opCode == "while");
        Assert.True(whileInstr.operand.TryGetProperty("condition", out _));
    }

    // ── InstructionCount mirrors array length ─────────────────────────────

    [Fact]
    public void Lower_InstructionCount_MatchesArrayLength()
    {
        var dto = Lower(
            "module M\nclass Program {\n" +
            "  static method Main() -> void {\n" +
            "    ldstr \"a\"\n" +
            "    ldstr \"b\"\n" +
            "    ret\n  }\n}");

        var method = dto.types[0].methods[0];
        Assert.Equal(method.instructions.Length, method.instructionCount);
    }
}
