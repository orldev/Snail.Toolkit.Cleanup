using System.Text.Json;
using System.Text.Json.Serialization;

namespace Snail.MCP.CleanUp.Core.Adapters;

/// <summary>Rules as a JSON file a person can read and edit, laid over the rules the account ships with.</summary>
/// <remarks>
/// The file is the point: a rule learned from a discovery is a line of data anyone can see, correct or take
/// back, not a change hidden inside the program. The shipped rules come first and the learned ones after,
/// so the learned ones are what a reader sees last.
/// </remarks>
public sealed partial class JsonRules(string path, IReadOnlyList<Rule> shipped) : IRules
{
    /// <inheritdoc/>
    public async Task<IReadOnlyList<Rule>> ListAsync(CancellationToken cancellationToken) =>
        [.. shipped, .. await LearnedAsync(cancellationToken)];

    /// <inheritdoc/>
    public async Task AddAsync(Rule rule, CancellationToken cancellationToken)
    {
        var learned = await LearnedAsync(cancellationToken);
        var written = new Learned([.. learned.Where(known => known.Trail != rule.Trail || known.Pattern != rule.Pattern), rule]);
        var temporary = $"{path}.{Guid.NewGuid():n}";

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(written, RulesJson.Default.Learned), cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private async Task<IReadOnlyList<Rule>> LearnedAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return [];

        try
        {
            return JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, cancellationToken), RulesJson.Default.Learned)?.Rules ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>The shape of the file: one list, so that the file can grow other sections without breaking.</summary>
    private sealed record Learned(IReadOnlyList<Rule> Rules);

    /// <summary>Actions written the way a person writes them in the file: <c>cache</c>, not <c>Cache</c>.</summary>
    private sealed class ActionNames() : JsonStringEnumConverter<RuleAction>(JsonNamingPolicy.CamelCase);

    /// <summary>The file's shape, read and written by generated code, which a native build needs and reflection is not.</summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = [typeof(ActionNames)])]
    [JsonSerializable(typeof(Learned))]
    private sealed partial class RulesJson : JsonSerializerContext;
}
