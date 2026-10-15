using AcerMode.Core;
using Xunit;

public class LatestWinsTests
{
    [Fact]
    public async Task RunsCurrentThenOnlyLatest()
    {
        var ran = new List<int>();
        var gate = new TaskCompletionSource();
        var lw = new LatestWins<int>(async v => { ran.Add(v); if (v == 1) await gate.Task; });

        var first = lw.PostAsync(1);
        await lw.PostAsync(2);
        await lw.PostAsync(3);
        gate.SetResult();
        await first;

        Assert.Equal(new[] { 1, 3 }, ran);
    }

    [Fact]
    public async Task ErrorDoesNotStopLaterPosts()
    {
        var errors = 0;
        var ran = new List<int>();
        var lw = new LatestWins<int>(v => { ran.Add(v); return v == 1 ? Task.FromException(new Exception("x")) : Task.CompletedTask; });
        lw.Error += _ => errors++;
        await lw.PostAsync(1);
        await lw.PostAsync(2);
        Assert.Equal(1, errors);
        Assert.Equal(new[] { 1, 2 }, ran);
    }
}
