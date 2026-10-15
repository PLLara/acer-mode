using AcerMode.Core;
using Xunit;

public class AcerWmiWorkerTests
{
    [Fact]
    public async Task HungCallTimesOut_NextCallsStillRun_AfterBackoff()
    {
        var clock = 0L;
        using var w = new SerialWorker(() => clock, backoffMs: 30_000);
        var never = new ManualResetEventSlim(false);

        await Assert.ThrowsAsync<HardwareException>(() => w.Run<int>(() => { never.Wait(); return 1; }, TimeSpan.FromMilliseconds(100)));

        // Durante a pausa, falha imediatamente sem tocar a BIOS.
        await Assert.ThrowsAsync<HardwareException>(() => w.Run(() => 2, TimeSpan.FromSeconds(1)));

        clock += 30_001;
        Assert.Equal(3, await w.Run(() => 3, TimeSpan.FromSeconds(1)));
        never.Set();
    }

    [Fact]
    public async Task CallsAreSerialized()
    {
        using var w = new SerialWorker(() => 0, backoffMs: 30_000);
        int inside = 0, maxInside = 0;
        var tasks = Enumerable.Range(0, 20).Select(_ => w.Run(() =>
        {
            var n = Interlocked.Increment(ref inside);
            maxInside = Math.Max(maxInside, n);
            Thread.Sleep(2);
            Interlocked.Decrement(ref inside);
            return 0;
        }, TimeSpan.FromSeconds(5)));
        await Task.WhenAll(tasks);
        Assert.Equal(1, maxInside);
    }
}
