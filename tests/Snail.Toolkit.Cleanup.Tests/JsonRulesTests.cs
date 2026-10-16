using Snail.MCP.CleanUp.Core.Adapters;
using Snail.MCP.CleanUp.Core.Domain;

namespace Snail.Toolkit.Cleanup.Tests;

public sealed class JsonRulesTests : IDisposable
{
    private readonly Scratch _scratch = new();

    public void Dispose() => _scratch.Dispose();

    /// <summary>The file is for a person to read and correct, so its shape is part of the contract.</summary>
    [Fact]
    public async Task Rule_Learned_IsWrittenAsAPersonWouldWriteIt()
    {
        var path = _scratch.File("rules.json");

        await new JsonRules(path, []).AddAsync(new Rule("file", "~/Library/Caches/thing", RuleAction.Cache) { Note = "rebuilds itself" }, CancellationToken.None);

        Assert.Equal(
            """
            {
              "rules": [
                {
                  "trail": "file",
                  "pattern": "~/Library/Caches/thing",
                  "action": "cache",
                  "note": "rebuilds itself"
                }
              ]
            }
            """.ReplaceLineEndings("\n"),
            (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task File_WrittenByHand_IsReadAfterTheShippedRules()
    {
        var path = _scratch.File("rules.json");
        var shipped = new Rule("file", "/shipped", RuleAction.Ignore);

        await File.WriteAllTextAsync(path, """{"rules":[{"trail":"process","pattern":"/opt/x/*","action":"Report","blockedWhileRunning":"x"}]}""");

        var rules = await new JsonRules(path, [shipped]).ListAsync(CancellationToken.None);

        Assert.Equal([shipped, new Rule("process", "/opt/x/*", RuleAction.Report) { BlockedWhileRunning = "x" }], rules);
    }

    [Fact]
    public async Task File_ThatIsNotJson_CountsAsNoLearnedRules()
    {
        var path = _scratch.File("rules.json");

        await File.WriteAllTextAsync(path, "{ not json");

        Assert.Empty(await new JsonRules(path, []).ListAsync(CancellationToken.None));
    }
}
