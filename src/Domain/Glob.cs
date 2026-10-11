namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>Path patterns as people write them: <c>*</c> for one segment, <c>**</c> for any number of them, <c>~</c> for home.</summary>
public static class Glob
{
    /// <summary>Whether the pattern covers the path.</summary>
    public static bool Matches(string pattern, string path) =>
        Matches(Segments(Expanded(pattern)), 0, Segments(path), 0);

    /// <summary>The pattern with a leading <c>~</c> written out as the home directory.</summary>
    public static string Expanded(string pattern)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.Equals(pattern, "~", StringComparison.Ordinal))
            return home;

        return pattern.StartsWith("~/", StringComparison.Ordinal) ? $"{home}{pattern[1..]}" : pattern;
    }

    /// <summary>The longest leading part of a pattern that has no wildcard: the directory a trail has to look into.</summary>
    public static string Root(string pattern)
    {
        var fixedPart = Segments(Expanded(pattern)).TakeWhile(segment => !segment.Contains('*', StringComparison.Ordinal));

        return $"/{string.Join('/', fixedPart)}";
    }

    /// <summary>How many segments below its root a pattern reaches; null when <c>**</c> makes it unbounded.</summary>
    public static int? Depth(string pattern)
    {
        var segments = Segments(Expanded(pattern));
        var wild = segments.SkipWhile(segment => !segment.Contains('*', StringComparison.Ordinal)).ToList();

        return wild.Contains("**") ? null : wild.Count;
    }

    private static string[] Segments(string path) => path.Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static bool Matches(string[] pattern, int at, string[] path, int on)
    {
        if (at == pattern.Length)
            return on == path.Length;

        if (pattern[at] == "**")
            return Enumerable.Range(on, path.Length - on + 1).Any(next => Matches(pattern, at + 1, path, next));

        return on < path.Length && Segment(pattern[at], path[on]) && Matches(pattern, at + 1, path, on + 1);
    }

    private static bool Segment(string pattern, string segment)
    {
        var parts = pattern.Split('*');

        if (parts.Length == 1)
            return string.Equals(pattern, segment, StringComparison.Ordinal);

        if (!segment.StartsWith(parts[0], StringComparison.Ordinal) || !segment.EndsWith(parts[^1], StringComparison.Ordinal))
            return false;

        var position = parts[0].Length;

        foreach (var part in parts[1..^1])
        {
            var found = segment.IndexOf(part, position, StringComparison.Ordinal);

            if (found < 0)
                return false;

            position = found + part.Length;
        }

        return position <= segment.Length - parts[^1].Length;
    }
}
