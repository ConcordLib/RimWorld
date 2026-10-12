using System;
using System.IO;
using Xunit;

namespace Concord.RimWorld.Tests;

public class ModLayoutTests {
  [Fact]
  public void No_LoadFolders_Shadows_The_Versioned_Folder_Scan() {
    Assert.False(File.Exists(Path.Combine(ModRoot(), "LoadFolders.xml")));
  }

  [Fact]
  public void The_Version_This_Build_Targets_Is_A_Supported_Version() {
    string about = File.ReadAllText(Path.Combine(ModRoot(), "About", "About.xml"));

    Assert.Contains("<li>" + HarmonyProbe.VersionDir + "</li>", about);
  }

  private static string ModRoot() {
    string current = AppContext.BaseDirectory;

    while (current != null) {
      if (File.Exists(Path.Combine(current, "About", "About.xml"))) {
        return current;
      }

      current = Path.GetDirectoryName(current);
    }

    throw new InvalidOperationException("Could not resolve the mod root from the test assembly location.");
  }
}
