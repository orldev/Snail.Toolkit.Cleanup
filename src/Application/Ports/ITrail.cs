namespace Snail.Toolkit.Cleanup.Application.Ports;

/// <summary>One place leftovers can show up in, looked at without knowing what they are: files, processes, ports.</summary>
/// <remarks>
/// A trail is how the account notices what no kind was written for. It is followed when an owner begins
/// and at every census after; whatever is new, claimed by no kind and covered by no rule becomes a
/// discovery — written down, never removed.
/// </remarks>
public interface ITrail
{
    /// <summary>The name its traces carry, such as <c>file</c>.</summary>
    string Name { get; }

    /// <summary>Everything on the trail right now.</summary>
    Task<IReadOnlyList<Trace>> FollowAsync(IReadOnlyList<Rule> rules, CancellationToken cancellationToken);

    /// <summary>The trace with its size filled in, for the few that become discoveries.</summary>
    Task<Trace> MeasureAsync(Trace trace, CancellationToken cancellationToken);
}
