using Snail.Toolkit.Cleanup.Domain;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class BaselineTests
{
    /// <summary>A host that stores a baseline as one value reads back what it wrote: every id it began with, and no other.</summary>
    [Fact]
    public void Bytes_ReadBack_HoldTheSameIds()
    {
        var baseline = new Baseline();

        baseline.Add("file:/tmp/a");
        baseline.Add("process:7@1");

        var read = Baseline.FromBytes(baseline.ToBytes());

        Assert.Equal(2, read.Count);
        Assert.True(read.Contains("file:/tmp/a"));
        Assert.True(read.Contains("process:7@1"));
        Assert.False(read.Contains("file:/tmp/b"));
    }

    [Fact]
    public void Bytes_OfAnEmptyBaseline_AreNone() =>
        Assert.Empty(new Baseline().ToBytes());

    /// <summary>A value cut short, by a disk that filled or a writer of before, loses only the hash it cut, never the read.</summary>
    [Fact]
    public void Bytes_CutInTheMiddleOfAHash_KeepTheWholeOnes()
    {
        var baseline = new Baseline();

        baseline.Add("file:/tmp/a");
        baseline.Add("file:/tmp/b");

        var bytes = baseline.ToBytes();
        var read = Baseline.FromBytes(bytes.AsSpan(0, bytes.Length - 3));

        Assert.Equal(1, read.Count);
    }
}
