using System;
using System.IO;
#if RW_1_7
using System.Reflection;
#endif
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

#if RW_1_7
  [Fact]
  public void Staged_Backend_Directory_Has_The_Body_Replacement_Backend() {
    string staged = Path.Combine(ModRoot(), "1.7", "Backend", DetourBackendLoader.BackendFileName);

    Assert.True(File.Exists(staged));
  }

  [Fact]
  public void Staged_Backend_Is_Stamped_With_The_Runtime_Build_It_Ships_Beside() {
    string staged = Path.Combine(ModRoot(), "1.7", "Backend", DetourBackendLoader.BackendFileName);
    string stamped = null;

    foreach (AssemblyMetadataAttribute stamp in
        Assembly.LoadFrom(staged).GetCustomAttributes<AssemblyMetadataAttribute>()) {
      if (stamp.Key == DetourBackendLoader.RuntimeBuildKey) {
        stamped = stamp.Value;
      }
    }

    Assert.Equal(HarmonyProbe.BuildSha(typeof(Patcher).Assembly), stamped);
  }
#endif

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
