using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OCRuntime.IR;

namespace OCRuntime.TextIR;

internal static class TextIrParser
{
    internal static ModuleDto ParseModule(string text)
    {
        var tokens = TextIrLexer.Lex(text);
        var p = new Parser(tokens);
        return p.ParseModule();
    }

    private sealed class Parser
    {
        private readonly IReadOnlyList<TextIrToken> _tokens;
        private int _current;

        // Options to enable reflection-based serialization where needed.
        private static readonly JsonSerializerOptions s_jsonOptions = new JsonSerializerOptions
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver()
        };

        public Parser(IReadOnlyList<TextIrToken> tokens)
        {
            _tokens = tokens;
        }

        public ModuleDto ParseModule()
        {
            // module Name [version X]
            ConsumeKeyword("module", "Expected 'module'");
            var moduleName = ConsumeIdentifier("Expected module name").Value;

            string version = "1.0.0";
            if (MatchKeyword("version"))
            {
                // Accept dotted identifiers/numbers (e.g. 1.0.0)
                version = ConsumeVersionLike("Expected version after 'version'");
            }

            var types = new List<TypeDto>();

            while (!IsAtEnd())
            {
                var attributes = ParseAttributes();

                if (CheckKeyword("class"))
                    types.Add(ParseType("class", attributes));
                else if (CheckKeyword("interface"))
                    types.Add(ParseType("interface", attributes));
                else if (CheckKeyword("struct"))
                    types.Add(ParseType("struct", attributes));
                else if (CheckKeyword("enum"))
                    types.Add(ParseType("enum", attributes));
                else if (Match(TextIrTokenType.Newline) || Match(TextIrTokenType.Eof))
                    continue;
                else
                    Advance();
            }

            return new ModuleDto
            {
                name = moduleName,
                version = version,
                metadata = JsonSerializer.SerializeToElement(new { }, s_jsonOptions),
                functions = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_jsonOptions),
                types = types.ToArray()
            };
        }

        private TypeDto ParseType(string kind, List<AttributeDto> attributes)
        {
            ConsumeKeyword(kind, $"Expected '{kind}'");
            var typeName = ConsumeIdentifier($"Expected {kind} name").Value;

            string? baseType = null;
            var interfaces = new List<string>();

            // Optional : BaseOrInterface[, ...]
            if (Match(TextIrTokenType.Colon))
            {
                if (!Check(TextIrTokenType.LBrace))
                {
                    baseType = ConsumeIdentifier("Expected base type").Value;
                }
                
                while (Match(TextIrTokenType.Comma))
                {
                    interfaces.Add(ConsumeIdentifier("Expected interface name").Value);
                }
            }

            Consume(TextIrTokenType.LBrace, "Expected '{' to start type body");

            var fields = new List<FieldDto>();
            var methods = new List<MethodDto>();

            while (!Check(TextIrTokenType.RBrace) && !IsAtEnd())
            {
                    // Skip blank lines
                    if (Match(TextIrTokenType.Newline))
                        continue;

                    var memberAttributes = ParseAttributes();

                    // Collect optional modifiers
                    string? access = null;
                    bool isStatic = false;
                    bool isVirtual = false;
                    bool isOverride = false;
                    bool isAbstract = false;

                bool consumedModifier;
                do
                {
                    consumedModifier = false;
                    if (MatchKeyword("public")) { access = "public"; consumedModifier = true; }
                    else if (MatchKeyword("private")) { access = "private"; consumedModifier = true; }
                    else if (MatchKeyword("protected")) { access = "protected"; consumedModifier = true; }
                    else if (MatchKeyword("internal")) { access = "internal"; consumedModifier = true; }
                    else if (MatchKeyword("static")) { isStatic = true; consumedModifier = true; }
                    else if (MatchKeyword("virtual")) { isVirtual = true; consumedModifier = true; }
                    else if (MatchKeyword("override")) { isOverride = true; consumedModifier = true; }
                    else if (MatchKeyword("abstract")) { isAbstract = true; consumedModifier = true; }
                    else if (MatchKeyword("sealed")) { consumedModifier = true; }
                    else if (Match(TextIrTokenType.Newline)) { consumedModifier = true; }
                } while (consumedModifier);

                if (CheckKeyword("field"))
                {
                    fields.Add(ParseField(access, isStatic, memberAttributes));
                }
                else if (CheckKeyword("method"))
                {
                    methods.Add(ParseMethod(isStatic, isVirtual, isOverride, isAbstract, isConstructor: false, access, memberAttributes));
                }
                else if (CheckKeyword("constructor"))
                {
                    methods.Add(ParseMethod(isStatic: false, isVirtual: false, isOverride: false, isAbstract: false, isConstructor: true, access, memberAttributes));
                }
                else
                {
                    // Skip unknown/member
                    Advance();
                }
            }

            Consume(TextIrTokenType.RBrace, "Expected '}' to end type body");

            return new TypeDto
            {
                kind = kind,
                name = typeName,
                _namespace = null,
                access = "public",
                isAbstract = false,
                isSealed = false,
                baseType = baseType,
                attributes = attributes.ToArray(),
                fields = fields.ToArray(),
                methods = methods.ToArray(),
                interfaces = interfaces.ToArray(),
                baseInterfaces = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_jsonOptions),
                genericParameters = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_jsonOptions),
                properties = JsonSerializer.SerializeToElement(Array.Empty<object>(), s_jsonOptions)
            };
        }

        private FieldDto ParseField(string? access, bool isStatic, List<AttributeDto> attributes)
        {
            ConsumeKeyword("field", "Expected 'field'");
            var name = ConsumeIdentifier("Expected field name").Value;
            Consume(TextIrTokenType.Colon, "Expected ':' after field name");

            var typeText = ReadTypeTextUntilLineEnd();

            return new FieldDto
            {
                name = name,
                type = typeText,
                access = access,
                isStatic = isStatic,
                isReadOnly = false,
                attributes = attributes.ToArray()
            };
        }

        private MethodDto ParseMethod(bool isStatic, bool isVirtual, bool isOverride, bool isAbstract, bool isConstructor, string? access, List<AttributeDto> attributes)
        {
            if (isConstructor)
            {
                ConsumeKeyword("constructor", "Expected 'constructor'");
            }
            else
            {
                ConsumeKeyword("method", "Expected 'method'");
            }

            string methodName;
            if (isConstructor)
            {
                methodName = "constructor";
            }
            else
            {
                // Support legacy/alternate syntax where constructor methods are written as '.ctor' or '.cctor'
                if (Peek().Type == TextIrTokenType.Dot)
                {
                    // Consume '.' and then expect an identifier like 'ctor' or 'cctor'
                    Advance(); // consume '.'
                    var id = ConsumeIdentifier("Expected constructor name after '.'");
                    methodName = "." + id.Value;
                    if (string.Equals(id.Value, "ctor", StringComparison.Ordinal) || string.Equals(id.Value, "cctor", StringComparison.Ordinal))
                    {
                        // Treat .ctor/.cctor as a constructor
                        isConstructor = true;
                    }
                }
                else
                {
                    methodName = ConsumeIdentifier("Expected method name").Value;
                }
            }

            var parameters = ParseParameters();

            string returnType = "void";
            if (!isConstructor && Match(TextIrTokenType.Arrow))
            {
                returnType = ReadTypeTextUntilLineEnd(stopAtLBrace: true);
            }

            // Optional: implements Foo.Bar
            if (MatchKeyword("implements"))
            {
                while (!Check(TextIrTokenType.LBrace) && !Check(TextIrTokenType.Newline) && !IsAtEnd())
                    Advance();
            }

            // Body
            var locals = new List<LocalVariableDto>();
            var instructions = new List<InstructionDto>();

            if (Match(TextIrTokenType.LBrace))
            {
                ParseMethodBody(parameters, locals, instructions);
                Consume(TextIrTokenType.RBrace, "Expected '}' to end method body");
            }

            return new MethodDto
            {
                name = methodName,
                returnType = returnType,
                access = access,
                isStatic = isStatic,
                isVirtual = isVirtual,
                isOverride = isOverride,
                isAbstract = isAbstract,
                isConstructor = isConstructor,
                attributes = attributes.ToArray(),
                parameters = parameters.ToArray(),
                localVariables = locals.ToArray(),
                instructionCount = instructions.Count,
                instructions = instructions.ToArray()
            };
        }

        private List<ParameterDto> ParseParameters()
        {
            var parameters = new List<ParameterDto>();

            Consume(TextIrTokenType.LParen, "Expected '('");
            while (!Check(TextIrTokenType.RParen) && !IsAtEnd())
            {
                if (Check(TextIrTokenType.Newline))
                {
                    Advance();
                    continue;
                }

                var paramName = ConsumeIdentifier("Expected parameter name").Value;
                Consume(TextIrTokenType.Colon, "Expected ':' after parameter name");
                var paramType = ReadTypeTextUntilCommaOrParen();

                parameters.Add(new ParameterDto { name = paramName, type = paramType });

                if (Match(TextIrTokenType.Comma))
                    continue;
            }
            Consume(TextIrTokenType.RParen, "Expected ')'");
            return parameters;
        }

        private void ParseMethodBody(List<ParameterDto> parameters, List<LocalVariableDto> locals, List<InstructionDto> instructions)
        {
            // We parse until matching RBrace at this nesting level.
            var localNames = new HashSet<string>(locals.Select(l => l.name), StringComparer.Ordinal);
            var paramNames = new HashSet<string>(parameters.Select(p => p.name), StringComparer.Ordinal);

            while (!Check(TextIrTokenType.RBrace) && !IsAtEnd())
            {
                if (Match(TextIrTokenType.Newline))
                    continue;

                if (CheckKeyword("local"))
                {
                    Advance();
                    var localName = ConsumeIdentifier("Expected local name").Value;
                    Consume(TextIrTokenType.Colon, "Expected ':' after local name");
                    var localType = ReadTypeTextUntilLineEnd();
                    locals.Add(new LocalVariableDto { name = localName, type = localType });
                    localNames.Add(localName);
                    continue;
                }

                if (CheckInstruction("if"))
                {
                    instructions.Add(ParseIfInstruction(localNames, paramNames));
                    continue;
                }

                if (CheckInstruction("while"))
                {
                    instructions.Add(ParseWhileInstruction(localNames, paramNames));
                    continue;
                }

                if (Peek().Type == TextIrTokenType.Instruction)
                {
                    instructions.Add(ParseSimpleInstruction());
                    continue;
                }

                // Skip unknown tokens.
                Advance();
            }
        }

        private InstructionDto ParseIfInstruction(HashSet<string> localNames, HashSet<string> paramNames)
        {
            ConsumeInstruction("if", "Expected 'if'");
            var condition = ParseConditionBlock(localNames, paramNames);
            Consume(TextIrTokenType.LBrace, "Expected '{' after if condition");
            var thenBlock = ParseInstructionBlock(localNames, paramNames);
            Consume(TextIrTokenType.RBrace, "Expected '}' after if block");

            InstructionDto[]? elseBlock = null;
            if (MatchKeyword("else"))
            {
                Consume(TextIrTokenType.LBrace, "Expected '{' after else");
                elseBlock = ParseInstructionBlock(localNames, paramNames);
                Consume(TextIrTokenType.RBrace, "Expected '}' after else block");
            }

            object operand;
            if (elseBlock == null)
                operand = new { condition, thenBlock };
            else
                operand = new { condition, thenBlock, elseBlock };

            return new InstructionDto
            {
                opCode = "if",
                operand = JsonSerializer.SerializeToElement(operand, s_jsonOptions)
            };
        }

        private InstructionDto ParseWhileInstruction(HashSet<string> localNames, HashSet<string> paramNames)
        {
            ConsumeInstruction("while", "Expected 'while'");
            var condition = ParseConditionBlock(localNames, paramNames);
            Consume(TextIrTokenType.LBrace, "Expected '{' after while condition");
            var body = ParseInstructionBlock(localNames, paramNames);
            Consume(TextIrTokenType.RBrace, "Expected '}' after while body");

            return new InstructionDto
            {
                opCode = "while",
                operand = JsonSerializer.SerializeToElement(new { condition, body }, s_jsonOptions)
            };
        }

        private InstructionDto[] ParseInstructionBlock(HashSet<string> localNames, HashSet<string> paramNames)
        {
            var list = new List<InstructionDto>();
            while (!Check(TextIrTokenType.RBrace) && !IsAtEnd())
            {
                if (Match(TextIrTokenType.Newline))
                    continue;

                if (CheckInstruction("if"))
                {
                    list.Add(ParseIfInstruction(localNames, paramNames));
                    continue;
                }

                if (CheckInstruction("while"))
                {
                    list.Add(ParseWhileInstruction(localNames, paramNames));
                    continue;
                }

                if (Peek().Type == TextIrTokenType.Instruction)
                {
                    list.Add(ParseSimpleInstruction());
                    continue;
                }

                Advance();
            }

            return list.ToArray();
        }

        private object ParseConditionBlock(HashSet<string> localNames, HashSet<string> paramNames)
        {
            Consume(TextIrTokenType.LParen, "Expected '('");

            // Special case: (stack)
            if (CheckIdentifierValue("stack"))
            {
                Advance();
                Consume(TextIrTokenType.RParen, "Expected ')'");
                return new { kind = "stack" };
            }

            // Parse: <lhs> <op> <rhs>
            var lhs = ReadUntilComparisonOperator();
            var op = ParseComparisonOperator();
            var rhs = ReadUntil(TokenPredicateParenEnd);
            Consume(TextIrTokenType.RParen, "Expected ')'");

            var block = new List<InstructionDto>();
            block.AddRange(EmitValueLoad(lhs, localNames, paramNames));
            block.AddRange(EmitValueLoad(rhs, localNames, paramNames));
            block.Add(new InstructionDto { opCode = op, operand = JsonSerializer.SerializeToElement(new { }) });

            return new { kind = "block", block = block.ToArray() };
        }

        private static bool TokenPredicateParenEnd(TextIrToken t) => t.Type == TextIrTokenType.RParen;

        private List<TextIrToken> ReadUntilComparisonOperator()
        {
            return ReadUntil(t =>
                t.Type is TextIrTokenType.EqualEqual or TextIrTokenType.BangEqual or TextIrTokenType.Less or TextIrTokenType.LessEqual or TextIrTokenType.Greater or TextIrTokenType.GreaterEqual);
        }

        private string ParseComparisonOperator()
        {
            if (Match(TextIrTokenType.EqualEqual)) return "ceq";
            if (Match(TextIrTokenType.BangEqual)) return "cne";
            if (Match(TextIrTokenType.Less)) return "clt";
            if (Match(TextIrTokenType.LessEqual)) return "cle";
            if (Match(TextIrTokenType.Greater)) return "cgt";
            if (Match(TextIrTokenType.GreaterEqual)) return "cge";
            throw new FormatException("Invalid comparison operator in condition");
        }

        private List<TextIrToken> ReadUntil(Func<TextIrToken, bool> stop)
        {
            var list = new List<TextIrToken>();
            while (!IsAtEnd() && !stop(Peek()))
            {
                if (Peek().Type != TextIrTokenType.Newline)
                    list.Add(Advance());
                else
                    Advance();
            }
            return list;
        }

        private IEnumerable<InstructionDto> EmitValueLoad(List<TextIrToken> expr, HashSet<string> localNames, HashSet<string> paramNames)
        {
            // Very small expression surface:
            // - identifier => ldloc/ldarg (prefers params; special 'this')
            // - number => ldc int32
            // - string => ldstr
            if (expr.Count == 0)
                yield break;

            if (expr.Count == 1 && expr[0].Type == TextIrTokenType.String)
            {
                yield return new InstructionDto { opCode = "ldstr", operand = JsonSerializer.SerializeToElement(new { value = expr[0].Value }) };
                yield break;
            }

            if (expr.Count == 1 && expr[0].Type == TextIrTokenType.Number)
            {
                // Use int64 for numeric literals to avoid overflow for large constants (e.g. ARGB colors)
                yield return new InstructionDto { opCode = "ldc", operand = JsonSerializer.SerializeToElement(new { value = expr[0].Value, type = "int64" }) };
                yield break;
            }

            if (expr.Count == 1 && expr[0].Type == TextIrTokenType.Identifier)
            {
                var name = expr[0].Value;
                if (string.Equals(name, "this", StringComparison.Ordinal))
                {
                    yield return new InstructionDto { opCode = "ldarg", operand = JsonSerializer.SerializeToElement(new { argumentName = "this" }) };
                    yield break;
                }

                if (paramNames.Contains(name))
                {
                    yield return new InstructionDto { opCode = "ldarg", operand = JsonSerializer.SerializeToElement(new { argumentName = name }) };
                    yield break;
                }

                if (localNames.Contains(name))
                {
                    yield return new InstructionDto { opCode = "ldloc", operand = JsonSerializer.SerializeToElement(new { localName = name }) };
                    yield break;
                }

                // Fallback: treat as local.
                yield return new InstructionDto { opCode = "ldloc", operand = JsonSerializer.SerializeToElement(new { localName = name }) };
                yield break;
            }

            // Fallback: stringify token sequence and try loading as identifier.
            var text = string.Join("", expr.Select(t => t.Value));
            yield return new InstructionDto { opCode = "ldloc", operand = JsonSerializer.SerializeToElement(new { localName = text }) };
        }

        private InstructionDto ParseSimpleInstruction()
        {
            var op = Consume(TextIrTokenType.Instruction, "Expected instruction").Value;

            // Read args until newline or brace
            var args = new List<TextIrToken>();
            while (!IsAtEnd() && !Check(TextIrTokenType.Newline) && !Check(TextIrTokenType.LBrace) && !Check(TextIrTokenType.RBrace))
            {
                args.Add(Advance());
            }
            Match(TextIrTokenType.Newline);

            // Normalize some textual aliases
            if (op is "ldc.i4" or "ldc.i8")
                op = "ldc";

            if (op is "ldc.r4" or "ldc.r8")
                op = "ldc";

            if (op == "ldarg" && args.Count >= 1)
            {
                return new InstructionDto
                {
                    opCode = "ldarg",
                    operand = JsonSerializer.SerializeToElement(new { argumentName = args[0].Value })
                };
            }

            if (op == "starg" && args.Count >= 1)
            {
                return new InstructionDto
                {
                    opCode = "starg",
                    operand = JsonSerializer.SerializeToElement(new { argumentName = args[0].Value })
                };
            }

            if ((op == "ldloc" || op == "stloc") && args.Count >= 1)
            {
                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(new { localName = args[0].Value }, s_jsonOptions)
                };
            }

            if (op == "ldstr" && args.Count >= 1)
            {
                return new InstructionDto
                {
                    opCode = "ldstr",
                    operand = JsonSerializer.SerializeToElement(new { value = args[0].Value }, s_jsonOptions)
                };
            }

            if (op == "ldc" && args.Count >= 1)
            {
                // Default numeric literal type to int64 to support large constants
                return new InstructionDto
                {
                    opCode = "ldc",
                    operand = JsonSerializer.SerializeToElement(new { value = args[0].Value, type = "int64" }, s_jsonOptions)
                };
            }

            if ((op == "ldfld" || op == "stfld") && args.Count >= 1)
            {
                var field = args[0].Value;
                var lastDot = field.LastIndexOf('.');
                if (lastDot >= 0) field = field[(lastDot + 1)..];

                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(new { field }, s_jsonOptions)
                };
            }

            if ((op == "call" || op == "callvirt") && args.Count >= 1)
            {
                var method = ParseCallTarget(args);
                return new InstructionDto
                {
                    opCode = op,
                    operand = JsonSerializer.SerializeToElement(new { method }, s_jsonOptions)
                };
            }

            if (op == "newobj" && args.Count >= 1)
            {
                // newobj TypeName[.constructor(...)] 
                var typeTokenText = string.Join("", args.Select(a => a.Value));
                var typeName = typeTokenText;
                var dot = typeName.IndexOf('.', StringComparison.Ordinal);
                if (dot >= 0)
                    typeName = typeName[..dot];

                return new InstructionDto
                {
                    opCode = "newobj",
                    operand = JsonSerializer.SerializeToElement(new { type = typeName }, s_jsonOptions)
                };
            }

            // Default: raw args as strings
                return args.Count == 0
                ? new InstructionDto { opCode = op, operand = JsonSerializer.SerializeToElement(new { }, s_jsonOptions) }
                : new InstructionDto { opCode = op, operand = JsonSerializer.SerializeToElement(new { arguments = args.Select(a => a.Value).ToArray() }, s_jsonOptions) };
        }

        private static CallTargetDto ParseCallTarget(List<TextIrToken> args)
        {
            // Expected: <DeclaringType>.<Name> ( <paramTypes...> ) -> <returnType>
            // We'll reconstruct the method name by consuming tokens until '('.

            var i = 0;
            var sb = new StringBuilder();
            while (i < args.Count && args[i].Type != TextIrTokenType.LParen)
            {
                sb.Append(args[i].Value);
                i++;
            }

            var fullMethodName = sb.ToString();
            var lastDot = fullMethodName.LastIndexOf('.');
            var declaringType = lastDot >= 0 ? fullMethodName[..lastDot] : "object";
            var name = lastDot >= 0 ? fullMethodName[(lastDot + 1)..] : fullMethodName;

            // Params
            var paramTypes = new List<string>();
            if (i < args.Count && args[i].Type == TextIrTokenType.LParen)
            {
                i++; // skip '('
                var paramSb = new StringBuilder();
                while (i < args.Count && args[i].Type != TextIrTokenType.RParen)
                {
                    if (args[i].Type == TextIrTokenType.Comma)
                    {
                        var p = paramSb.ToString().Trim();
                        if (p.Length > 0) paramTypes.Add(p);
                        paramSb.Clear();
                    }
                    else
                    {
                        paramSb.Append(args[i].Value);
                    }
                    i++;
                }
                var last = paramSb.ToString().Trim();
                if (last.Length > 0) paramTypes.Add(last);

                if (i < args.Count && args[i].Type == TextIrTokenType.RParen)
                    i++;
            }

            // Return type after ->
            string returnType = "void";
            for (; i < args.Count; i++)
            {
                if (args[i].Type == TextIrTokenType.Arrow)
                {
                    i++;
                    if (i < args.Count)
                    {
                        returnType = string.Join("", args.Skip(i).Select(t => t.Value)).Trim();
                    }
                    break;
                }
            }

            return new CallTargetDto
            {
                declaringType = declaringType,
                name = name,
                parameterTypes = paramTypes.ToArray(),
                returnType = returnType
            };
        }

        private List<AttributeDto> ParseAttributes()
        {
            var attributes = new List<AttributeDto>();
            while (Match(TextIrTokenType.LBracket))
            {
                do
                {
                    var name = ConsumeIdentifier("Expected attribute name").Value;
                    var args = new List<object>();
                    if (Match(TextIrTokenType.LParen))
                    {
                        if (!Check(TextIrTokenType.RParen))
                        {
                            do
                            {
                                if (Match(TextIrTokenType.String))
                                    args.Add(Previous().Value);
                                else if (Match(TextIrTokenType.Number))
                                    args.Add(double.Parse(Previous().Value));
                                else if (Match(TextIrTokenType.Identifier))
                                    args.Add(Previous().Value);
                                else
                                    throw new Exception("Invalid attribute argument");
                            } while (Match(TextIrTokenType.Comma));
                        }
                        Consume(TextIrTokenType.RParen, "Expected ')' after attribute arguments");
                    }
                    attributes.Add(new AttributeDto { type = name, constructorArguments = args.ToArray() });
                } while (Match(TextIrTokenType.Comma));

                Consume(TextIrTokenType.RBracket, "Expected ']' after attributes");
                
                // Consume optional newline after attributes
                Match(TextIrTokenType.Newline);
            }
            return attributes;
        }


        private string ReadTypeTextUntilCommaOrParen()
        {
            var sb = new StringBuilder();
            while (!IsAtEnd() && !Check(TextIrTokenType.Comma) && !Check(TextIrTokenType.RParen))
            {
                if (Peek().Type != TextIrTokenType.Newline)
                    sb.Append(Advance().Value);
                else
                    Advance();
            }
            return sb.ToString().Trim();
        }

        private string ReadTypeTextUntilLineEnd(bool stopAtLBrace = false)
        {
            var sb = new StringBuilder();
            while (!IsAtEnd() && !Check(TextIrTokenType.Newline) && !(stopAtLBrace && Check(TextIrTokenType.LBrace)) && !Check(TextIrTokenType.RBrace))
            {
                sb.Append(Advance().Value);
            }
            Match(TextIrTokenType.Newline);
            return sb.ToString().Trim();
        }

        private bool CheckIdentifierValue(string value)
            => Peek().Type == TextIrTokenType.Identifier && string.Equals(Peek().Value, value, StringComparison.Ordinal);

        private bool Check(TextIrTokenType type)
            => Peek().Type == type;

        private bool CheckKeyword(string value)
            => Peek().Type == TextIrTokenType.Keyword && string.Equals(Peek().Value, value, StringComparison.Ordinal);

        private bool MatchKeyword(string value)
        {
            if (CheckKeyword(value))
            {
                Advance();
                return true;
            }
            return false;
        }

        private void ConsumeKeyword(string value, string message)
        {
            if (!MatchKeyword(value))
                throw new FormatException(message + $" (got '{Peek().Value}')");
        }

        private bool CheckInstruction(string value)
            => Peek().Type == TextIrTokenType.Instruction && string.Equals(Peek().Value, value, StringComparison.Ordinal);

        private void ConsumeInstruction(string value, string message)
        {
            if (!CheckInstruction(value))
                throw new FormatException(message);
            Advance();
        }

        private string ConsumeVersionLike(string message)
        {
            var sb = new StringBuilder();
            while (!IsAtEnd() && !Check(TextIrTokenType.Newline) && !CheckKeyword("class") && !CheckKeyword("interface") && !CheckKeyword("struct") && !CheckKeyword("enum"))
            {
                var t = Advance();
                if (t.Type is TextIrTokenType.Identifier or TextIrTokenType.Number)
                    sb.Append(t.Value);
                else
                    break;
            }
            Match(TextIrTokenType.Newline);
            var v = sb.ToString().Trim();
            if (v.Length == 0) throw new FormatException(message);
            return v;
        }

        private TextIrToken ConsumeIdentifier(string message)
        {
            if (Peek().Type != TextIrTokenType.Identifier)
                throw new FormatException(message);
            return Advance();
        }

        private TextIrToken Consume(TextIrTokenType type, string message)
        {
            if (Peek().Type != type)
                throw new FormatException(message);
            return Advance();
        }

        private bool Match(TextIrTokenType type)
        {
            if (Peek().Type == type)
            {
                Advance();
                return true;
            }
            return false;
        }

        private TextIrToken Advance()
        {
            if (!IsAtEnd()) _current++;
            return Previous();
        }

        private bool IsAtEnd() => Peek().Type == TextIrTokenType.Eof;

        private TextIrToken Peek() => _tokens[_current];
        private TextIrToken Previous() => _tokens[_current - 1];
    }
}
