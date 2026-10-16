using System.Diagnostics;

namespace Snail.Toolkit.Cleanup.Tests.Support;

/// <summary>A folder of the test's own, removed when the test is done with it.</summary>
/// <remarks>
/// Named by the convention of Snail.Sdk's test folders, <c>s3l-test-&lt;pid&gt;-&lt;start&gt;-&lt;random&gt;</c> directly in
/// the temporary folder: when a run is killed before it removes the folder, the name proves to a cleaner that the
/// process which made it has ended. A folder named at random would prove nothing.
/// </remarks>
internal sealed class Scratch : IDisposable
{
    private static readonly Lazy<long> Started = new(StartOfThisProcess);

    public Scratch() => Directory.CreateDirectory(Path);

    public string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"s3l-test-{Environment.ProcessId}-{Started.Value}-{System.IO.Path.GetRandomFileName()[..8]}");

    public string File(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        if (Directory.Exists(Path))
            Directory.Delete(Path, recursive: true);
    }

    private static long StartOfThisProcess()
    {
        using var process = Process.GetCurrentProcess();

        return new DateTimeOffset(process.StartTime.ToUniversalTime()).ToUnixTimeSeconds();
    }
}
