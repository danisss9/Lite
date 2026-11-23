#if !NET8_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Nodes;

namespace System.Text.Json.Nodes;

internal static class JsonNodePolyfills
{
    public static JsonNode DeepClone(this JsonNode node) =>
        JsonNode.Parse(node.ToJsonString())!;

    public static JsonValueKind GetValueKind(this JsonNode node) => node switch
    {
        JsonObject => JsonValueKind.Object,
        JsonArray => JsonValueKind.Array,
        JsonValue => JsonDocument.Parse(node.ToJsonString()).RootElement.ValueKind,
        _ => JsonValueKind.Undefined,
    };
}
#endif
