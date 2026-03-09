using System.Text.Json;
using lattice.IR;
using ObjectIR.AST;

namespace lattice.TextIR;

/// <summary>
/// Lowers an ObjectIR.AST <see cref="ModuleNode"/> tree into the flat
/// <see cref="ModuleDto"/> representation consumed by <see cref="IRRuntime"/>.
/// </summary>
internal static class AstLowering
{
    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── Module ──────────────────────────────────────────────────────────

    internal static ModuleDto Lower(ModuleNode module)
    {
        var types = new List<TypeDto>();

        foreach (var iface in module.Interfaces)
            types.Add(LowerInterface(iface));

        foreach (var cls in module.Classes)
            types.Add(LowerClass(cls));

        return new ModuleDto
        {
            name = module.Name,
            version = module.Version ?? "1.0.0",
            metadata = JsonSerializer.SerializeToElement(new { }, s_json),
            functions = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json),
            types = types.ToArray()
        };
    }

    // ── Interface ───────────────────────────────────────────────────────

    private static TypeDto LowerInterface(InterfaceNode iface)
    {
        var methods = iface.Methods.Select(LowerMethodSignature).ToArray();

        return new TypeDto
        {
            kind = "interface",
            name = iface.Name,
            access = "public",
            methods = methods,
            fields = Array.Empty<FieldDto>(),
            interfaces = Array.Empty<string>(),
            attributes = Array.Empty<AttributeDto>(),
            baseInterfaces = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json),
            genericParameters = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json),
            properties = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json)
        };
    }

    private static MethodDto LowerMethodSignature(MethodSignature sig)
    {
        return new MethodDto
        {
            name = sig.Name,
            returnType = sig.ReturnType.Name,
            isStatic = sig.IsStatic,
            isAbstract = true,
            parameters = sig.Parameters.Select(LowerParameter).ToArray(),
            attributes = Array.Empty<AttributeDto>(),
            localVariables = Array.Empty<LocalVariableDto>(),
            instructions = Array.Empty<InstructionDto>(),
            instructionCount = 0
        };
    }

    // ── Class ───────────────────────────────────────────────────────────

    private static TypeDto LowerClass(ClassNode cls)
    {
        string? baseType = null;
        var interfaces = new List<string>();

        // First base type is the base class; the rest are interfaces.
        for (int i = 0; i < cls.BaseTypes.Count; i++)
        {
            if (i == 0)
                baseType = cls.BaseTypes[i];
            else
                interfaces.Add(cls.BaseTypes[i]);
        }

        var methods = new List<MethodDto>();

        foreach (var ctor in cls.Constructors)
            methods.Add(LowerConstructor(ctor));

        foreach (var m in cls.Methods)
            methods.Add(LowerMethod(m));

        return new TypeDto
        {
            kind = "class",
            name = cls.Name,
            access = "public",
            baseType = baseType,
            interfaces = interfaces.ToArray(),
            fields = cls.Fields.Select(LowerField).ToArray(),
            methods = methods.ToArray(),
            attributes = Array.Empty<AttributeDto>(),
            baseInterfaces = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json),
            genericParameters = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json),
            properties = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_json)
        };
    }

    // ── Field ───────────────────────────────────────────────────────────

    private static FieldDto LowerField(FieldNode field)
    {
        return new FieldDto
        {
            name = field.Name,
            type = field.FieldType.Name,
            access = field.Access.ToString().ToLowerInvariant(),
            isStatic = false,
            isReadOnly = false,
            attributes = Array.Empty<AttributeDto>()
        };
    }

    // ── Parameter ───────────────────────────────────────────────────────

    private static ParameterDto LowerParameter(ParameterNode p)
    {
        return new ParameterDto { name = p.Name, type = p.ParameterType.Name };
    }

    // ── Constructor ─────────────────────────────────────────────────────

    private static MethodDto LowerConstructor(ConstructorNode ctor)
    {
        var locals = new List<LocalVariableDto>();
        var instructions = LowerBlock(ctor.Body, locals);

        return new MethodDto
        {
            name = "constructor",
            returnType = "void",
            isConstructor = true,
            parameters = ctor.Parameters.Select(LowerParameter).ToArray(),
            attributes = Array.Empty<AttributeDto>(),
            localVariables = locals.ToArray(),
            instructions = instructions,
            instructionCount = instructions.Length
        };
    }

    // ── Method ──────────────────────────────────────────────────────────

    private static MethodDto LowerMethod(MethodNode method)
    {
        var locals = new List<LocalVariableDto>();
        var instructions = LowerBlock(method.Body, locals);

        return new MethodDto
        {
            name = method.Name,
            returnType = method.ReturnType.Name,
            isStatic = method.IsStatic,
            parameters = method.Parameters.Select(LowerParameter).ToArray(),
            attributes = Array.Empty<AttributeDto>(),
            localVariables = locals.ToArray(),
            instructions = instructions,
            instructionCount = instructions.Length
        };
    }

    // ── Block / Statement lowering ──────────────────────────────────────

    private static InstructionDto[] LowerBlock(BlockStatement block, List<LocalVariableDto> locals)
    {
        var instructions = new List<InstructionDto>();

        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case LocalDeclarationStatement local:
                    locals.Add(new LocalVariableDto { name = local.Name, type = local.LocalType.Name });
                    break;

                case InstructionStatement instrStmt:
                    instructions.Add(LowerInstruction(instrStmt.Instruction));
                    break;

                case IfStatement ifStmt:
                    instructions.Add(LowerIf(ifStmt, locals));
                    break;

                case WhileStatement whileStmt:
                    instructions.Add(LowerWhile(whileStmt, locals));
                    break;
            }
        }

        return instructions.ToArray();
    }

    // ── If / While ──────────────────────────────────────────────────────

    private static InstructionDto LowerIf(IfStatement ifStmt, List<LocalVariableDto> locals)
    {
        var condition = LowerCondition(ifStmt.Condition);
        var thenBlock = LowerBlock(ifStmt.Then, locals);

        object operand;
        if (ifStmt.Else is not null)
        {
            var elseBlock = LowerBlock(ifStmt.Else, locals);
            operand = new { condition, thenBlock, elseBlock };
        }
        else
        {
            operand = new { condition, thenBlock };
        }

        return new InstructionDto
        {
            opCode = "if",
            operand = JsonSerializer.SerializeToElement(operand, s_json)
        };
    }

    private static InstructionDto LowerWhile(WhileStatement whileStmt, List<LocalVariableDto> locals)
    {
        var condition = LowerCondition(whileStmt.Condition);
        var body = LowerBlock(whileStmt.Body, locals);

        return new InstructionDto
        {
            opCode = "while",
            operand = JsonSerializer.SerializeToElement(new { condition, body }, s_json)
        };
    }

    /// <summary>
    /// Converts a condition string from the AST into a structured condition
    /// object matching what the runtime expects.
    /// Handles "stack" (pop-from-stack) and binary expressions like "x == y".
    /// </summary>
    private static object LowerCondition(string condition)
    {
        var trimmed = condition.Trim();

        // stack-based condition: value already on the eval stack
        if (string.Equals(trimmed, "stack", StringComparison.OrdinalIgnoreCase))
        {
            return new { kind = "stack" };
        }

        // Try to parse a simple binary condition: <lhs> <op> <rhs>
        var (lhs, op, rhs) = TryParseBinaryCondition(trimmed);
        if (op is not null)
        {
            var block = new List<InstructionDto>();
            block.Add(EmitValueLoad(lhs));
            block.Add(EmitValueLoad(rhs));
            block.Add(new InstructionDto { opCode = op, operand = JsonSerializer.SerializeToElement(new { }, s_json) });
            return new { kind = "block", block = block.ToArray() };
        }

        // Fallback: treat the whole condition as a single value load (truthy check)
        return new { kind = "block", block = new[] { EmitValueLoad(trimmed) } };
    }

    private static (string Lhs, string? Op, string Rhs) TryParseBinaryCondition(string text)
    {
        // Try longest operators first
        string[] operators = ["==", "!=", ">=", "<=", ">", "<"];
        string[] opcodes = ["ceq", "cne", "cge", "cle", "cgt", "clt"];

        for (int i = 0; i < operators.Length; i++)
        {
            var idx = text.IndexOf(operators[i], StringComparison.Ordinal);
            if (idx > 0)
            {
                var lhs = text[..idx].Trim();
                var rhs = text[(idx + operators[i].Length)..].Trim();
                if (lhs.Length > 0 && rhs.Length > 0)
                    return (lhs, opcodes[i], rhs);
            }
        }

        return (text, null, "");
    }

    /// <summary>
    /// Emits a load instruction for a simple value token:
    /// numbers → ldc, quoted strings → ldstr, "this" → ldarg this,
    /// everything else → ldloc (could be local or argument; runtime resolves).
    /// </summary>
    private static InstructionDto EmitValueLoad(string token)
    {
        // Numeric literal
        if (long.TryParse(token, out _) || double.TryParse(token, out _))
        {
            return new InstructionDto
            {
                opCode = "ldc",
                operand = JsonSerializer.SerializeToElement(new { value = token, type = "int64" }, s_json)
            };
        }

        // Quoted string literal
        if (token.StartsWith('"') && token.EndsWith('"') && token.Length >= 2)
        {
            return new InstructionDto
            {
                opCode = "ldstr",
                operand = JsonSerializer.SerializeToElement(new { value = token[1..^1] }, s_json)
            };
        }

        // 'this' reference
        if (string.Equals(token, "this", StringComparison.Ordinal))
        {
            return new InstructionDto
            {
                opCode = "ldarg",
                operand = JsonSerializer.SerializeToElement(new { argumentName = "this" }, s_json)
            };
        }

        // Default: treat as local variable name
        return new InstructionDto
        {
            opCode = "ldloc",
            operand = JsonSerializer.SerializeToElement(new { localName = token }, s_json)
        };
    }

    // ── Instruction lowering ────────────────────────────────────────────

    private static InstructionDto LowerInstruction(Instruction instruction)
    {
        return instruction switch
        {
            SimpleInstruction simple => LowerSimpleInstruction(simple),
            CallInstruction call => LowerCallInstruction(call),
            NewObjInstruction newObj => LowerNewObjInstruction(newObj),
            _ => throw new NotSupportedException($"Unknown instruction type: {instruction.GetType().Name}")
        };
    }

    private static InstructionDto LowerSimpleInstruction(SimpleInstruction simple)
    {
        var op = simple.OpCode;
        var operandText = simple.Operand;

        // Normalize aliases
        if (op is "ldc.i4" or "ldc.i8")
            op = "ldc";
        if (op is "ldc.r4" or "ldc.r8")
            op = "ldc";

        // No operand
        if (string.IsNullOrWhiteSpace(operandText))
        {
            return new InstructionDto
            {
                opCode = op,
                operand = JsonSerializer.SerializeToElement(new { }, s_json)
            };
        }

        // Instruction-specific operand mapping
        switch (op)
        {
            case "ldarg":
                return new InstructionDto
                {
                    opCode = "ldarg",
                    operand = JsonSerializer.SerializeToElement(new { argumentName = operandText }, s_json)
                };

            case "starg":
                return new InstructionDto
                {
                    opCode = "starg",
                    operand = JsonSerializer.SerializeToElement(new { argumentName = operandText }, s_json)
                };

            case "ldloc":
            case "stloc":
                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(new { localName = operandText }, s_json)
                };

            case "ldstr":
            {
                // The AST parser stores string operands as raw text, e.g. `"hello"` with
                // the surrounding double-quote characters still present.  Strip them here
                // so the DTO carries the bare value just like the Lattice TextIR parser does.
                var strValue = operandText;
                if (strValue.StartsWith('"') && strValue.EndsWith('"') && strValue.Length >= 2)
                    strValue = strValue[1..^1];

                return new InstructionDto
                {
                    opCode = "ldstr",
                    operand = JsonSerializer.SerializeToElement(new { value = strValue }, s_json)
                };
            }

            case "ldc":
                return new InstructionDto
                {
                    opCode = "ldc",
                    operand = JsonSerializer.SerializeToElement(new { value = operandText, type = "int64" }, s_json)
                };

            case "ldfld":
            case "stfld":
            {
                var fieldName = operandText;
                var lastDot = fieldName.LastIndexOf('.');
                if (lastDot >= 0) fieldName = fieldName[(lastDot + 1)..];

                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(new { field = fieldName }, s_json)
                };
            }

            case "ldsfld":
            case "stsfld":
            {
                var lastDot = operandText.LastIndexOf('.');
                var declaringType = lastDot >= 0 ? operandText[..lastDot] : "";
                var fieldName = lastDot >= 0 ? operandText[(lastDot + 1)..] : operandText;

                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(new
                    {
                        field = new { declaringType, name = fieldName }
                    }, s_json)
                };
            }

            case "newobj":
            {
                var typeName = operandText;
                var dot = typeName.IndexOf('.', StringComparison.Ordinal);
                if (dot >= 0) typeName = typeName[..dot];

                return new InstructionDto
                {
                    opCode = "newobj",
                    operand = JsonSerializer.SerializeToElement(new { type = typeName }, s_json)
                };
            }

            case "newarr":
                return new InstructionDto
                {
                    opCode = "newarr",
                    operand = JsonSerializer.SerializeToElement(new { elementType = operandText }, s_json)
                };

            default:
                // Generic fallback: store raw operand as arguments array
                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(
                        new { arguments = new[] { operandText } }, s_json)
                };
        }
    }

    private static InstructionDto LowerCallInstruction(CallInstruction call)
    {
        var method = new CallTargetDto
        {
            declaringType = call.Target.DeclaringType.Name,
            name = call.Target.MethodName,
            parameterTypes = call.Arguments.Select(a => a.Name).ToArray(),
            returnType = call.ReturnType.Name
        };

        return new InstructionDto
        {
            opCode = call.IsVirtual ? "callvirt" : "call",
            operand = JsonSerializer.SerializeToElement(new { method }, s_json)
        };
    }

    private static InstructionDto LowerNewObjInstruction(NewObjInstruction newObj)
    {
        return new InstructionDto
        {
            opCode = "newobj",
            operand = JsonSerializer.SerializeToElement(new { type = newObj.Type.Name }, s_json)
        };
    }
}
