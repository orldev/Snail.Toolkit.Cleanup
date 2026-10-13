namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>What ties a resource to its owner, weakest first.</summary>
/// <remarks>
/// Only the last four are proof. A resource that merely appeared while a session ran may as well be the
/// developer's own, which is why nothing below <see cref="Ancestry"/> is ever removed on ownership alone.
/// <para>
/// The order is the strength, and only that: a store keeps each value under a code of its own, so that a value added
/// between two others does not change what the rows written before it say.
/// </para>
/// </remarks>
public enum Evidence
{
    /// <summary>Nothing ties it to an agent.</summary>
    None,

    /// <summary>It appeared while a session ran: a coincidence of time, not of cause.</summary>
    Appeared,

    /// <summary>It was seen running under the agent's own process while the session was alive.</summary>
    Ancestry,

    /// <summary>Its environment carries the session: Claude Code gives every program it starts <c>CLAUDE_CODE_SESSION_ID</c>, and a program keeps it when it detaches.</summary>
    /// <remarks>
    /// Written by the agent when the program was made, like a label, but read from the program rather than given to it on
    /// purpose — and only from the programs of the user this server runs as, since another user's environment says nothing
    /// this server may act on.
    /// </remarks>
    Environment,

    /// <summary>Its own name or label carries the owner, written there when it was made.</summary>
    Label,

    /// <summary>It was made through this server and recorded at birth.</summary>
    Ledger
}

/// <summary>What the strength of evidence allows.</summary>
public static class EvidenceStrength
{
    /// <summary>Whether this evidence is enough to remove a resource on ownership alone.</summary>
    public static bool IsProof(this Evidence evidence) => evidence >= Evidence.Ancestry;
}
