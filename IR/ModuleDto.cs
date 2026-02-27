using System.Text.Json;

namespace OCRuntime.IR;

public sealed class ModuleDto
{
    public string name { get; set; } = "";
    public string version { get; set; } = "";
    public JsonElement metadata { get; set; }
    public TypeDto[] types { get; set; } = Array.Empty<TypeDto>();
    public JsonElement functions { get; set; }
}

public sealed class TypeDto
{
    public string kind { get; set; } = "";
    public string name { get; set; } = "";
    public string? _namespace { get; set; }
    public string? access { get; set; }

    public bool isAbstract { get; set; }
    public bool isSealed { get; set; }

    public string? baseType { get; set; }

    public AttributeDto[] attributes { get; set; } = Array.Empty<AttributeDto>();

    public FieldDto[] fields { get; set; } = Array.Empty<FieldDto>();
    public MethodDto[] methods { get; set; } = Array.Empty<MethodDto>();

    public string[] interfaces { get; set; } = Array.Empty<string>();
    public JsonElement baseInterfaces { get; set; }
    public JsonElement genericParameters { get; set; }
    public JsonElement properties { get; set; }
}

public sealed class FieldDto
{
    public string name { get; set; } = "";
    public string type { get; set; } = "";
    public string? access { get; set; }
    public bool isStatic { get; set; }
    public bool isReadOnly { get; set; }
    public AttributeDto[] attributes { get; set; } = Array.Empty<AttributeDto>();
}

public sealed class MethodDto
{
    public string name { get; set; } = "";
    public string returnType { get; set; } = "void";
    public string? access { get; set; }

    public bool isStatic { get; set; }
    public bool isVirtual { get; set; }
    public bool isOverride { get; set; }
    public bool isAbstract { get; set; }
    public bool isConstructor { get; set; }

    public AttributeDto[] attributes { get; set; } = Array.Empty<AttributeDto>();

    public ParameterDto[] parameters { get; set; } = Array.Empty<ParameterDto>();
    public LocalVariableDto[] localVariables { get; set; } = Array.Empty<LocalVariableDto>();

    public int instructionCount { get; set; }
    public InstructionDto[] instructions { get; set; } = Array.Empty<InstructionDto>();
}

public sealed class ParameterDto
{
    public string name { get; set; } = "";
    public string type { get; set; } = "";
}

public sealed class LocalVariableDto
{
    public string name { get; set; } = "";
    public string type { get; set; } = "";
}

public sealed class AttributeDto
{
    public string type { get; set; } = "";
    // For simplicity, we'll store arguments as strings or basic types if possible, 
    // but JSON deserialization of object[] can be tricky. 
    // Let's use JsonElement[] for now or just ignore args in this pass if not strictly needed.
    // But the user wants to "use Custom Attributes", so args might be important.
    public object[] constructorArguments { get; set; } = Array.Empty<object>();
}
