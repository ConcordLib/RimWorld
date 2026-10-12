using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Xunit;

namespace Concord.RimWorld.Tests;

[Collection(GameAssemblies.Name)]
public class DetourBackendLoaderTests {
  public static Action<string> LastLog;
  public static bool? LastFlag;
  public static string[] LastDirectories;
  public static int InstallCalls;

  private static readonly string runtimeBuild = HarmonyProbe.BuildSha(typeof(Patcher).Assembly);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1013", Justification = "Emitted fixture IL calls this across assemblies, so it has to stay public and is not a test.")]
  public static void Record(Action<string> log, bool? enabled, string[] assemblyDirectories) {
    InstallCalls++;
    LastLog = log;
    LastFlag = enabled;
    LastDirectories = assemblyDirectories;
  }

  [Fact]
  public void MissingBackendIsQuiet() {
    string root = NewRoot();
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Empty(log);
    Assert.Empty(warnings);
  }

  [Fact]
  public void PresentBackendGetsLogSinkNullFlagAndAssemblyDirectories() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Good", Fixture.Installs, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    string[] directories = ["/one", "/two"];
    InstallCalls = 0;
    LastLog = null;
    LastFlag = true;
    LastDirectories = null;

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => directories);

    Assert.True(installed);
    Assert.Empty(warnings);
    Assert.Equal(1, InstallCalls);
    Assert.Null(LastFlag);
    Assert.Equal(directories, LastDirectories);
    Assert.NotNull(LastLog);
    LastLog("from the backend");
    Assert.Contains("from the backend", log);
  }

  [Fact]
  public void ThrowingBackendIsSwallowedSoWiringStillRuns() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Bad", Fixture.Throws, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    bool reachedWiring = false;

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);
    reachedWiring = true;

    Assert.False(installed);
    Assert.True(reachedWiring);
    Assert.Single(warnings);
    Assert.Contains(BackendPath(root), warnings[0]);
  }

  [Fact]
  public void UnusableModRootIsSwallowedSoWiringStillRuns() {
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = DetourBackendLoader.TryInstall(null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
  }

  [Fact]
  public void BackendWithoutMatchingEntryPointIsReported() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.WrongSignature", Fixture.WrongSignature, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("no matching entry point", warnings[0]);
  }

  [Fact]
  public void BackendBuiltAgainstAnotherRuntimeIsRefused() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Mismatched", Fixture.Installs, "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f");
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    InstallCalls = 0;

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Equal(0, InstallCalls);
    Assert.Single(warnings);
    Assert.Contains("0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", warnings[0]);
    Assert.Contains(runtimeBuild, warnings[0]);
  }

  [Fact]
  public void BackendWithNoBuildStampSaysSoAndStillInstalls() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Unstamped", Fixture.Installs);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    InstallCalls = 0;

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);

    Assert.True(installed);
    Assert.Equal(1, InstallCalls);
    Assert.Single(warnings);
    Assert.Contains(DetourBackendLoader.RuntimeBuildKey, warnings[0]);
  }

  [Fact]
  public void AssemblyDirectoriesMapsEveryAssemblyToItsDirectoryAndDedupes() {
    Assembly self = typeof(DetourBackendLoaderTests).Assembly;
    Assembly runtime = typeof(object).Assembly;
    string selfDir = Path.GetDirectoryName(Path.GetFullPath(self.Location));
    string runtimeDir = Path.GetDirectoryName(Path.GetFullPath(runtime.Location));

    string[] dirs = DetourBackendLoader.AssemblyDirectories(new[] { self, self, runtime });

    Assert.NotEqual(selfDir, runtimeDir);
    Assert.Contains(selfDir, dirs);
    Assert.Contains(runtimeDir, dirs);
    Assert.Equal(2, dirs.Length);
  }

  [Fact]
  public void AssemblyDirectoriesSkipsAssembliesWithNoFileOnDisk() {
#if NETFRAMEWORK
    Assembly inMemory = AppDomain.CurrentDomain.DefineDynamicAssembly(
        new AssemblyName("Fixture.InMemory"),
        AssemblyBuilderAccess.Run);
#else
    Assembly inMemory = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("Fixture.InMemory"),
        AssemblyBuilderAccess.Run);
#endif

    Assert.Empty(DetourBackendLoader.AssemblyDirectories(new Assembly[] { null, inMemory }));
  }

  private static string BackendPath(string modRoot) {
    return Path.Combine(
        modRoot,
        HarmonyProbe.VersionDir,
        "Backend",
        DetourBackendLoader.BackendFileName);
  }

  private static string NewRoot() {
    string root = Path.Combine(Path.GetTempPath(), "concord-backend-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }

  private enum Fixture {
    Installs,
    Throws,
    WrongSignature
  }

  private static void WriteFixture(string modRoot, string assemblyName, Fixture kind, string concordBuild = null) {
    string path = BackendPath(modRoot);
    string dir = Path.GetDirectoryName(path);
    string fileName = Path.GetFileName(path);
    Directory.CreateDirectory(dir);

#if NETFRAMEWORK
    AssemblyBuilder assembly = AppDomain.CurrentDomain.DefineDynamicAssembly(
        new AssemblyName(assemblyName),
        AssemblyBuilderAccess.Save,
        dir);
#else
    PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(
        new AssemblyName(assemblyName),
        typeof(object).Assembly);
#endif

    if (concordBuild != null) {
      assembly.SetCustomAttribute(new CustomAttributeBuilder(
          typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]),
          [DetourBackendLoader.RuntimeBuildKey, concordBuild]));
    }

#if NETFRAMEWORK
    ModuleBuilder module = assembly.DefineDynamicModule(fileName, fileName);
#else
    ModuleBuilder module = assembly.DefineDynamicModule(fileName);
#endif
    TypeBuilder type = module.DefineType(
        "Concord.Host.Adapter.AdapterEntryPoint",
        TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    MethodBuilder install = type.DefineMethod(
        "Install",
        MethodAttributes.Public | MethodAttributes.Static,
        typeof(void),
        kind == Fixture.WrongSignature
            ? []
            : [typeof(Action<string>), typeof(bool?), typeof(string[])]);
    ILGenerator il = install.GetILGenerator();

    if (kind == Fixture.Throws) {
      il.Emit(OpCodes.Ldstr, "fixture backend refuses to install");
      il.Emit(OpCodes.Newobj, typeof(InvalidOperationException).GetConstructor([typeof(string)]));
      il.Emit(OpCodes.Throw);
    } else if (kind == Fixture.WrongSignature) {
      il.Emit(OpCodes.Ret);
    } else {
      il.Emit(OpCodes.Ldarg_0);
      il.Emit(OpCodes.Ldarg_1);
      il.Emit(OpCodes.Ldarg_2);
      il.Emit(OpCodes.Call, typeof(DetourBackendLoaderTests).GetMethod(nameof(Record)));
      il.Emit(OpCodes.Ret);
    }

    type.CreateType();
    assembly.Save(
#if NETFRAMEWORK
        fileName);
#else
            path);
#endif
  }
}
