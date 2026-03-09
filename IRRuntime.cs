﻿using System.Reflection;
using System.Text.Json;
using lattice.IR;
using lattice.Runtime;
using lattice.TextIR;

namespace lattice;

public sealed class IRRuntime
{
    private class ExceptionSignal : Exception
    {
        public object? ExceptionObject { get; }

        public ExceptionSignal(object? exceptionObject)
        {
            ExceptionObject = exceptionObject;
        }
    }

    public enum InputFormat
    {
        Auto,
        Json,
        TextIr
    }

    private ModuleDto _program;
    private readonly Stack<CallFrame> _callStack = new();

    public Action<CallFrame, InstructionDto>? OnStep;
    public Action<Exception>? OnException;

    public delegate object? NativeMethod(object? instance, object?[] args);
    private readonly Dictionary<string, NativeMethod> _nativeMethods = new(StringComparer.Ordinal);

    public bool EnableReflectionNativeMethods { get; set; }

    private readonly Dictionary<(string declaringType, string fieldName), object?> _staticFields = new();
    private Dictionary<string, TypeDto> _typeMap = new(StringComparer.Ordinal);

    public IRRuntime(string input = null, InputFormat format = InputFormat.Auto, bool enableReflectionNativeMethods = false)
    {
        EnableReflectionNativeMethods = enableReflectionNativeMethods;
        if (input != null)
        {

            _program = format switch
            {
                InputFormat.Json => DeserializeJsonModule(input),
                InputFormat.TextIr => TextIrParser.ParseModule(input),
                _ => AutoParse(input)
            };
            _typeMap = BuildTypeMap();
        }
      

            _nativeMethods["System.Console.WriteLine(string)"] = (instance, args) =>
            {
                Console.WriteLine(args.Length > 0 ? args[0] : null);
                return null;
            };

            _nativeMethods["System.Console.Write(string)"] = (instance, args) =>
            {
                Console.Write(args.Length > 0 ? args[0] : null);
                return null;
            };
            _nativeMethods["System.Console.WriteLine(int32)"] = (instance, args) =>
            {
                Console.WriteLine(args.Length > 0 ? args[0] : null);
                return null;
            };
            _nativeMethods["System.Console.WriteLine(object)"] = (instance, args) =>
            {
                Console.WriteLine(args.Length > 0 ? args[0] : null);
                return null;
            };
            _nativeMethods["System.Console.ReadLine()"] = (instance, args) => Console.ReadLine();
            _nativeMethods["System.Console.Clear()"] = (instance, args) =>
            {
                Console.Clear();
                return null;
            };
            _nativeMethods["System.Console.Beep()"] = (instance, args) =>
            {
                Console.Beep();
                return null;
            };


            
            _nativeMethods["System.Object.ToString()"] = (instance, args) => instance?.ToString();
            _nativeMethods["System.Object.GetType()"] = (instance, args) => instance?.GetType();
    }
    public void RegisterNativeMethod(string signature, NativeMethod method)
    {
        Console.WriteLine($"Registering native method: '{signature}'");
        _nativeMethods[signature] = method;
        var trimmed = signature.Trim();
        if (trimmed != signature)
        {
            Console.WriteLine($"Also registering trimmed signature: '{trimmed}'");
            _nativeMethods[trimmed] = method;
        }
    }

    /// <summary>
    /// Load (or replace) the IR module from a TextIR or JSON string.
    /// </summary>
    public void LoadModule(string input, InputFormat format = InputFormat.Auto)
    {
        _program = format switch
        {
            InputFormat.Json => DeserializeJsonModule(input),
            InputFormat.TextIr => TextIrParser.ParseModule(input),
            _ => AutoParse(input)
        };
        _typeMap = BuildTypeMap();
    }

    /// <summary>
    /// Call a method by "TypeName.MethodName" (or just "MethodName" to search all types)
    /// with the given arguments and return the result cast to <typeparamref name="TResult"/>.
    /// </summary>
    public TResult CallMethod<TResult>(string qualifiedName, params object?[] args)
    {
        var result = CallMethod(qualifiedName, args);
        if (result is null)
            return default!;
        if (result is TResult typed)
            return typed;
        return (TResult)ValueHelpers.ConvertTo(typeof(TResult), result);
    }

    /// <summary>
    /// Call a method by "TypeName.MethodName" (or just "MethodName") with the given arguments.
    /// </summary>
    public object? CallMethod(string qualifiedName, params object?[] args)
    {
        if (_program == null)
            throw new InvalidOperationException("No module loaded. Call LoadModule first.");

        string? typeName = null;
        string methodName = qualifiedName;
        var dot = qualifiedName.LastIndexOf('.');
        if (dot >= 0)
        {
            typeName = qualifiedName[..dot];
            methodName = qualifiedName[(dot + 1)..];
        }

        TypeDto? typeDto = null;
        MethodDto? methodDto = null;

        if (typeName != null)
        {
            typeDto = _program.types.FirstOrDefault(t =>
                string.Equals(t.name, typeName, StringComparison.Ordinal) ||
                QualifiedNameMatches(t, typeName));
            if (typeDto == null)
                throw new InvalidOperationException($"Type '{typeName}' not found in loaded module.");
            methodDto = typeDto.methods.FirstOrDefault(m => string.Equals(m.name, methodName, StringComparison.Ordinal));
        }
        else
        {
            foreach (var t in _program.types)
            {
                methodDto = t.methods.FirstOrDefault(m => string.Equals(m.name, methodName, StringComparison.Ordinal));
                if (methodDto != null) { typeDto = t; break; }
            }
        }

        if (methodDto == null)
            throw new MissingMethodException($"Method '{qualifiedName}' not found in loaded module.");

        var frame = new CallFrame(methodDto, args);
        _callStack.Push(frame);
        Execute();

        return frame.EvalStack.Count > 0 ? frame.EvalStack.Pop() : null;
    }
    private static ModuleDto AutoParse(string input)
    {
        var trimmed = input.AsSpan().TrimStart();
        if (!trimmed.IsEmpty && (trimmed[0] == '{' || trimmed[0] == '['))
            return DeserializeJsonModule(input);

        return TextIrParser.ParseModule(input);
    }

    private static ModuleDto DeserializeJsonModule(string json)
        => JsonSerializer.Deserialize<ModuleDto>(json)
           ?? throw new InvalidOperationException("Failed to deserialize module JSON");

    public void Run()
    {
        if (_program == null)
            throw new InvalidOperationException("No program loaded into IRRuntime");

        Console.WriteLine("Running Module: " + _program.name);
        TypeDto programType = null;
        try
        {

            programType = _program.types.SingleOrDefault(t => t.name == "Program");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Error finding Program type in module", ex);
        }
        if (programType == null)
            throw new InvalidOperationException("No Program type found in module");
        //// log the program methods
        //foreach (var t in _program.types)
        //{
        //    foreach (var m in t.methods)
        //    {
        //        Console.WriteLine($"Method: {t.name}.{m.name}()");
        //    }
        //}
        var entry = programType.methods.SingleOrDefault(m => m.name == "Main" && m.isStatic);
        if (entry == null)
            throw new InvalidOperationException("No static Program.Main method found");
        _callStack.Push(new CallFrame(entry));
        Execute();
    }

    private void Execute()
    {
        while (_callStack.Count > 0)
        {
            var frame = _callStack.Peek();

            if (frame.IP >= frame.Method.instructions.Length)
            {
                _callStack.Pop();
                continue;
            }

            var instr = frame.Method.instructions[frame.IP++];
            OnStep?.Invoke(frame, instr);
            ExecuteInstruction(frame, instr);
        }

        // Check for unhandled exception
        if (_callStack.Count > 0)
        {
            var topFrame = _callStack.Peek();
            if (topFrame.PendingException != null)
            {
                OnException?.Invoke(topFrame.PendingException as Exception ?? new Exception($"Unhandled IR exception: {topFrame.PendingException}"));
            }
        }
    }

    private void ExecuteInstruction(CallFrame frame, InstructionDto instr)
    {
        try
        {
            switch (instr.opCode)
            {
                case "nop":
                    return;

                case "dup":
                {
                    var v = frame.EvalStack.Peek();
                    frame.EvalStack.Push(v);
                    return;
                }

                case "pop":
                    _ = frame.EvalStack.Pop();
                    return;

                case "ldnull":
                    frame.EvalStack.Push(null);
                    return;

                case "ldc":
                {
                    var valueText = GetString(instr.operand, "value") ?? "null";
                    var typeName = GetString(instr.operand, "type") ?? "system.string";

                    object? value = valueText == "null" ? null : ValueHelpers.ConvertTo(typeName, valueText);
                    frame.EvalStack.Push(value);
                    return;
                }

                // Back-compat for older JSON or hand-authored IR
                case "ldstr":
                    frame.EvalStack.Push(GetString(instr.operand, "value"));
                    return;

                case "ldarg":
                {
                    if (TryGetInt(instr.operand, "index", out int index))
                    {
                        frame.EvalStack.Push(index >= 0 && index < frame.Args.Length ? frame.Args[index] : null);
                        return;
                    }

                    var argName = GetString(instr.operand, "argumentName");
                    if (argName != null && string.Equals(argName, "this", StringComparison.Ordinal))
                    {
                        frame.EvalStack.Push(frame.This);
                        return;
                    }
                    if (argName != null && frame.ArgNameToIndex.TryGetValue(argName, out int idx))
                    {
                        frame.EvalStack.Push(idx >= 0 && idx < frame.Args.Length ? frame.Args[idx] : null);
                        return;
                    }

                    throw new InvalidOperationException("ldarg missing operand.index (or operand.argumentName)");
                }

                case "starg":
                {
                    var argName = GetString(instr.operand, "argumentName")
                                  ?? throw new InvalidOperationException("starg missing operand.argumentName");

                    if (!frame.ArgNameToIndex.TryGetValue(argName, out int idx) || idx < 0 || idx >= frame.Args.Length)
                    {
                        throw new InvalidOperationException($"starg invalid argument '{argName}'");
                    }

                    frame.Args[idx] = frame.EvalStack.Pop();
                    return;
                }

                case "ldloc":
                {
                    var localName = GetString(instr.operand, "localName")
                                    ?? throw new InvalidOperationException("ldloc missing operand.localName");
                    frame.Locals.TryGetValue(localName, out var value);
                    frame.EvalStack.Push(value);
                    return;
                }

                case "stloc":
                {
                    var localName = GetString(instr.operand, "localName")
                                    ?? throw new InvalidOperationException("stloc missing operand.localName");
                    frame.Locals[localName] = frame.EvalStack.Pop();
                    return;
                }

                case "add":
                case "sub":
                case "mul":
                case "div":
                case "rem":
                {
                    var b = frame.EvalStack.Pop();
                    var a = frame.EvalStack.Pop();
                    frame.EvalStack.Push(ExecuteBinaryArithmetic(instr.opCode, a, b));
                    return;
                }

                case "neg":
                {
                    var v = frame.EvalStack.Pop();
                    frame.EvalStack.Push(-ValueHelpers.ToDouble(v));
                    return;
                }

                case "not":
                {
                    var v = frame.EvalStack.Pop();
                    frame.EvalStack.Push(!ValueHelpers.ToBool(v));
                    return;
                }

                case "ceq":
                case "cne":
                case "cgt":
                case "cge":
                case "clt":
                case "cle":
                {
                    var b = frame.EvalStack.Pop();
                    var a = frame.EvalStack.Pop();
                    frame.EvalStack.Push(ExecuteComparison(instr.opCode, a, b));
                    return;
                }

                case "newobj":
                {
                    var typeName = GetString(instr.operand, "type")
                                   ?? throw new InvalidOperationException("newobj missing operand.type");
                    var obj = new ManagedObject(typeName);
                    
                    // Attach attributes
                    if (_typeMap.TryGetValue(typeName, out var typeDto))
                    {
                        obj.AttachAttributesFromIR(typeDto, _typeMap, ResolveStaticField);
                    }

                    frame.EvalStack.Push(obj);
                    return;
                }

                case "newarr":
                {
                    _ = GetString(instr.operand, "elementType");
                    frame.EvalStack.Push(new List<object?>());
                    return;
                }

                case "ldelem":
                {
                    var index = (int)ValueHelpers.ToInt64(frame.EvalStack.Pop());
                    var arr = frame.EvalStack.Pop();
                    if (arr is not List<object?> list)
                        throw new InvalidOperationException("ldelem expects List<object?> array");
                    frame.EvalStack.Push(index >= 0 && index < list.Count ? list[index] : null);
                    return;
                }

                case "stelem":
                {
                    var value = frame.EvalStack.Pop();
                    var index = (int)ValueHelpers.ToInt64(frame.EvalStack.Pop());
                    var arr = frame.EvalStack.Pop();
                    if (arr is not List<object?> list)
                        throw new InvalidOperationException("stelem expects List<object?> array");

                    while (list.Count <= index)
                        list.Add(null);

                    list[index] = value;
                    return;
                }

                case "ldfld":
                {
                    var fieldName = GetFieldName(instr.operand) ?? throw new InvalidOperationException("ldfld missing operand.field.name");

                    ManagedObject? instance = null;
                    if (frame.EvalStack.Count > 0 && frame.EvalStack.Peek() is ManagedObject)
                    {
                        instance = (ManagedObject?)frame.EvalStack.Pop();
                    }
                    instance ??= frame.This;

                    if (instance == null)
                        throw new InvalidOperationException("ldfld requires an object instance (stack or this)");

                    frame.EvalStack.Push(instance.GetField(fieldName));
                    return;
                }

                case "stfld":
                {
                    var fieldName = GetFieldName(instr.operand) ?? throw new InvalidOperationException("stfld missing operand.field.name");
                    var value = frame.EvalStack.Pop();

                    ManagedObject? instance = null;
                    if (frame.EvalStack.Count > 0 && frame.EvalStack.Peek() is ManagedObject)
                    {
                        instance = (ManagedObject?)frame.EvalStack.Pop();
                    }
                    instance ??= frame.This;

                    if (instance == null)
                        throw new InvalidOperationException("stfld requires an object instance (stack or this)");

                    instance.SetField(fieldName, value);
                    return;
                }

                case "ldsfld":
                {
                    var key = GetStaticFieldKey(instr.operand);
                    _staticFields.TryGetValue(key, out var value);
                    frame.EvalStack.Push(value);
                    return;
                }

                case "stsfld":
                {
                    var key = GetStaticFieldKey(instr.operand);
                    _staticFields[key] = frame.EvalStack.Pop();
                    return;
                }

                case "conv":
                {
                    var targetType = GetString(instr.operand, "targetType")
                                     ?? throw new InvalidOperationException("conv missing operand.targetType");
                    var value = frame.EvalStack.Pop();
                    frame.EvalStack.Push(ValueHelpers.ConvertTo(targetType, value));
                    return;
                }

                case "castclass":
                {
                    var targetType = GetString(instr.operand, "targetType")
                                     ?? throw new InvalidOperationException("castclass missing operand.targetType");
                    var value = frame.EvalStack.Pop();

                    if (value is null)
                    {
                        frame.EvalStack.Push(null);
                        return;
                    }

                    if (value is ManagedObject obj && string.Equals(obj.TypeName, targetType, StringComparison.Ordinal))
                    {
                        frame.EvalStack.Push(obj);
                        return;
                    }

                    throw new InvalidCastException($"Cannot cast value to '{targetType}'");
                }

                case "isinst":
                {
                    var targetType = GetString(instr.operand, "targetType")
                                     ?? throw new InvalidOperationException("isinst missing operand.targetType");
                    var value = frame.EvalStack.Pop();
                    frame.EvalStack.Push(value is ManagedObject obj && string.Equals(obj.TypeName, targetType, StringComparison.Ordinal));
                    return;
                }

                case "break":
                    throw new BreakSignal();

                case "continue":
                    throw new ContinueSignal();

                case "if":
                {
                    var cond = GetCondition(instr.operand);
                    var thenBlock = GetInstructionBlock(instr.operand, "thenBlock")
                                    ?? throw new InvalidOperationException("if missing operand.thenBlock");
                    var elseBlock = GetInstructionBlock(instr.operand, "elseBlock");

                    if (EvaluateCondition(cond, frame))
                    {
                        ExecuteBlock(frame, thenBlock);
                    }
                    else if (elseBlock != null)
                    {
                        ExecuteBlock(frame, elseBlock);
                    }
                    return;
                }

                case "while":
                {
                    var cond = GetCondition(instr.operand);
                    var body = GetInstructionBlock(instr.operand, "body")
                               ?? throw new InvalidOperationException("while missing operand.body");

                    while (EvaluateCondition(cond, frame))
                    {
                        try
                        {
                            ExecuteBlock(frame, body);
                        }
                        catch (ContinueSignal)
                        {
                            continue;
                        }
                        catch (BreakSignal)
                        {
                            break;
                        }
                    }

                    return;
                }

                case "call":
                case "callvirt":
                    ExecuteCall(frame, instr);
                    return;

                case "ret":
                    ExecuteReturn();
                    return;

                case "throw":
                {
                    frame.PendingException = frame.EvalStack.Pop();
                    return;
                }

                case "try":
                {
                    var tryBlock = GetInstructionBlock(instr.operand, "tryBlock")
                                   ?? throw new InvalidOperationException("try missing operand.tryBlock");
                    var catchBlocks = GetCatchBlocks(instr.operand);
                    var finallyBlock = GetInstructionBlock(instr.operand, "finallyBlock");

                    ExecuteBlock(frame, tryBlock);
                    if (frame.PendingException != null)
                    {
                        var ex = frame.PendingException;
                        frame.PendingException = null; // clear it
                        bool handled = false;
                        foreach (var cb in catchBlocks)
                        {
                            if (cb.exceptionType == null)
                            {
                                frame.EvalStack.Push(ex);
                                ExecuteBlock(frame, cb.block);
                                handled = true;
                                break;
                            }
                            // Check for IR exception types (ManagedObject)
                            if (ex is ManagedObject mo && string.Equals(mo.TypeName, cb.exceptionType, StringComparison.Ordinal))
                            {
                                frame.EvalStack.Push(ex);
                                ExecuteBlock(frame, cb.block);
                                handled = true;
                                break;
                            }
                            // Fallback for CLR types
                            var catchType = ResolveClrType(cb.exceptionType);
                            if (catchType != null && ex != null && catchType.IsAssignableFrom(ex.GetType()))
                            {
                                frame.EvalStack.Push(ex);
                                ExecuteBlock(frame, cb.block);
                                handled = true;
                                break;
                            }
                        }
                        if (!handled)
                        {
                            // re-set the pending exception for upper frames
                            frame.PendingException = ex;
                        }
                    }

                    if (finallyBlock != null)
                    {
                        ExecuteBlock(frame, finallyBlock);
                    }
                    return;
                }

                default:
                    throw new NotSupportedException($"Unknown opcode '{instr.opCode}'");
            }
        }
        catch (Exception ex)
        {
            OnException?.Invoke(ex);
            throw;
        }
    }

    private void ExecuteBlock(CallFrame frame, InstructionDto[] instructions)
    {
        foreach (var i in instructions)
        {
            ExecuteInstruction(frame, i);
            if (frame.PendingException != null)
                break;
        }
    }

    private void ExecuteReturn()
    {
        var frame = _callStack.Pop();
        object? returnValue = frame.EvalStack.Count > 0 ? frame.EvalStack.Pop() : null;

        if (_callStack.Count > 0 && returnValue != null)
        {
            _callStack.Peek().EvalStack.Push(returnValue);
        }
    }

    private void ExecuteCall(CallFrame frame, InstructionDto instr)
    {
        var methodNode = instr.operand.ValueKind == JsonValueKind.Object && instr.operand.TryGetProperty("method", out var m)
            ? m
            : default;

        var target = methodNode.ValueKind == JsonValueKind.Object
            ? JsonSerializer.Deserialize<CallTargetDto>(methodNode.GetRawText())
            : null;

        if (target == null)
            throw new InvalidOperationException("call/callvirt missing operand.method");

        var signature = $"{target.declaringType}.{target.name}({string.Join(",", target.parameterTypes)})";
        // Diagnostic: log call signature to help debug native binding issues
        Console.WriteLine($"IR Call Signature: {signature}");

        var args = new object?[target.parameterTypes.Length];
        for (int i = args.Length - 1; i >= 0; i--)
        {
            args[i] = frame.EvalStack.Pop();
        }

        ManagedObject? instance = null;
        if (instr.opCode == "callvirt")
        {
            instance = frame.EvalStack.Pop() as ManagedObject;
            if (instance == null)
                throw new InvalidOperationException("callvirt requires an object instance on the stack");
        }

        if (_nativeMethods.TryGetValue(signature, out var native))
        {
            var result = native(instance, args);
            // If the result is a ManagedObject with type name ending in Exception, treat as VM exception
            if (result is ManagedObject mo && mo.TypeName.EndsWith("Exception", StringComparison.Ordinal))
            {
                frame.PendingException = mo;
            }
            else if (!IsVoid(target.returnType))
            {
                frame.EvalStack.Push(result);
            }
            return;
        }

        if (EnableReflectionNativeMethods && TryInvokeReflectedNative(target, args, instance, out var reflectedResult))
        {
            if (!IsVoid(target.returnType))
            {
                frame.EvalStack.Push(reflectedResult);
            }
            return;
        }

        var declaringType = _program.types.SingleOrDefault(t => t.name == target.declaringType || QualifiedNameMatches(t, target.declaringType));
        if (declaringType == null)
            throw new InvalidOperationException($"Type '{target.declaringType}' not found");
        var method = declaringType.methods.SingleOrDefault(m => m.name == target.name);
        if (method == null)
            throw new InvalidOperationException($"Method '{target.name}' not found in type '{target.declaringType}'");

        var newFrame = new CallFrame(method, args)
        {
            This = instance
        };

        _callStack.Push(newFrame);
    }

    private static bool TryInvokeReflectedNative(CallTargetDto target, object?[] args, object? instance, out object? result)
    {
        result = null;

        var clrType = ResolveClrType(target.declaringType);
        if (clrType == null)
            return false;

        var paramClrTypes = target.parameterTypes.Select(MapTypeNameToClrType).ToArray();
        if (paramClrTypes.Any(t => t == null))
            return false;

        var bindingFlags = BindingFlags.Public | BindingFlags.NonPublic;
        bindingFlags |= (instance == null) ? BindingFlags.Static : BindingFlags.Instance;

        var method = clrType.GetMethod(
            target.name,
            bindingFlags,
            binder: null,
            types: paramClrTypes!,
            modifiers: null);

        if (method == null)
            return false;

        var convertedArgs = new object?[args.Length];
        for (int i = 0; i < args.Length; i++)
        {
            convertedArgs[i] = ValueHelpers.ConvertTo(paramClrTypes[i]!, args[i]);
        }

        result = method.Invoke(instance, convertedArgs);
        return true;
    }

    private static Type? ResolveClrType(string typeName)
    {
        var t = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
        if (t != null) return t;

        t = MapTypeNameToClrType(typeName);
        if (t != null) return t;

        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            t = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (t != null) return t;
        }

        return null;
    }

    private static Type? MapTypeNameToClrType(string typeName)
    {
        var n = typeName.Trim().ToLowerInvariant();
        return n switch
        {
            "void" or "system.void" => typeof(void),
            "bool" or "system.boolean" => typeof(bool),
            "int8" or "system.sbyte" => typeof(sbyte),
            "uint8" or "system.byte" => typeof(byte),
            "int16" or "system.int16" => typeof(short),
            "uint16" or "system.uint16" => typeof(ushort),
            "int32" or "system.int32" => typeof(int),
            "uint32" or "system.uint32" => typeof(uint),
            "int64" or "system.int64" => typeof(long),
            "uint64" or "system.uint64" => typeof(ulong),
            "float32" or "single" or "system.single" => typeof(float),
            "float64" or "double" or "system.double" => typeof(double),
            "char" or "system.char" => typeof(char),
            "string" or "system.string" => typeof(string),
            _ => null
        };
    }

    private static bool QualifiedNameMatches(TypeDto t, string qualified)
    {
        if (string.IsNullOrWhiteSpace(t._namespace))
            return string.Equals(t.name, qualified, StringComparison.Ordinal);

        return string.Equals($"{t._namespace}.{t.name}", qualified, StringComparison.Ordinal);
    }

    private static bool IsVoid(string returnType)
    {
        var n = returnType.Trim().ToLowerInvariant();
        return n is "void" or "system.void";
    }

    private static object ExecuteBinaryArithmetic(string op, object? a, object? b)
    {
        // If either looks non-integer, promote to double.
        bool useDouble = a is float or double || b is float or double || a is string || b is string;
        if (useDouble)
        {
            var da = ValueHelpers.ToDouble(a);
            var db = ValueHelpers.ToDouble(b);
            return op switch
            {
                "add" => da + db,
                "sub" => da - db,
                "mul" => da * db,
                "div" => da / db,
                "rem" => da % db,
                _ => throw new NotSupportedException(op)
            };
        }

        var ia = ValueHelpers.ToInt64(a);
        var ib = ValueHelpers.ToInt64(b);
        return op switch
        {
            "add" => ia + ib,
            "sub" => ia - ib,
            "mul" => ia * ib,
            "div" => ia / ib,
            "rem" => ia % ib,
            _ => throw new NotSupportedException(op)
        };
    }

    private static bool ExecuteComparison(string op, object? a, object? b)
    {
        if (a is string sa && b is string sb)
        {
            return op switch
            {
                "ceq" => string.Equals(sa, sb, StringComparison.Ordinal),
                "cne" => !string.Equals(sa, sb, StringComparison.Ordinal),
                "cgt" => string.Compare(sa, sb, StringComparison.Ordinal) > 0,
                "cge" => string.Compare(sa, sb, StringComparison.Ordinal) >= 0,
                "clt" => string.Compare(sa, sb, StringComparison.Ordinal) < 0,
                "cle" => string.Compare(sa, sb, StringComparison.Ordinal) <= 0,
                _ => throw new NotSupportedException(op)
            };
        }

        if (a is bool ba && b is bool bb)
        {
            return op switch
            {
                "ceq" => ba == bb,
                "cne" => ba != bb,
                _ => throw new NotSupportedException($"Comparison '{op}' not supported for bool")
            };
        }

        var da = ValueHelpers.ToDouble(a);
        var db = ValueHelpers.ToDouble(b);
        return op switch
        {
            "ceq" => da == db,
            "cne" => da != db,
            "cgt" => da > db,
            "cge" => da >= db,
            "clt" => da < db,
            "cle" => da <= db,
            _ => throw new NotSupportedException(op)
        };
    }

    private static ConditionDto GetCondition(JsonElement operand)
    {
        if (operand.ValueKind != JsonValueKind.Object || !operand.TryGetProperty("condition", out var c))
            return new ConditionDto { kind = "stack" };

        return JsonSerializer.Deserialize<ConditionDto>(c.GetRawText()) ?? new ConditionDto { kind = "stack" };
    }

    private bool EvaluateCondition(ConditionDto condition, CallFrame frame)
    {
        switch (condition.Kind)
        {
            case ConditionKind.Stack:
                return ValueHelpers.ToBool(frame.EvalStack.Pop());

            case ConditionKind.Binary:
            {
                var right = frame.EvalStack.Pop();
                var left = frame.EvalStack.Pop();
                var op = condition.operation ?? "ceq";
                return ExecuteComparison(op, left, right);
            }

            case ConditionKind.Expression:
            {
                if (condition.expression != null)
                {
                    ExecuteInstruction(frame, condition.expression);
                }
                return ValueHelpers.ToBool(frame.EvalStack.Pop());
            }

            case ConditionKind.Block:
            {
                if (condition.block != null)
                {
                    ExecuteBlock(frame, condition.block);
                }
                return ValueHelpers.ToBool(frame.EvalStack.Pop());
            }

            default:
                return false;
        }
    }

    private static InstructionDto[]? GetInstructionBlock(JsonElement operand, string name)
    {
        if (operand.ValueKind != JsonValueKind.Object || !operand.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return JsonSerializer.Deserialize<InstructionDto[]>(arr.GetRawText()) ?? Array.Empty<InstructionDto>();
    }

    private static string? GetFieldName(JsonElement operand)
    {
        if (operand.ValueKind != JsonValueKind.Object || !operand.TryGetProperty("field", out var f))
            return null;

        if (f.ValueKind == JsonValueKind.String)
            return f.GetString();

        if (f.ValueKind == JsonValueKind.Object && f.TryGetProperty("name", out var n))
            return n.GetString();

        return null;
    }

    private static (string declaringType, string fieldName) GetStaticFieldKey(JsonElement operand)
    {
        if (operand.ValueKind != JsonValueKind.Object || !operand.TryGetProperty("field", out var f))
            throw new InvalidOperationException("ldsfld/stsfld missing operand.field");

        if (f.ValueKind == JsonValueKind.Object)
        {
            var declaringType = f.TryGetProperty("declaringType", out var dt) ? dt.GetString() ?? "" : "";
            var fieldName = f.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            return (declaringType, fieldName);
        }

        throw new InvalidOperationException("ldsfld/stsfld operand.field must be an object");
    }

    private static string? GetString(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        return element.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()
            : null;
    }

    private static bool TryGetInt(JsonElement element, string property, out int value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object)
            return false;

        if (!element.TryGetProperty(property, out var p))
            return false;

        if (p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out value))
            return true;

        if (p.ValueKind == JsonValueKind.String && int.TryParse(p.GetString(), out value))
            return true;

        return false;
    }

    public void LoadProgram(string input, InputFormat format = InputFormat.Auto)
    {
        _program = format switch
        {
            InputFormat.Json => DeserializeJsonModule(input),
            InputFormat.TextIr => TextIrParser.ParseModule(input),
            _ => AutoParse(input)
        };
        // Reset runtime state
        _callStack.Clear();
        _staticFields.Clear();
    }

    private static (string? exceptionType, InstructionDto[] block)[] GetCatchBlocks(JsonElement operand)
    {
        if (operand.ValueKind != JsonValueKind.Object || !operand.TryGetProperty("catchBlocks", out var arr) || arr.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<(string?, InstructionDto[])>();
        }

        var list = new List<(string?, InstructionDto[])>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var exceptionType = item.TryGetProperty("exceptionType", out var et) && et.ValueKind == JsonValueKind.String ? et.GetString() : null;
            var block = item.TryGetProperty("block", out var b) && b.ValueKind == JsonValueKind.Array
                ? JsonSerializer.Deserialize<InstructionDto[]>(b.GetRawText()) ?? Array.Empty<InstructionDto>()
                : Array.Empty<InstructionDto>();
            list.Add((exceptionType, block));
        }
        return list.ToArray();
    }

    private Dictionary<string, TypeDto> BuildTypeMap()
    {
        var map = new Dictionary<string, TypeDto>(StringComparer.Ordinal);
        foreach (var t in _program.types)
        {
            map[t.name] = t;
        }
        return map;
    }

    private object? ResolveStaticField(string key)
    {
        var parts = key.Split('.');
        if (parts.Length != 2) return null;
        var k = (parts[0], parts[1]);
        _staticFields.TryGetValue(k, out var val);
        return val;
    }

    public IReadOnlyList<CallFrame> CallStack => _callStack.Reverse().ToList();
}



