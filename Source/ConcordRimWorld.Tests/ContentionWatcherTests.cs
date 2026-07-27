using Concord.Detour;
using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;
using Concord.RimWorld;

namespace Concord.RimWorld.Tests;

public class ContentionWatcherTests
{
    [Fact]
    public void HookMissing_OverlapTarget_WarnOnCheckpoint_DialogFiredOnce()
    {
        List<MethodBase> pinned = new List<MethodBase> {
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)
        };
        List<string> warnings = new List<string>();
        List<string> dialogs = new List<string>();
        bool dialogFiredOnce = false;

        Action<string> dialogOnce = (msg) => {
            if (!dialogFiredOnce) {
                dialogs.Add(msg);
                dialogFiredOnce = true;
            }
        };

        ContentionWatcher watcher = NewWatcher(
            pinned, NoTargets(), (m) => new List<string> { "test.owner" }.AsReadOnly(),
            notifierInstalled: false, warnings.Add, dialogOnce);

        watcher.RunCheckpoint();
        Assert.Single(warnings);
        Assert.Contains(CoexistenceLogMarkers.LateContention, warnings[0]);
        Assert.Contains("test.owner", warnings[0]);
        Assert.Contains("Concord injections on this method are not running", warnings[0]);
        Assert.Contains("the notifier hook is not installed", warnings[0]);
        Assert.Single(dialogs);

        watcher.RunCheckpoint();
        Assert.Equal(2, warnings.Count);
        Assert.Single(dialogs);
    }

    [Fact]
    public void HookInstalled_RawOverlapTarget_Silent()
    {
        List<MethodBase> pinned = new List<MethodBase> {
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)
        };
        List<string> warnings = new List<string>();
        List<string> dialogs = new List<string>();

        // With the hook installed a contested raw target would have been promoted, so a foreign owner
        // showing up here is not something the watcher should shout about.
        ContentionWatcher watcher = NewWatcher(
            pinned, NoTargets(), (m) => new List<string> { "test.owner" }.AsReadOnly(),
            notifierInstalled: true, warnings.Add, dialogs.Add);

        watcher.RunCheckpoint();

        Assert.Empty(warnings);
        Assert.Empty(dialogs);
    }

    [Fact]
    public void HookInstalled_ContestedLostTarget_IsStillReported()
    {
        List<MethodBase> lost = new List<MethodBase> {
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static)
        };
        List<string> warnings = new List<string>();
        List<string> dialogs = new List<string>();

        ContentionWatcher watcher = NewWatcher(
            new List<MethodBase>(), lost, (m) => new List<string> { "test.owner" }.AsReadOnly(),
            notifierInstalled: true, warnings.Add, dialogs.Add);

        watcher.RunCheckpoint();

        Assert.Single(warnings);
        Assert.Contains("could not hand its injections", warnings[0]);
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

        ContentionWatcher watcher = NewWatcher(
            pinned, NoTargets(), (m) => new List<string>().AsReadOnly(),
            notifierInstalled: false, warnings.Add, dialogs.Add);

        watcher.RunCheckpoint();
        Assert.Empty(warnings);
        Assert.Empty(dialogs);
    }

    [Fact]
    public void ThrowingLookupForOneTarget_StillProcessesRemainingTargets()
    {
        List<MethodBase> pinned = new List<MethodBase> {
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethod), BindingFlags.NonPublic | BindingFlags.Static),
            typeof(ContentionWatcherTests).GetMethod(nameof(DummyMethodTwo), BindingFlags.NonPublic | BindingFlags.Static)
        };
        List<string> warnings = new List<string>();
        List<string> dialogs = new List<string>();

        Func<MethodBase, IReadOnlyList<string>> foreignOwners = (m) => {
            if (m.Name == nameof(DummyMethod)) {
                throw new InvalidOperationException("lookup failed");
            }

            return new List<string> { "test.owner" }.AsReadOnly();
        };

        ContentionWatcher watcher = NewWatcher(
            pinned, NoTargets(), foreignOwners, notifierInstalled: false, warnings.Add, dialogs.Add);

        watcher.RunCheckpoint();

        Assert.Equal(2, warnings.Count);
        Assert.Contains(warnings, w => w.Contains("lookup failed"));
        Assert.Contains(warnings, w => w.Contains("test.owner"));
        Assert.Single(dialogs);
    }

    private static List<MethodBase> NoTargets()
    {
        return new List<MethodBase>();
    }

    private static ContentionWatcher NewWatcher(
        List<MethodBase> pinned,
        List<MethodBase> lost,
        Func<MethodBase, IReadOnlyList<string>> foreignOwners,
        bool notifierInstalled,
        Action<string> warn,
        Action<string> dialogOnce)
    {
        return new ContentionWatcher(
            () => pinned.AsReadOnly(),
            () => lost.AsReadOnly(),
            foreignOwners,
            () => notifierInstalled,
            warn,
            dialogOnce);
    }

    private static void DummyMethodTwo()
    {
    }

    private static void DummyMethod()
    {
    }
}
