using System.Text.Json;
using System.Text.Json.Serialization;

namespace BananaFarm.Contracts;

/// <summary>
/// The single JSON configuration every service uses. Centralised so the farm cannot
/// serialize in a shape the factory fails to read.
/// </summary>
public static class BananaJson
{
    /// <summary>Shared serializer options: camelCase properties, enums as strings.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serialize <paramref name="value"/> to UTF-8 bytes for publishing.</summary>
    public static byte[] SerializeToUtf8Bytes<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value, Options);

    /// <summary>Deserialize a message body, or throw if the payload is not valid.</summary>
    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json) =>
        JsonSerializer.Deserialize<T>(utf8Json, Options)
        ?? throw new JsonException($"Payload deserialized to null for {typeof(T).Name}.");
}
