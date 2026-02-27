using System;
using System.Collections.Generic;
using System.Text.Json;
using OCRuntime.IR;
using OCRuntime.Runtime;

namespace OCRuntime.TextIR;

public static class AttributeFactory
{
    /// <summary>
    /// Instantiates a ManagedObject for an attribute, given its IR AttributeDto and the loaded type map.
    /// </summary>
    /// <param name="attr">The attribute IR node.</param>
    /// <param name="typeMap">A map of type name to TypeDto for all loaded types.</param>
    /// <param name="context">The instance the attribute is attached to (for 'this' resolution).</param>
    /// <param name="staticResolver">Function to resolve static references (e.g. "Class.Field").</param>
    /// <returns>A ManagedObject representing the attribute instance.</returns>
    public static ManagedObject CreateAttributeInstance(
        AttributeDto attr, 
        Dictionary<string, TypeDto> typeMap,
        ManagedObject? context = null,
        Func<string, object?>? staticResolver = null)
    {
        // Convention: attribute type names end with 'Attribute', but allow both forms
        var attrTypeName = attr.type.EndsWith("Attribute") ? attr.type : attr.type + "Attribute";
        if (!typeMap.ContainsKey(attrTypeName))
            throw new Exception($"Attribute type '{attrTypeName}' not found in IR");

        var obj = new ManagedObject(attrTypeName);
        
        var resolvedArgs = new List<object?>();
        foreach (var arg in attr.constructorArguments)
        {
            resolvedArgs.Add(ResolveArgument(arg, context, staticResolver));
        }

        obj.SetMetadata("ctorArgs", resolvedArgs.ToArray());
        return obj;
    }

    private static object? ResolveArgument(object arg, ManagedObject? context, Func<string, object?>? staticResolver)
    {
        if (arg is string s)
        {
            if (s == "this" && context != null) return context;
            
            // Try to resolve as static field if it looks like one
            if (staticResolver != null && s.Contains('.'))
            {
                try 
                {
                    return staticResolver(s);
                }
                catch
                {
                    // If resolution fails, treat as string literal
                }
            }
            return s;
        }
        
        if (arg is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.String) 
                return ResolveArgument(je.GetString()!, context, staticResolver);
            if (je.ValueKind == JsonValueKind.Number) 
                return je.GetDouble();
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
            if (je.ValueKind == JsonValueKind.Null) return null;
        }

        return arg;
    }
}
