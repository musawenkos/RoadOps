using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Server;

namespace RoadOps.Mcp.Tools;

/// <summary>
/// Registers tools with input schemas that every client can map. By default an optional <c>int? degree = null</c>
/// becomes <c>"type": ["integer", "null"]</c> with <c>"default": null</c>: legal JSON Schema, but clients that map tool
/// schemas onto a single-type dialect (e.g. the OpenAPI subset used for some function-calling APIs) reject the tool or
/// drop the constraint. Here that parameter is <c>"type": "integer"</c> and simply not required. Omitting it and
/// passing null mean the same thing for every tool, and an explicit null is still accepted.
/// </summary>
internal static class PortableToolSchemas
{
    public static readonly AIJsonSchemaCreateOptions SchemaOptions = new() { TransformSchemaNode = static (_, node) => MakeSingleType(node) };

    /// <summary>Same discovery and lifetime as the SDK's <c>WithTools&lt;T&gt;()</c>, plus <see cref="SchemaOptions"/>.</summary>
    public static IMcpServerBuilder WithPortableTools<TToolType>(this IMcpServerBuilder builder)
    {
        foreach (var method in typeof(TToolType).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<McpServerToolAttribute>() is null)
            {
                continue;
            }

            builder.Services.AddSingleton((Func<IServiceProvider, McpServerTool>)(method.IsStatic
                ? services => McpServerTool.Create(method, options: new() { Services = services, SchemaCreateOptions = SchemaOptions })
                : services => McpServerTool.Create(method, static r => ActivatorUtilities.CreateInstance(r.Services!, typeof(TToolType)),
                    new() { Services = services, SchemaCreateOptions = SchemaOptions })));
        }

        return builder;
    }

    internal static JsonNode MakeSingleType(JsonNode node)
    {
        if (node is JsonObject schema && schema["type"] is JsonArray types && types.Count == 2 &&
            types.SingleOrDefault(t => t?.GetValue<string>() != "null") is JsonValue single &&
            types.Any(t => t?.GetValue<string>() == "null"))
        {
            schema["type"] = single.GetValue<string>();
            if (schema.TryGetPropertyValue("default", out var defaultValue) && defaultValue is null)
            {
                schema.Remove("default");
            }
        }

        return node;
    }
}
