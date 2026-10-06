using System.Text.Json;

namespace NeoFences.Core.Lifecycle;

/// <summary>One check of the VM test pass (M38): its id, whether it passed, and what the guest script noted.</summary>
public sealed record CheckRow(string Id, bool Ok, string Note);

/// <summary>
/// The results file the test pass writes inside a VM (<c>results.json</c>, M38 spec §2), read on the host into a report. A
/// damaged or unfinished file is a failed report (the pass did not finish), never a throw.
/// </summary>
public sealed record CheckReport(string Machine, string Version, IReadOnlyList<CheckRow> Checks, int Failed, string Summary)
{
    public static CheckReport Read(string json)
    {
        string machine = "unknown machine", version = "?";
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return NoResults(machine, version);
            if (root.TryGetProperty("machine", out var machineValue) && machineValue.ValueKind == JsonValueKind.String) machine = machineValue.GetString()!;
            if (root.TryGetProperty("version", out var versionValue) && versionValue.ValueKind == JsonValueKind.String) version = versionValue.GetString()!;
            if (!root.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array) return NoResults(machine, version);
            var rows = new List<CheckRow>();
            foreach (var check in checks.EnumerateArray())
            {
                if (check.ValueKind != JsonValueKind.Object || !check.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String) continue;
                var ok = check.TryGetProperty("ok", out var okValue) && okValue.ValueKind == JsonValueKind.True;
                var note = check.TryGetProperty("note", out var noteValue) && noteValue.ValueKind == JsonValueKind.String ? noteValue.GetString()! : "";
                rows.Add(new CheckRow(id.GetString()!, ok, note));
            }
            if (rows.Count == 0) return NoResults(machine, version);
            var failed = rows.Where(row => !row.Ok).ToList();
            var summary = $"{machine}, {version}: {rows.Count - failed.Count} of {rows.Count} passed";
            if (failed.Count > 0)
                summary += "; failed: " + string.Join(", ", failed.Select(row => row.Note.Length > 0 ? $"{row.Id} ({row.Note})" : row.Id));
            return new CheckReport(machine, version, rows, failed.Count, summary);
        }
        catch (JsonException)
        {
            return NoResults(machine, version);
        }
    }

    private static CheckReport NoResults(string machine, string version) =>
        new(machine, version, [], 1, $"{machine}, {version}: no results (the test pass did not finish)");
}
