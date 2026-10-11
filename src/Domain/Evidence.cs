namespace Snail.MCP.CleanUp.Core.Domain;

/// <summary>What ties a resource to its owner, weakest first.</summary>
/// <remarks>
/// Only the last three are proof. A resource that merely appeared while a session ran may as well be the
/// developer's own, which is why nothing below <see cref="Ancestry"/> is ever removed on ownership alone.
/// </remarks>
public enum Evidence
{
    /// <summary>Nothing ties it to an agent.</summary>
    None,

    /// <summary>It appeared while a session ran: a coincidence of time, not of cause.</summary>
    Appeared,

    /// <summary>It was seen running under the agent's own process while the session was alive.</summary>
    Ancestry,

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
