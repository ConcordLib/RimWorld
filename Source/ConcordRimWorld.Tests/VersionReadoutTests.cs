using System;
using System.Reflection;
using Xunit;

namespace Concord.RimWorld.Tests;

public sealed class VersionReadoutTests {
    [Fact]
    public void Line_NamesBothAssemblies() {
        string line = (string)typeof(VersionReadout)
            .GetProperty("Line", BindingFlags.NonPublic | BindingFlags.Static)
            .GetValue(null);

        Assert.StartsWith("Concord v", line, StringComparison.Ordinal);
        Assert.Contains(" (v", line, StringComparison.Ordinal);
        Assert.EndsWith(")", line, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown", line, StringComparison.Ordinal);
    }
}
