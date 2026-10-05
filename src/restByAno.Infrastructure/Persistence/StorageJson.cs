using System.Text.Json;
using System.Text.Json.Serialization;

namespace restByAno.Infrastructure.Persistence;

/// <summary>Serializer settings for settings and stored definitions: camelCase properties, enums persisted by name.</summary>
public static class StorageJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            MaxDepth = 64,
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
