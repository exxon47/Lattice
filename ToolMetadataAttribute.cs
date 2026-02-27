using System;

namespace OCRuntime
{
    /// <summary>
    /// Custom attribute for tool metadata extensibility.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Property, Inherited = false, AllowMultiple = true)]
    public sealed class ToolMetadataAttribute : Attribute
    {
        public string Key { get; }
        public string Value { get; }

        public ToolMetadataAttribute(string key, string value)
        {
            Key = key;
            Value = value;
        }
    }
}

