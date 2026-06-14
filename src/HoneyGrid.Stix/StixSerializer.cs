using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HoneyGrid.Stix;

/// <summary>
/// Konfiguracja serializacji JSON dla obiektów STIX 2.1.
/// Specyfikacja wymaga kluczy snake_case (np. <c>spec_version</c>, <c>valid_from</c>);
/// część kluczy ma jawne <see cref="JsonPropertyNameAttribute"/>, polityka snake_case
/// pokrywa pozostałe (np. 'type', 'name'). Pola null są pomijane.
/// </summary>
public static class StixJson
{
    /// <summary>Współdzielone opcje serializacji STIX.</summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        // Wzorce STIX zawierają znaki '=' i ''' — relaksowany encoder zachowuje je
        // czytelnie zamiast escapować do = / ' (poprawny JSON w obu wypadkach).
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new StixObjectConverter() },
    };
}

/// <summary>
/// Konwerter polimorficzny: każdy element tablicy <c>objects</c> (deklarowany jako
/// <see cref="StixObject"/>) serializowany jest wg swojego typu runtime, dzięki czemu
/// emituje własne pola (pattern, name, source_ref...). Bez tego System.Text.Json
/// renderowałby wyłącznie pola typu bazowego (type/id).
/// </summary>
internal sealed class StixObjectConverter : JsonConverter<StixObject>
{
    public override StixObject Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        // Deserializacja polimorficzna nie jest potrzebna (tylko eksport) — testy
        // round-trip korzystają z JsonDocument, nie z tego konwertera.
        throw new NotSupportedException("Deserializacja obiektów STIX nie jest wspierana.");

    public override void Write(Utf8JsonWriter writer, StixObject value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, value, value.GetType(), options);
}

/// <summary>
/// Serializer bundli STIX do reprezentacji JSON zgodnej ze specyfikacją 2.1.
/// </summary>
public static class StixSerializer
{
    /// <summary>Serializuje bundle STIX do łańcucha JSON (snake_case, bez pól null).</summary>
    public static string ToJson(StixBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        // Serializacja jako StixObject (typ bazowy) gwarantuje, że polimorficzne
        // pola z tablicy 'objects' renderują się wraz z własnymi właściwościami.
        return JsonSerializer.Serialize<StixObject>(bundle, StixJson.Options);
    }
}
