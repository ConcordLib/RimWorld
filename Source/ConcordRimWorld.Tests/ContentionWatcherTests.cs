using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

public class ContentionWatcherTests
{
    [Fact]
    public void OverlapTarget_WarnOnCheckpoint_DialogFiredOnce()
    {
        List<MethodBase> pinned = new List<MethodBase> {
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)
        };
        List<string> warnings = new List<string>();
        List<string> dialogs = new List<string>();
        bool dialogFiredOnce = false;

        Func<IReadOnlyCollection<MethodBase>> rawPinned = () => pinned.AsReadOnly();
        Func<MethodBase, IReadOnlyList<string>> foreignOwners = (m) => new List<string> { "test.owner" }.AsReadOnly();
        Action<string> warn = (msg) => warnings.Add(msg);
        Action<string> dialogOnce = (msg) => {
            if (!dialogFiredOnce) {
                dialogs.Add(msg);
                dialogFiredOnce = true;
            }
        };

        ContentionWatcher watcher = new ContentionWatcher(rawPinned, foreignOwners, warn, dialogOnce);

        watcher.RunCheckpoint();
        Assert.Single(warnings);
        Assert.Contains(CoexistenceLogMarkers.LateContention, warnings[0]);
        Assert.Contains("test.owner", warnings[0]);
        Assert.Contains("Concord injections on this method are not running", warnings[0]);
        Assert.Single(dialogs);

        watcher.RunCheckpoint();
        Assert.Equal(2, warnings.Count);
        Assert.Single(dialogs);
    }

    [Fact]
    public void NoOverlapTarget_Silent()
    {
        List<MethodBase> pinned = new List<MethodBase> {
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)
        };
        List<string> warnings = new List<string>();
        List<string> dialogs = new List<string>();

        Func<IReadOnlyCollection<MethodBase>> rawPinned = () => pinned.AsReadOnly();
        Func<MethodBase, IReadOnlyList<string>> foreignOwners = (m) => new List<string>().AsReadOnly();
        Action<string> warn = (msg) => warnings.Add(msg);
        Action<string> dialogOnce = (msg) => dialogs.Add(msg);

        ContentionWatcher watcher = new ContentionWatcher(rawPinned, foreignOwners, warn, dialogOnce);

        watcher.RunCheckpoint();
        Assert.Empty(warnings);
        Assert.Empty(dialogs);
    }

    private static void DummyMethod()
    {
    }
}
