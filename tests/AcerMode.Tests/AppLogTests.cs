using AcerMode.Core;
using Xunit;

public class AppLogTests
{
    [Fact]
    public void RotatesWhenTooBig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "acermode-log-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var log = new AppLog(Path.Combine(dir, "events.log"), maxBytes: 200);
        for (int i = 0; i < 20; i++) log.Write("linha " + i);
        Assert.True(File.Exists(Path.Combine(dir, "events.1.log")));
        Assert.True(new FileInfo(log.Path).Length <= 400);
        Assert.EndsWith("linha 19", log.Tail(1)[0]);
        Directory.Delete(dir, true);
    }
}
