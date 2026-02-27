using OCRuntime.IR;
using OCRuntime.TextIR;

namespace OCRuntime.Runtime;

public sealed class ManagedObject
{
    public string TypeName { get; }
    private readonly Dictionary<string, object?> _fields = new(StringComparer.Ordinal);
    private readonly Dictionary<string, object?> _metadata = new(StringComparer.Ordinal);
    private readonly List<object> _attributes = new();

    public ManagedObject(string typeName)
    {
        TypeName = typeName;
    }

    public object? GetField(string name)
    {
        _fields.TryGetValue(name, out var value);
        return value;
    }

    public void SetField(string name, object? value)
    {
        _fields[name] = value;
    }

    // Attribute reflection support
    public void AddAttribute(object attribute)
    {
        _attributes.Add(attribute);
    }

    public IEnumerable<object> GetAttributes() => _attributes;
    public T? GetAttribute<T>() where T : class
    {
        foreach (var attr in _attributes)
            if (attr is T t) return t;
        return null;
    }

    // Metadata support (for extensibility)
    public void SetMetadata(string key, object? value) => _metadata[key] = value;
    public object? GetMetadata(string key) => _metadata.TryGetValue(key, out var v) ? v : null;

    public void AttachAttributesFromIR(TypeDto type, Dictionary<string, TypeDto> typeMap, Func<string, object?>? staticResolver = null)
    {
        foreach (var attr in type.attributes)
        {
            var attrObj = AttributeFactory.CreateAttributeInstance(attr, typeMap, this, staticResolver);
            AddAttribute(attrObj);
        }
    }
    public void AttachAttributesFromIR(FieldDto field, Dictionary<string, TypeDto> typeMap, Func<string, object?>? staticResolver = null)
    {
        foreach (var attr in field.attributes)
        {
            var attrObj = AttributeFactory.CreateAttributeInstance(attr, typeMap, this, staticResolver);
            AddAttribute(attrObj);
        }
    }
    public void AttachAttributesFromIR(MethodDto method, Dictionary<string, TypeDto> typeMap, Func<string, object?>? staticResolver = null)
    {
        foreach (var attr in method.attributes)
        {
            var attrObj = AttributeFactory.CreateAttributeInstance(attr, typeMap, this, staticResolver);
            AddAttribute(attrObj);
        }
    }

    public override string ToString() => $"instance of {TypeName}";
}