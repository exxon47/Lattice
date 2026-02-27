using OCRuntime.IR;

namespace OCRuntime.Runtime;

public sealed class CallFrame
{
    public MethodDto Method { get; }
    public int IP { get; set; }

    public Stack<object?> EvalStack { get; } = new();

    public object?[] Args { get; }
    public Dictionary<string, int> ArgNameToIndex { get; }

    public Dictionary<string, object?> Locals { get; }

    public ManagedObject? This { get; set; }

    public object? PendingException { get; set; }

    public CallFrame(MethodDto method, object?[]? args = null)
    {
        Method = method;
        IP = 0;

        Args = args ?? Array.Empty<object?>();
        ArgNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < method.parameters.Length; i++)
        {
            var p = method.parameters[i];
            if (!string.IsNullOrWhiteSpace(p.name))
            {
                ArgNameToIndex[p.name] = i;
            }
        }

        Locals = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var local in method.localVariables)
        {
            if (!string.IsNullOrWhiteSpace(local.name))
            {
                Locals[local.name] = null;
            }
        }
    }
}
