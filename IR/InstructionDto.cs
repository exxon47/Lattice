using System.Text.Json;

namespace OCRuntime.IR;

public sealed class InstructionDto
{
    public string opCode { get; set; } = "";
    public JsonElement operand { get; set; }
}

public sealed class CallTargetDto
{
    public string declaringType { get; set; } = "";
    public string name { get; set; } = "";
    public string returnType { get; set; } = "void";
    public string[] parameterTypes { get; set; } = Array.Empty<string>();
}

public sealed class FieldRefDto
{
    public string declaringType { get; set; } = "";
    public string name { get; set; } = "";
    public string? type { get; set; }
}

public enum ConditionKind
{
    None,
    Stack,
    Binary,
    Expression,
    Block
}

public sealed class ConditionDto
{
    public string kind { get; set; } = "none";
    public string? operation { get; set; }
    public InstructionDto? expression { get; set; }
    public InstructionDto[]? block { get; set; }

    public ConditionKind Kind => kind.ToLowerInvariant() switch
    {
        "stack" => ConditionKind.Stack,
        "binary" => ConditionKind.Binary,
        "expression" => ConditionKind.Expression,
        "block" => ConditionKind.Block,
        _ => ConditionKind.None
    };
}
