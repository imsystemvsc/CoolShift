using CoolShift;
using Xunit;

namespace CoolShift.Tests;

public sealed class GameDetectionTests
{
    [Fact]
    public void ClassifiesOnlyPathsInsideKnownGameDirectories()
    {
        var catalog = new GameInstallationCatalog(new[] { @"C:\Games\Steam\steamapps\common\Example Game" });

        Assert.True(catalog.IsGameExecutable(@"C:\Games\Steam\steamapps\common\Example Game\bin\game.exe", Array.Empty<string>()));
        Assert.False(catalog.IsGameExecutable(@"C:\Games\Steam\steam.exe", Array.Empty<string>()));
        Assert.False(catalog.IsGameExecutable(@"C:\Tools\game.exe", Array.Empty<string>()));
    }

    [Fact]
    public void ExclusionsOverrideInstalledGamePath()
    {
        var catalog = new GameInstallationCatalog(new[] { @"C:\Games\Example" });

        Assert.False(catalog.IsGameExecutable(@"C:\Games\Example\game.exe", new[] { "game.exe" }));
        Assert.True(GameInstallationCatalog.IsExcluded(@"C:\Games\Example\crashreporter.exe", Array.Empty<string>()));
        Assert.True(GameInstallationCatalog.IsExcluded(@"C:\Program Files\CoolShift\CoolShift.exe", Array.Empty<string>()));
    }

    [Fact]
    public void StartupDetectionWaitsFiveSecondsBeforeAutomaticActivation()
    {
        var tracker = new GameActivationTracker();
        var game = new DetectedGame("42", "Example Game", false);
        var startup = DateTimeOffset.Parse("2026-08-27T12:00:00Z");

        Assert.Equal(GameAutomationAction.None, tracker.Update(startup, null, game, false));
        Assert.Equal(GameAutomationAction.None, tracker.Update(startup.AddSeconds(4), null, game, false));
        Assert.Equal(GameAutomationAction.ActivateAlwaysOn, tracker.Update(startup.AddSeconds(5), null, game, false));
        Assert.Equal("Example Game", tracker.ActiveGameName);
    }

    [Fact]
    public void ManualGameHasPriorityAndActivatesImmediately()
    {
        var tracker = new GameActivationTracker();
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");

        var action = tracker.Update(now, new DetectedGame("1", "Manual Game", true), new DetectedGame("2", "Automatic Game", false), false);

        Assert.Equal(GameAutomationAction.ActivateAlwaysOn, action);
        Assert.Equal("Manual Game", tracker.ActiveGameName);
    }

    [Fact]
    public void DoesNotActivateAlwaysOnWhileOnBattery()
    {
        var tracker = new GameActivationTracker();
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
        var game = new DetectedGame("42", "Example Game", false);

        Assert.Equal(GameAutomationAction.None, tracker.Update(now, null, game, true));
        Assert.Equal(GameAutomationAction.None, tracker.Update(now.AddSeconds(10), null, game, true));
    }

    [Fact]
    public void RestoresCoolIdleTenSecondsAfterLastGameCloses()
    {
        var tracker = new GameActivationTracker();
        var now = DateTimeOffset.Parse("2026-08-27T12:00:00Z");
        var game = new DetectedGame("42", "Example Game", false);
        tracker.Update(now, null, game, false);
        tracker.Update(now.AddSeconds(5), null, game, false);

        Assert.Equal(GameAutomationAction.None, tracker.Update(now.AddSeconds(6), null, null, false));
        Assert.Equal(GameAutomationAction.None, tracker.Update(now.AddSeconds(15), null, null, false));
        Assert.Equal(GameAutomationAction.RestoreCoolIdle, tracker.Update(now.AddSeconds(16), null, null, false));
    }
}
