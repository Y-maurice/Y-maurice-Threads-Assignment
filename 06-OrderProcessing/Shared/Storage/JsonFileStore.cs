using System.Text.Json;
using System.Text.Json.Serialization;

namespace _06_OrderProcessing.Shared.Storage;

public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
}
