namespace Snail.Toolkit.Cleanup.Domain;

/// <summary>What the ledger knows about one resource: who holds it, since when, and how that is known.</summary>
/// <param name="ResourceId">The <see cref="Resource.Id"/> it is about.</param>
/// <param name="Owner">Who holds it.</param>
/// <param name="Evidence">How that was established.</param>
/// <param name="RecordedAt">When the ledger learned of it.</param>
/// <param name="Title">What a person reads to recognise it, kept for when it is gone.</param>
public sealed record Holding(string ResourceId, Owner Owner, Evidence Evidence, DateTimeOffset RecordedAt, string Title)
{
    /// <summary>Why it is to be left alone, when somebody asked for that.</summary>
    public string? KeptBecause { get; init; }

    /// <summary>Whether somebody asked for it to be left alone.</summary>
    public bool IsKept => KeptBecause is not null;
}
