using System.Text.Json;

namespace Omnimud.Core.Actions;

/// <summary>Defensive JSON reading shared by the translators: whatever is wrong, the answer is "nothing".</summary>
internal static class ActionMenuJson
{
    private static readonly JsonDocumentOptions Options = new() { MaxDepth = 32 };

    /// <summary>The document when the payload is a JSON object of reasonable size; null otherwise. The caller disposes it.</summary>
    public static JsonDocument? ParseObject(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload) || payload.Length > ActionMenuLimits.MaxPayloadLength)
            return null;

        try
        {
            var document = JsonDocument.Parse(payload, Options);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
                return document;

            document.Dispose();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static string? String(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object &&
           element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// The elements of an array property. Empty when the property is missing or null; null (through
    /// <paramref name="valid"/> = false) when it is there but is not an array.
    /// </summary>
    public static IEnumerable<JsonElement> Array(JsonElement element, string name, out bool valid)
    {
        valid = true;
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return [];

        if (value.ValueKind == JsonValueKind.Array)
            return value.EnumerateArray();

        valid = false;
        return [];
    }
}
