using System;

namespace OCRuntime;

/// <summary>
/// Minimal smoke tests for OCRuntime features claimed in docs.
/// Not a full unit test suite; this is meant to be runnable via Program/Main.
/// </summary>
internal static class RuntimeGapSmokeTests
{
    public static void RunAll()
    {
        HelloWorld_TextIr();
        IfCondition_Stack_TextIr();
        WhileLoop_TextIr();

        // TextIR grammar now supports try/catch/finally blocks.
        TryCatchFinally_TextIr();
    }

    private static void RunTextIr(string name, string textIr)
    {
        Console.WriteLine($"\n=== {name} ===");
        var rt = new IRRuntime(textIr);
        rt.Run();
    }

    private static void HelloWorld_TextIr()
    {
        RunTextIr(
            "HelloWorld",
            "module HelloWorld\n\n" +
            "class Program {\n" +
            "    static method Main() -> void {\n" +
            "        ldstr \"Hello, World!\"\n" +
            "        call System.Console.WriteLine(string) -> void\n" +
            "        ret\n" +
            "    }\n" +
            "}\n");
    }

    private static void IfCondition_Stack_TextIr()
    {
        // if (stack) { ... } else { ... }
        // Condition.Stack() corresponds to: if (stack)
        RunTextIr(
            "If(Stack)",
            "module IfTest\n\n" +
            "class Program {\n" +
            "    static method Main() -> void {\n" +
            "        ldc.i4 1\n" +
            "        if (stack) {\n" +
            "            ldstr \"then\"\n" +
            "            call System.Console.WriteLine(string) -> void\n" +
            "        } else {\n" +
            "            ldstr \"else\"\n" +
            "            call System.Console.WriteLine(string) -> void\n" +
            "        }\n" +
            "        ret\n" +
            "    }\n" +
            "}\n");
    }

    private static void WhileLoop_TextIr()
    {
        RunTextIr(
            "WhileLoop",
            "module WhileTest\n\n" +
            "class Program {\n" +
            "    static method Main() -> void {\n" +
            "        local i: int32\n" +
            "        ldc.i4 0\n" +
            "        stloc i\n\n" +
            "        while (i < 3) {\n" +
            "            ldloc i\n" +
            "            call System.Console.WriteLine(int32) -> void\n\n" +
            "            ldloc i\n" +
            "            ldc.i4 1\n" +
            "            add\n" +
            "            stloc i\n" +
            "        }\n\n" +
            "        ret\n" +
            "    }\n" +
            "}\n");
    }

    private static void TryCatchFinally_TextIr()
    {
        // try { throw new Exception("x") } catch(System.Exception) { WriteLine("caught") } finally { WriteLine("finally") }
        RunTextIr(
            "TryCatchFinally",
            "module TryTest\n\n" +
            "class Program {\n" +
            "    static method Main() -> void {\n" +
            "        try {\n" +
            "            ldstr \"boom\"\n" +
            "            newobj System.Exception\n" +
            "            throw\n" +
            "        } catch (System.Exception) {\n" +
            "            pop\n" + // Pop the exception object pushed by runtime
            "            ldstr \"caught\"\n" +
            "            call System.Console.WriteLine(string) -> void\n" +
            "        } finally {\n" +
            "            ldstr \"finally\"\n" +
            "            call System.Console.WriteLine(string) -> void\n" +
            "        }\n" +
            "        ret\n" +
            "    }\n" +
            "}\n");
    }
}
