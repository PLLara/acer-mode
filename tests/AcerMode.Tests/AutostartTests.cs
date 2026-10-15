using AcerMode.Core;
using Xunit;

public class AutostartTests
{
    const string Xml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task><Actions Context="Author"><Exec><Command>C:\Users\pedro\AcerMode\AcerMode.exe</Command><Arguments>--tray</Arguments></Exec></Actions></Task>
        """;

    [Fact] public void ParseCommand_ReadsExePath() => Assert.Equal(@"C:\Users\pedro\AcerMode\AcerMode.exe", Autostart.ParseCommand(Xml));
    [Fact] public void ParseCommand_NoCommand_IsNull() => Assert.Null(Autostart.ParseCommand("<Task/>"));

    [Fact] public void ExistingTarget_IsKept() =>
        Assert.False(Autostart.ShouldRefresh(@"C:\a\AcerMode.exe", @"C:\dev\bin\AcerMode.exe", _ => true));

    [Fact] public void MissingTarget_IsRepointed() =>
        Assert.True(Autostart.ShouldRefresh(@"C:\old\AcerMode.exe", @"C:\a\AcerMode.exe", _ => false));

    [Fact] public void SameTarget_NeedsNothing() =>
        Assert.False(Autostart.ShouldRefresh(@"C:\a\AcerMode.exe", @"c:\A\acermode.exe", _ => false));

    [Fact] public void UnknownTarget_IsLeftAlone() =>
        Assert.False(Autostart.ShouldRefresh(null, @"C:\a\AcerMode.exe", _ => false));
}
