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
  public static string[] LastDirectories;
  public static int InstallCalls;

  private static readonly string runtimeBuild = ModLayout.BuildSha(typeof(Patcher).Assembly);

  [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "xUnit1013", Justification = "Emitted fixture IL calls this across assemblies, so it has to stay public and is not a test.")]
  public static void Record(Action<string> log, string[] assemblyDirectories) {
    InstallCalls++;
    LastLog = log;
    LastDirectories = assemblyDirectories;
  }

  [Fact]
  public void MissingBackendWarnsThatConcordWillNotPatch() {
    string root = NewRoot();
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void PresentBackendGetsLogSinkAndAssemblyDirectories() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Good", Fixture.Installs, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();
    string[] directories = ["/one", "/two"];
    InstallCalls = 0;
    LastLog = null;
    LastDirectories = null;

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => directories);

    Assert.True(installed);
    Assert.Empty(warnings);
    Assert.Equal(1, InstallCalls);
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
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void UnusableModRootIsSwallowedSoWiringStillRuns() {
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = DetourBackendLoader.TryInstall(null, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
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
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void BackendThatRefusesThisRuntimeWarnsThatConcordWillNotPatch() {
    string root = NewRoot();
    WriteFixture(root, "Fixture.Backend.Refuses", Fixture.Refuses, runtimeBuild);
    List<string> log = new List<string>();
    List<string> warnings = new List<string>();

    bool installed = DetourBackendLoader.TryInstall(root, log.Add, warnings.Add, () => []);

    Assert.False(installed);
    Assert.Single(warnings);
    Assert.Contains("fixture backend refused this runtime", warnings[0]);
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
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
    Assert.Contains("will not patch", warnings[0], StringComparison.OrdinalIgnoreCase);
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
    Assert.Empty(warnings);
    Assert.Single(log);
    Assert.Contains(DetourBackendLoader.RuntimeBuildKey, log[0]);
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
    Assembly inMemory = AssemblyBuilder.DefineDynamicAssembly(
        new AssemblyName("Fixture.InMemory"),
        AssemblyBuilderAccess.Run);

    Assert.Empty(DetourBackendLoader.AssemblyDirectories(new Assembly[] { null, inMemory }));
  }

  private static string BackendPath(string modRoot) {
    return Path.Combine(
        modRoot,
        ModLayout.VersionDir,
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
    Refuses,
    Throws,
    WrongSignature
  }

  private static void WriteFixture(string modRoot, string assemblyName, Fixture kind, string concordBuild = null) {
    string path = BackendPath(modRoot);
    string dir = Path.GetDirectoryName(path);
    string fileName = Path.GetFileName(path);
    Directory.CreateDirectory(dir);

    PersistedAssemblyBuilder assembly = new PersistedAssemblyBuilder(
        new AssemblyName(assemblyName),
        typeof(object).Assembly);

    if (concordBuild != null) {
      assembly.SetCustomAttribute(new CustomAttributeBuilder(
          typeof(AssemblyMetadataAttribute).GetConstructor([typeof(string), typeof(string)]),
          [DetourBackendLoader.RuntimeBuildKey, concordBuild]));
    }

    ModuleBuilder module = assembly.DefineDynamicModule(fileName);
    ConstructorInfo selectionCtor = DefineFixtureSelection(module);

    TypeBuilder type = module.DefineType(
        "Concord.Host.Adapter.AdapterEntryPoint",
        TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
    MethodBuilder install = type.DefineMethod(
        "Install",
        MethodAttributes.Public | MethodAttributes.Static,
        kind == Fixture.WrongSignature ? typeof(void) : typeof(object),
        kind == Fixture.WrongSignature
            ? []
            : [typeof(Action<string>), typeof(string[])]);
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
      il.Emit(OpCodes.Call, typeof(DetourBackendLoaderTests).GetMethod(nameof(Record)));
      il.Emit(kind == Fixture.Refuses ? OpCodes.Ldc_I4_0 : OpCodes.Ldc_I4_1);
      il.Emit(OpCodes.Ldstr, kind == Fixture.Refuses ? "fixture backend refused this runtime" : "fixture backend installed");
      il.Emit(OpCodes.Newobj, selectionCtor);
      il.Emit(OpCodes.Ret);
    }

    type.CreateType();
    assembly.Save(path);
  }

  private static ConstructorInfo DefineFixtureSelection(ModuleBuilder module) {
    TypeBuilder selection = module.DefineType("Fixture.Selection", TypeAttributes.Public);
    FieldBuilder waylith = selection.DefineField("waylith", typeof(bool), FieldAttributes.Private);
    FieldBuilder message = selection.DefineField("message", typeof(string), FieldAttributes.Private);

    ConstructorBuilder ctor = selection.DefineConstructor(
        MethodAttributes.Public,
        CallingConventions.Standard,
        [typeof(bool), typeof(string)]);
    ILGenerator ctorIl = ctor.GetILGenerator();
    ctorIl.Emit(OpCodes.Ldarg_0);
    ctorIl.Emit(OpCodes.Ldarg_1);
    ctorIl.Emit(OpCodes.Stfld, waylith);
    ctorIl.Emit(OpCodes.Ldarg_0);
    ctorIl.Emit(OpCodes.Ldarg_2);
    ctorIl.Emit(OpCodes.Stfld, message);
    ctorIl.Emit(OpCodes.Ret);

    DefineReadOnlyProperty(selection, "Waylith", typeof(bool), waylith);
    DefineReadOnlyProperty(selection, "Message", typeof(string), message);

    selection.CreateType();
    return ctor;
  }

  private static void DefineReadOnlyProperty(TypeBuilder type, string name, Type propertyType, FieldBuilder backingField) {
    PropertyBuilder property = type.DefineProperty(name, PropertyAttributes.None, propertyType, null);
    MethodBuilder getter = type.DefineMethod(
        "get_" + name,
        MethodAttributes.Public | MethodAttributes.SpecialName,
        propertyType,
        null);
    ILGenerator getterIl = getter.GetILGenerator();
    getterIl.Emit(OpCodes.Ldarg_0);
    getterIl.Emit(OpCodes.Ldfld, backingField);
    getterIl.Emit(OpCodes.Ret);
    property.SetGetMethod(getter);
  }
}
