using System.Globalization;

namespace OCRuntime.Runtime;

public static class ValueHelpers
{
    public static bool ToBool(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            int i => i != 0,
            long l => l != 0,
            float f => Math.Abs(f) > float.Epsilon,
            double d => Math.Abs(d) > double.Epsilon,
            string s => !string.IsNullOrEmpty(s),
            _ => true
        };
    }

    public static double ToDouble(object? value)
    {
        return value switch
        {
            null => 0,
            int i => i,
            long l => l,
            float f => f,
            double d => d,
            bool b => b ? 1 : 0,
            string s => double.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Cannot convert '{value.GetType().FullName}' to double")
        };
    }

    public static long ToInt64(object? value)
    {
        return value switch
        {
            null => 0,
            int i => i,
            long l => l,
            float f => (long)f,
            double d => (long)d,
            bool b => b ? 1 : 0,
            string s => long.Parse(s, CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Cannot convert '{value.GetType().FullName}' to int64")
        };
    }

    public static object? ConvertTo(string targetType, object? value)
    {
        var normalized = targetType.Trim().ToLowerInvariant();

        return normalized switch
        {
            "void" or "system.void" => null,

            "bool" or "boolean" or "system.bool" or "system.boolean" => ToBool(value),

            "int" or "int32" or "system.int32" => checked((int)ToInt64(value)),
            "long" or "int64" or "system.int64" => ToInt64(value),

            "float" or "float32" or "system.single" or "system.float32" => (float)ToDouble(value),
            "double" or "float64" or "system.double" or "system.float64" => ToDouble(value),

            "string" or "system.string" => value?.ToString(),
            "object" or "system.object" => value,

            _ => value
        };
    }

    public static object? ConvertTo(Type targetType, object? value)
    {
        if (targetType == typeof(void))
            return null;

        if (value == null)
            return targetType.IsValueType ? Activator.CreateInstance(targetType) : null;

        if (targetType.IsInstanceOfType(value))
            return value;

        if (targetType == typeof(string))
            return value.ToString();

        if (targetType == typeof(bool))
            return ToBool(value);

        if (targetType == typeof(int))
            return checked((int)ToInt64(value));
        if (targetType == typeof(long))
            return ToInt64(value);
        if (targetType == typeof(short))
            return checked((short)ToInt64(value));
        if (targetType == typeof(byte))
            return checked((byte)ToInt64(value));
        if (targetType == typeof(sbyte))
            return checked((sbyte)ToInt64(value));
        if (targetType == typeof(uint))
            return checked((uint)ToInt64(value));
        if (targetType == typeof(ulong))
            return checked((ulong)ToInt64(value));
        if (targetType == typeof(ushort))
            return checked((ushort)ToInt64(value));

        if (targetType == typeof(float))
            return (float)ToDouble(value);
        if (targetType == typeof(double))
            return ToDouble(value);

        if (targetType == typeof(char))
        {
            if (value is char c) return c;
            var s = value.ToString();
            return string.IsNullOrEmpty(s) ? '\0' : s![0];
        }

        try
        {
            return System.Convert.ChangeType(value, targetType, CultureInfo.InvariantCulture);
        }
        catch
        {
            return value;
        }
    }
}
