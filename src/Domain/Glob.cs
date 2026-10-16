using System.Collections.Concurrent;

namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>Path patterns as people write them: <c>*</c> for one segment, <c>**</c> for any number of them, <c>~</c> for home.</summary>
/// <remarks>
/// A census matches every trace against every rule: 2,495 traces and 16 rules, forty thousand matches, which cost
/// 170 ms and 46 MB when each one split the pattern and the path again (measured on 2026-10-12). A pattern is now
/// taken apart once, and a path is turned away on its first segment that differs from the pattern's fixed beginning,
/// without being split at all — and nearly every path is.
/// </remarks>
public static class Glob
{
    private static readonly ConcurrentDictionary<string, Compiled> Patterns = new(StringComparer.Ordinal);

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    /// <summary>Whether the pattern covers the path.</summary>
    public static bool Matches(string pattern, string path) =>
        Patterns.GetOrAdd(pattern, static written => new Compiled(Segments(Expanded(written)))).Matches(path);

    /// <summary>The pattern with a leading <c>~</c> written out as the home directory.</summary>
    public static string Expanded(string pattern)
    {
        if (string.Equals(pattern, "~", StringComparison.Ordinal))
            return Home;

        return pattern.StartsWith("~/", StringComparison.Ordinal) ? $"{Home}{pattern[1..]}" : pattern;
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

    /// <summary>A pattern taken apart: the segments it begins with literally, then the rest, each split at its stars.</summary>
    private sealed class Compiled
    {
        private readonly string[] _fixed;

        /// <summary>The segments after the fixed ones; null stands for <c>**</c>.</summary>
        private readonly string[]?[] _wild;

        public Compiled(string[] segments)
        {
            _fixed = [.. segments.TakeWhile(segment => !segment.Contains('*', StringComparison.Ordinal))];
            _wild = [.. segments.Skip(_fixed.Length).Select(segment => segment == "**" ? null : segment.Split('*'))];
        }

        public bool Matches(string path)
        {
            var text = path.AsSpan();
            var matched = 0;
            List<Range>? rest = null;

            foreach (var range in text.Split('/'))
            {
                if (range.Start.Value == range.End.Value)
                    continue;

                if (matched < _fixed.Length)
                {
                    if (!text[range].SequenceEqual(_fixed[matched]))
                        return false;

                    matched++;

                    continue;
                }

                (rest ??= []).Add(range);
            }

            return matched == _fixed.Length && Matches(0, text, rest is null ? [] : [.. rest], 0);
        }

        private bool Matches(int at, ReadOnlySpan<char> text, Range[] path, int on)
        {
            if (at == _wild.Length)
                return on == path.Length;

            if (_wild[at] is not { } parts)
            {
                for (var next = on; next <= path.Length; next++)
                {
                    if (Matches(at + 1, text, path, next))
                        return true;
                }

                return false;
            }

            return on < path.Length && MatchesSegment(parts, text[path[on]]) && Matches(at + 1, text, path, on + 1);
        }

        private static bool MatchesSegment(string[] parts, ReadOnlySpan<char> segment)
        {
            if (parts.Length == 1)
                return segment.SequenceEqual(parts[0]);

            if (!segment.StartsWith(parts[0], StringComparison.Ordinal) || !segment.EndsWith(parts[^1], StringComparison.Ordinal))
                return false;

            var position = parts[0].Length;

            for (var index = 1; index < parts.Length - 1; index++)
            {
                var found = segment[position..].IndexOf(parts[index], StringComparison.Ordinal);

                if (found < 0)
                    return false;

                position += found + parts[index].Length;
            }

            return position <= segment.Length - parts[^1].Length;
        }
    }
}
