using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using NeoFences.Core.Model;

namespace NeoFences.Core.Config;

/// <summary>
/// JSON for <see cref="NeoFencesConfig"/>: camelCase names, camelCase enum values (spec §5 shape).
/// Reflection-based on purpose: the source generator assigns every init-only property, so a property
/// missing from a hand-edited file would lose its default (e.g. iconSize 0 instead of 48). ADR-006, amended in M1.
/// </summary>
public static class ConfigJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Hand-edited file, never embedded in HTML: write + and & as-is instead of + and &.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters =
        {
            // Appearance enums (M14): a hand-edited style or weight is repaired by the normalizer, never fails the file.
            new LenientEnumConverter<ColourStyle>(), new LenientEnumConverter<TitleWeight>(),
            // Folder views (M21): a typo in a view's show or sort is repaired too, not the whole file lost (final review).
            new LenientEnumConverter<ViewShow>(), new LenientEnumConverter<FenceSort>(),
            new LenientEnumConverter<Items.ItemShow>(), new LenientEnumConverter<Items.FenceLayout>(), // M24 // M22: a typo in items.json shows the usual look, never fails the file
            new LenientEnumConverter<Items.PanelLook>(), new LenientEnumConverter<Items.PanelSort>(), // M26
            // M36: a fence's own look and the presets; a typo is "like all fences", never a lost file.
            new LenientEnumConverter<TitleAlign>(), new LenientEnumConverter<Spacing>(), new LenientEnumConverter<LabelMode>(),
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
        },
    };

    public static string Serialize(NeoFencesConfig config) => JsonSerializer.Serialize(config, Options);

    /// <summary>A snapshot file (M10): the same names, enums and leniency as config.json.</summary>
    public static string SerializeSnapshot(Snapshot snapshot) => JsonSerializer.Serialize(snapshot, Options);

    /// <exception cref="JsonException">The text is not a snapshot document.</exception>
    public static Snapshot DeserializeSnapshot(string json) =>
        JsonSerializer.Deserialize<Snapshot>(json, Options) ?? throw new JsonException("the snapshot file contains null");

    /// <summary>items.json (M18, ADR-041): the same names and leniency as config.json.</summary>
    public static string SerializeItems(Items.ItemsDocument document) => JsonSerializer.Serialize(document, Options);

    /// <exception cref="JsonException">The text is not an items document.</exception>
    public static Items.ItemsDocument DeserializeItems(string json) =>
        JsonSerializer.Deserialize<Items.ItemsDocument>(json, Options) ?? throw new JsonException("items.json contains null");

    /// <summary>The library folder's index (M12): the same names and enums as config.json.</summary>
    public static string SerializeLibrary(Library.LibraryState state) => JsonSerializer.Serialize(state, Options);

    /// <exception cref="JsonException">The text is not a library index.</exception>
    public static Library.LibraryState DeserializeLibrary(string json) =>
        JsonSerializer.Deserialize<Library.LibraryState>(json, Options) ?? throw new JsonException("the library index contains null");

    /// <summary>Raw parse; explicit nulls and odd values survive. Run the result through <c>ConfigNormalizer</c>.</summary>
    /// <exception cref="JsonException">The text is not a valid config document.</exception>
    public static NeoFencesConfig Deserialize(string json) =>
        JsonSerializer.Deserialize<NeoFencesConfig>(json, Options)
        ?? throw new JsonException("config.json contains null");
}

/// <summary>
/// A camelCase enum that reads an unknown name (or anything else odd) as an undefined value instead of failing, so the
/// normalizer can repair it (M11 final review I3, M14). Undefined values are written as numbers.
/// </summary>
internal sealed class LenientEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
{
    private static readonly TEnum Unknown = (TEnum)Enum.ToObject(typeof(TEnum), -1);

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String when Enum.TryParse<TEnum>(reader.GetString(), ignoreCase: true, out var named) && Enum.IsDefined(named):
                return named;
            case JsonTokenType.Number when reader.TryGetInt32(out var number):
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            case JsonTokenType.StartObject or JsonTokenType.StartArray:
                reader.Skip();
                return Unknown;
            default:
                return Unknown;
        }
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (Enum.IsDefined(value)) writer.WriteStringValue(JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
        else writer.WriteNumberValue(Convert.ToInt32(value));
    }
}
