using System;

namespace Illustra.Shared.Attributes
{
    /// <summary>
    /// Defines metadata for an MCP tool.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class McpToolAttribute : Attribute
    {
        /// <summary>
        /// The name of the tool, used for invocation.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// A brief description of what the tool does.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// JSON Schema definition for the tool's input arguments as a string.
        /// </summary>
        public string InputSchemaJson { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="McpToolAttribute"/> class.
        /// </summary>
        /// <param name="name">The name of the tool.</param>
        /// <param name="description">The description of the tool.</param>
        /// <param name="inputSchemaJson">The JSON schema for the input as a string.</param>
        public McpToolAttribute(string name, string description, string inputSchemaJson)
        {
            Name = name;
            Description = description;
            InputSchemaJson = inputSchemaJson;
        }
    }
}
