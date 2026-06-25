using System.IO.Compression;
using System.Text;
using System.Text.Json;
using AgentX.Core.Constants;
using AgentX.Core.Data;
using AgentX.Core.Data.Entities;
using AgentX.Core.Services.Inbox;
using AgentX.Core.Services.OAuth;
using AgentX.Core.Services.Plugins;
using AgentX.Core.Validation;
using AgentX.Tests.Helpers;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Serilog;
using Xunit;

namespace AgentX.Tests.Services.Plugins;

/// <summary>
/// End-to-end coverage for <see cref="PluginService"/>. Each test drives the real service
/// against an in-memory SQLite <see cref="AgentXDbContext"/> (so the plugin-record lifecycle is
/// persisted for real) and the real <see cref="PluginManifestValidator"/>, with the host service
/// graph (<see cref="IOAuthService"/> / <see cref="IInboxService"/>) provided through a real
/// <see cref="ServiceProvider"/>.
///
/// The assembly-isolation path is exercised against genuinely-loadable plugin DLLs compiled
/// in-process with Roslyn (<see cref="TestPluginAssemblies"/>): install → enable loads each DLL
/// into a collectible <see cref="System.Runtime.Loader.AssemblyLoadContext"/>, discovers the
/// <see cref="IPlugin"/> type, instantiates it, and runs its Initialize/Activate/Deactivate
/// lifecycle. Failure variants (no plugin type, multiple types, throwing ctor/Initialize/Activate,
/// corrupt image) cover every guarded branch in <c>EnablePluginAsync</c>.
///
/// Installs write under <c>%LocalAppData%\AgentX\Plugins\</c>; every test uses a unique
/// reverse-DNS plugin id and the harness deletes the directories it created on dispose (with a
/// GC-assisted retry, because a loaded assembly keeps its file handle until the ALC is collected).
/// </summary>
public sealed class PluginServiceTests
{
    // ─────────────────────────────────────────────────────────────────────
    // Harness
    // ─────────────────────────────────────────────────────────────────────

    private sealed class PluginHarness : IDisposable
    {
        public TestDbContextFactory DbFactory { get; } = new();
        public AgentXDbContext Db { get; }
        public PluginManifestValidator Validator { get; } = new();
        public Mock<IOAuthService> OAuth { get; } = new();
        public Mock<IInboxService> Inbox { get; } = new();
        public ServiceProvider RootProvider { get; }
        public PluginService Service { get; }

        /// <summary>%LocalAppData%\AgentX\Plugins — computed exactly as the service does.</summary>
        public string PluginBaseDir { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AgentX", "Plugins");

        private readonly List<string> _cleanupDirs = new();
        private readonly List<string> _cleanupFiles = new();

        public PluginHarness(bool registerInbox = false)
        {
            Db = DbFactory.CreateContext();

            var services = new ServiceCollection();
            services.AddSingleton(OAuth.Object);
            if (registerInbox)
                services.AddSingleton(Inbox.Object);
            RootProvider = services.BuildServiceProvider();

            // Silent Serilog logger — no sinks configured, so log calls are no-ops.
            ILogger logger = new LoggerConfiguration().CreateLogger();
            Service = new PluginService(Db, RootProvider, Validator, logger);
        }

        /// <summary>A unique, reverse-DNS-valid (so the manifest validator accepts it) plugin id.</summary>
        public static string NewPluginId() => "test.cov." + Guid.NewGuid().ToString("N");

        /// <summary>The install path the service will derive for <paramref name="pluginId"/>, registered for cleanup.</summary>
        public string InstallDirFor(string pluginId)
        {
            var dir = Path.Combine(PluginBaseDir, pluginId);
            _cleanupDirs.Add(dir);
            return dir;
        }

        /// <summary>A fresh temp directory outside the plugin base, registered for cleanup.</summary>
        public string NewExternalDir()
        {
            var dir = Path.Combine(Path.GetTempPath(), "axplugintest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            _cleanupDirs.Add(dir);
            return dir;
        }

        /// <summary>A fresh context over the same in-memory database, for arrange/assert isolation.</summary>
        public AgentXDbContext Fresh() => DbFactory.CreateContext();

        /// <summary>
        /// Builds a <c>.agentx-plugin</c> ZIP package in a temp file and returns its path.
        /// Pass <paramref name="rawManifestJson"/> to write a verbatim (possibly malformed) manifest,
        /// otherwise <paramref name="manifest"/> is serialized. Set <paramref name="includeManifest"/>
        /// false to omit the manifest entry entirely.
        /// </summary>
        public string CreatePackage(
            PluginManifest? manifest,
            IReadOnlyList<(string name, byte[] bytes)>? files = null,
            string? rawManifestJson = null,
            bool includeManifest = true)
        {
            var path = Path.Combine(Path.GetTempPath(), "axpkg_" + Guid.NewGuid().ToString("N") + ".agentx-plugin");
            _cleanupFiles.Add(path);

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

            if (includeManifest)
            {
                var entry = zip.CreateEntry("manifest.json");
                using var writer = new StreamWriter(entry.Open());
                writer.Write(rawManifestJson ?? JsonSerializer.Serialize(manifest));
            }

            if (files is not null)
            {
                foreach (var (name, bytes) in files)
                {
                    var e = zip.CreateEntry(name);
                    using var s = e.Open();
                    s.Write(bytes, 0, bytes.Length);
                }
            }

            return path;
        }

        /// <summary>
        /// Seeds an on-disk install directory and a matching DB row WITHOUT going through install —
        /// used to drive enable/uninstall branches directly. Returns the surrogate entity id.
        /// </summary>
        public long SeedInstalled(
            string pluginId,
            string installPath,
            bool isEnabled = false,
            string? manifestJson = null,
            IReadOnlyList<(string name, byte[] bytes)>? files = null,
            bool createDir = true)
        {
            if (createDir)
                Directory.CreateDirectory(installPath);
            if (manifestJson is not null)
                File.WriteAllText(Path.Combine(installPath, "manifest.json"), manifestJson);
            if (files is not null)
                foreach (var (name, bytes) in files)
                    File.WriteAllBytes(Path.Combine(installPath, name), bytes);

            using var ctx = DbFactory.CreateContext();
            var entity = new PluginEntity
            {
                PluginId = pluginId,
                Name = "Seeded",
                Version = "1.0.0",
                Author = "Tester",
                Description = "seeded",
                PluginType = "Custom",
                InstallPath = installPath,
                IsEnabled = isEnabled,
                InstalledAt = DateTime.UtcNow,
            };
            ctx.Plugins.Add(entity);
            ctx.SaveChanges();
            return entity.Id;
        }

        public void Dispose()
        {
            foreach (var file in _cleanupFiles)
            {
                try { if (File.Exists(file)) File.Delete(file); }
                catch { /* best effort */ }
            }

            foreach (var dir in _cleanupDirs)
                DeleteDirWithRetry(dir);

            Db.Dispose();
            RootProvider.Dispose();
            DbFactory.Dispose();
        }

        // A loaded plugin assembly keeps its file mapped until the collectible ALC is collected;
        // a GC pass after Unload() releases the handle so the directory can be removed.
        private static void DeleteDirWithRetry(string dir)
        {
            for (var attempt = 0; attempt < 4 && Directory.Exists(dir); attempt++)
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    return;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch
                {
                    return; // give up quietly on any other error
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>A reference type satisfying <c>where T : class, IPlugin</c> that no test plugin implements.</summary>
    private interface ITestMarkerPlugin : IPlugin { }

    private static PluginManifest ValidManifest(
        string id,
        string entryAssembly = "plugin.dll",
        string? readme = null) => new()
        {
            Id = id,
            Name = "Test Plugin",
            Version = "1.0.0",
            Author = "Tester",
            Description = "A test plugin",
            PluginType = "Custom",
            EntryAssembly = entryAssembly,
            Readme = readme,
        };

    private static string ManifestJson(string id, string entryAssembly = "plugin.dll")
        => JsonSerializer.Serialize(ValidManifest(id, entryAssembly));

    // ─────────────────────────────────────────────────────────────────────
    // Constructor guards
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullDbContext_ThrowsArgumentNullException()
    {
        using var f = new TestDbContextFactory();
        var sp = new ServiceCollection().BuildServiceProvider();
        var act = () => new PluginService(null!, sp, new PluginManifestValidator(), new LoggerConfiguration().CreateLogger());
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        using var f = new TestDbContextFactory();
        var db = f.CreateContext();
        var act = () => new PluginService(db, null!, new PluginManifestValidator(), new LoggerConfiguration().CreateLogger());
        act.Should().Throw<ArgumentNullException>().WithParameterName("rootServiceProvider");
    }

    [Fact]
    public void Constructor_NullValidator_ThrowsArgumentNullException()
    {
        using var f = new TestDbContextFactory();
        var db = f.CreateContext();
        var sp = new ServiceCollection().BuildServiceProvider();
        var act = () => new PluginService(db, sp, null!, new LoggerConfiguration().CreateLogger());
        act.Should().Throw<ArgumentNullException>().WithParameterName("manifestValidator");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        using var f = new TestDbContextFactory();
        var db = f.CreateContext();
        var sp = new ServiceCollection().BuildServiceProvider();
        var act = () => new PluginService(db, sp, new PluginManifestValidator(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────────
    // GetInstalledPluginsAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInstalledPluginsAsync_NoPlugins_ReturnsEmpty()
    {
        using var h = new PluginHarness();

        var result = await h.Service.GetInstalledPluginsAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInstalledPluginsAsync_ReturnsAllOrderedByName()
    {
        using var h = new PluginHarness();
        using (var ctx = h.Fresh())
        {
            ctx.Plugins.AddRange(
                MakeRow("Charlie"),
                MakeRow("Alpha"),
                MakeRow("Bravo"));
            await ctx.SaveChangesAsync();
        }

        var result = await h.Service.GetInstalledPluginsAsync();

        result.Select(p => p.Name).Should().ContainInOrder("Alpha", "Bravo", "Charlie");

        static PluginEntity MakeRow(string name) => new()
        {
            PluginId = PluginHarness.NewPluginId(),
            Name = name,
            Version = "1.0.0",
            Author = "Tester",
            Description = "d",
            PluginType = "Custom",
            InstallPath = "x",
            InstalledAt = DateTime.UtcNow,
        };
    }

    // ─────────────────────────────────────────────────────────────────────
    // InstallPluginAsync — validation and guard branches
    // ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InstallPluginAsync_BlankPath_ThrowsArgumentException(string? path)
    {
        using var h = new PluginHarness();

        var act = () => h.Service.InstallPluginAsync(path!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task InstallPluginAsync_FileDoesNotExist_ThrowsFileNotFound()
    {
        using var h = new PluginHarness();
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".agentx-plugin");

        var act = () => h.Service.InstallPluginAsync(missing);

        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task InstallPluginAsync_PackageWithoutManifest_Throws()
    {
        using var h = new PluginHarness();
        var pkg = h.CreatePackage(manifest: null, includeManifest: false,
            files: new[] { ("readme.txt", Encoding.UTF8.GetBytes("hi")) });

        var act = () => h.Service.InstallPluginAsync(pkg);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest.json*");
    }

    [Fact]
    public async Task InstallPluginAsync_ManifestDeserializesToNull_Throws()
    {
        using var h = new PluginHarness();
        var pkg = h.CreatePackage(manifest: null, rawManifestJson: "null");

        var act = () => h.Service.InstallPluginAsync(pkg);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*deserialized to null*");
    }

    [Fact]
    public async Task InstallPluginAsync_InvalidManifest_Throws()
    {
        using var h = new PluginHarness();
        // "nodots" fails the reverse-DNS plugin-id rule in the real validator.
        var pkg = h.CreatePackage(ValidManifest("nodots"));

        var act = () => h.Service.InstallPluginAsync(pkg);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*manifest is invalid*");
    }

    [Fact]
    public async Task InstallPluginAsync_DuplicatePluginId_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        using (var ctx = h.Fresh())
        {
            ctx.Plugins.Add(new PluginEntity
            {
                PluginId = id,
                Name = "Existing",
                Version = "0.9.0",
                Author = "Tester",
                Description = "d",
                PluginType = "Custom",
                InstallPath = "x",
                InstalledAt = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }
        var pkg = h.CreatePackage(ValidManifest(id));

        var act = () => h.Service.InstallPluginAsync(pkg);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already installed*");
    }

    [Fact]
    public async Task InstallPluginAsync_ZipSlipEntry_ThrowsFailedToExtract()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id); // register the rollback dir for cleanup
        var pkg = h.CreatePackage(ValidManifest(id),
            files: new[] { ("../escaped.txt", Encoding.UTF8.GetBytes("escape")) });

        var act = () => h.Service.InstallPluginAsync(pkg);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to extract*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // InstallPluginAsync — happy path and README handling
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task InstallPluginAsync_Valid_CreatesDisabledRecordWithReadmeFile()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var pkg = h.CreatePackage(ValidManifest(id),
            files: new[]
            {
                ("plugin.dll", TestPluginAssemblies.Good),
                ("README.md", Encoding.UTF8.GetBytes("# Hello from the file")),
            });

        var entity = await h.Service.InstallPluginAsync(pkg);

        entity.PluginId.Should().Be(id);
        entity.IsEnabled.Should().BeFalse();
        entity.InstalledAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        entity.ReadmeContent.Should().Be("# Hello from the file");
        entity.InstallPath.Should().Be(installDir);
        File.Exists(Path.Combine(installDir, "plugin.dll")).Should().BeTrue();

        using var verify = h.Fresh();
        verify.Plugins.Should().ContainSingle(p => p.PluginId == id && !p.IsEnabled);
    }

    [Fact]
    public async Task InstallPluginAsync_ReadmeFileTooLarge_SkipsReadme()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var oversized = new byte[AppConstants.MaxPluginReadmeBytes + 1];
        Array.Fill(oversized, (byte)'a');
        var pkg = h.CreatePackage(ValidManifest(id),
            files: new[] { ("README.md", oversized) });

        var entity = await h.Service.InstallPluginAsync(pkg);

        entity.ReadmeContent.Should().BeNull();
    }

    [Fact]
    public async Task InstallPluginAsync_InlineManifestReadme_Stored()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var pkg = h.CreatePackage(ValidManifest(id, readme: "inline documentation"));

        var entity = await h.Service.InstallPluginAsync(pkg);

        entity.ReadmeContent.Should().Be("inline documentation");
    }

    [Fact]
    public async Task InstallPluginAsync_InlineManifestReadmeTooLarge_Skipped()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var bigReadme = new string('a', AppConstants.MaxPluginReadmeBytes + 1);
        var pkg = h.CreatePackage(ValidManifest(id, readme: bigReadme));

        var entity = await h.Service.InstallPluginAsync(pkg);

        entity.ReadmeContent.Should().BeNull();
    }

    [Fact]
    public async Task InstallPluginAsync_NoReadme_LeavesReadmeNull()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var pkg = h.CreatePackage(ValidManifest(id));

        var entity = await h.Service.InstallPluginAsync(pkg);

        entity.ReadmeContent.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // UninstallPluginAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UninstallPluginAsync_NotFound_DoesNotThrow()
    {
        using var h = new PluginHarness();

        var act = () => h.Service.UninstallPluginAsync(987654);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task UninstallPluginAsync_ContainedDirectory_DeletesFilesAndRow()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            files: new[] { ("data.txt", Encoding.UTF8.GetBytes("payload")) });

        await h.Service.UninstallPluginAsync(entityId);

        Directory.Exists(installDir).Should().BeFalse();
        using var verify = h.Fresh();
        verify.Plugins.Any(p => p.Id == entityId).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallPluginAsync_DirectoryOutsideBase_RefusesDeleteButRemovesRow()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var externalDir = h.NewExternalDir(); // outside %LocalAppData%\AgentX\Plugins
        File.WriteAllText(Path.Combine(externalDir, "keep.txt"), "do not delete");
        var entityId = h.SeedInstalled(id, externalDir, createDir: false);

        await h.Service.UninstallPluginAsync(entityId);

        // The containment guard must refuse to delete a path outside the plugin base directory.
        Directory.Exists(externalDir).Should().BeTrue();
        File.Exists(Path.Combine(externalDir, "keep.txt")).Should().BeTrue();
        using var verify = h.Fresh();
        verify.Plugins.Any(p => p.Id == entityId).Should().BeFalse();
    }

    [Fact]
    public async Task UninstallPluginAsync_EmptyInstallPath_RemovesRow()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var entityId = h.SeedInstalled(id, installPath: "", createDir: false);

        await h.Service.UninstallPluginAsync(entityId);

        using var verify = h.Fresh();
        verify.Plugins.Any(p => p.Id == entityId).Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────
    // EnablePluginAsync — guard / load-failure branches
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnablePluginAsync_NotFound_Throws()
    {
        using var h = new PluginHarness();

        var act = () => h.Service.EnablePluginAsync(987654);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*was not found*");
    }

    [Fact]
    public async Task EnablePluginAsync_EntryAssemblyMissing_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        // No manifest, no DLL → derived entry name, file absent.
        var entityId = h.SeedInstalled(id, installDir);

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*entry assembly not found*");
    }

    [Fact]
    public async Task EnablePluginAsync_UnsafeEntryAssemblyInManifest_FallsBackThenThrows()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        // A path-bearing entryAssembly is rejected by GetEntryAssemblyName, which falls back to the
        // derived name; that DLL does not exist, so enable fails with "entry assembly not found".
        var entityId = h.SeedInstalled(id, installDir, manifestJson: ManifestJson(id, entryAssembly: "..\\evil.dll"));

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*entry assembly not found*");
    }

    [Fact]
    public async Task EnablePluginAsync_MalformedManifestOnDisk_FallsBackThenThrows()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir, manifestJson: "{ this is not valid json");

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*entry assembly not found*");
    }

    [Fact]
    public async Task EnablePluginAsync_CorruptAssembly_ThrowsFailedToLoad()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            manifestJson: ManifestJson(id),
            files: new[] { ("plugin.dll", Encoding.UTF8.GetBytes("this is not a portable executable")) });

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to load assembly*");
    }

    [Fact]
    public async Task EnablePluginAsync_NoPluginTypeInAssembly_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            manifestJson: ManifestJson(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.NoPlugin) });

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No public*IPlugin*");
    }

    [Fact]
    public async Task EnablePluginAsync_MultiplePluginTypes_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            manifestJson: ManifestJson(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.MultiPlugin) });

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Multiple types*");
    }

    [Fact]
    public async Task EnablePluginAsync_PluginConstructorThrows_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            manifestJson: ManifestJson(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.CtorThrows) });

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Failed to instantiate*");
    }

    [Fact]
    public async Task EnablePluginAsync_InitializeThrows_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            manifestJson: ManifestJson(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.InitThrows) });

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*InitializeAsync*");

        using var verify = h.Fresh();
        verify.Plugins.Single(p => p.Id == entityId).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task EnablePluginAsync_ActivateThrows_Throws()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir,
            manifestJson: ManifestJson(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.ActivateThrows) });

        var act = () => h.Service.EnablePluginAsync(entityId);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*ActivateAsync*");
    }

    // ─────────────────────────────────────────────────────────────────────
    // DisablePluginAsync
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DisablePluginAsync_NotFound_DoesNotThrow()
    {
        using var h = new PluginHarness();

        var act = () => h.Service.DisablePluginAsync(987654);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DisablePluginAsync_NotLoaded_PersistsDisabledFlag()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        var installDir = h.InstallDirFor(id);
        var entityId = h.SeedInstalled(id, installDir, isEnabled: true);

        await h.Service.DisablePluginAsync(entityId);

        using var verify = h.Fresh();
        verify.Plugins.Single(p => p.Id == entityId).IsEnabled.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Active-plugin queries with nothing loaded
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetActivePluginsAsync_NoneLoaded_ReturnsEmpty()
    {
        using var h = new PluginHarness();

        var active = await h.Service.GetActivePluginsAsync();

        active.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetPluginInstanceAsync_BlankId_ThrowsArgumentException(string? pluginId)
    {
        using var h = new PluginHarness();

        var act = () => h.Service.GetPluginInstanceAsync<IPlugin>(pluginId!);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task GetPluginInstanceAsync_NotActive_ReturnsNull()
    {
        using var h = new PluginHarness();

        var instance = await h.Service.GetPluginInstanceAsync<IPlugin>("com.vendor.not-loaded");

        instance.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Full lifecycle against real, loadable plugin assemblies
    // ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Lifecycle_InstallEnableQueryDisable_GoodPlugin()
    {
        using var h = new PluginHarness(registerInbox: true); // exercises the IInboxService-present branch
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var pkg = h.CreatePackage(ValidManifest(id),
            files: new[]
            {
                ("plugin.dll", TestPluginAssemblies.Good),
                ("README.md", Encoding.UTF8.GetBytes("# Good plugin")),
            });

        var installed = await h.Service.InstallPluginAsync(pkg);
        installed.IsEnabled.Should().BeFalse();

        await h.Service.EnablePluginAsync(installed.Id);

        // Enabling again is an idempotent no-op (the already-loaded guard).
        await h.Service.EnablePluginAsync(installed.Id);

        using (var verify = h.Fresh())
        {
            var row = verify.Plugins.Single(p => p.Id == installed.Id);
            row.IsEnabled.Should().BeTrue();
            row.LastActivatedAt.Should().NotBeNull();
        }

        (await h.Service.GetActivePluginsAsync()).Should().HaveCount(1);
        (await h.Service.GetPluginInstanceAsync<IPlugin>(id)).Should().NotBeNull();
        // Active, but does not implement the marker interface → null.
        (await h.Service.GetPluginInstanceAsync<ITestMarkerPlugin>(id)).Should().BeNull();

        await h.Service.DisablePluginAsync(installed.Id);

        (await h.Service.GetActivePluginsAsync()).Should().BeEmpty();
        using var after = h.Fresh();
        after.Plugins.Single(p => p.Id == installed.Id).IsEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Lifecycle_EnableThenUninstall_RemovesRowAndDeactivates()
    {
        using var h = new PluginHarness(); // IInboxService absent → exercises the optional-service branch
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var pkg = h.CreatePackage(ValidManifest(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.Good) });

        var installed = await h.Service.InstallPluginAsync(pkg);
        await h.Service.EnablePluginAsync(installed.Id);
        (await h.Service.GetActivePluginsAsync()).Should().HaveCount(1);

        await h.Service.UninstallPluginAsync(installed.Id);

        (await h.Service.GetActivePluginsAsync()).Should().BeEmpty();
        using var verify = h.Fresh();
        verify.Plugins.Any(p => p.Id == installed.Id).Should().BeFalse();
    }

    [Fact]
    public async Task Lifecycle_DeactivateAndDisposeThrow_StillUnloadsCleanly()
    {
        using var h = new PluginHarness();
        var id = PluginHarness.NewPluginId();
        h.InstallDirFor(id);
        var pkg = h.CreatePackage(ValidManifest(id),
            files: new[] { ("plugin.dll", TestPluginAssemblies.DeactivateThrows) });

        var installed = await h.Service.InstallPluginAsync(pkg);
        await h.Service.EnablePluginAsync(installed.Id);

        // Deactivate and Dispose both throw inside the plugin; the service must swallow them and
        // still unload + persist the disabled state.
        var act = () => h.Service.DisablePluginAsync(installed.Id);
        await act.Should().NotThrowAsync();

        (await h.Service.GetActivePluginsAsync()).Should().BeEmpty();
        using var verify = h.Fresh();
        verify.Plugins.Single(p => p.Id == installed.Id).IsEnabled.Should().BeFalse();
    }
}

// ─────────────────────────────────────────────────────────────────────────
// Roslyn-compiled plugin fixtures
// ─────────────────────────────────────────────────────────────────────────

/// <summary>
/// Compiles minimal plugin assemblies in-process and caches the emitted bytes per variant, so the
/// plugin-loader tests run against genuinely loadable DLLs rather than mocks. Each variant targets a
/// specific branch of <c>PluginService.EnablePluginAsync</c> / <c>DiscoverPluginType</c>.
/// </summary>
internal static class TestPluginAssemblies
{
    private static readonly Dictionary<string, byte[]> Cache = new(StringComparer.Ordinal);
    private static readonly object Gate = new();

    public static byte[] Good => Get("AxTestPlugin_Good", PluginSource(
        "P",
        init: "return Task.CompletedTask;",
        activate: "return Task.CompletedTask;",
        deactivate: "return Task.CompletedTask;",
        dispose: ""));

    public static byte[] InitThrows => Get("AxTestPlugin_InitThrows", PluginSource(
        "P",
        init: "throw new InvalidOperationException(\"init boom\");",
        activate: "return Task.CompletedTask;",
        deactivate: "return Task.CompletedTask;",
        dispose: ""));

    public static byte[] ActivateThrows => Get("AxTestPlugin_ActivateThrows", PluginSource(
        "P",
        init: "return Task.CompletedTask;",
        activate: "throw new InvalidOperationException(\"activate boom\");",
        deactivate: "return Task.CompletedTask;",
        dispose: ""));

    public static byte[] DeactivateThrows => Get("AxTestPlugin_DeactivateThrows", PluginSource(
        "P",
        init: "return Task.CompletedTask;",
        activate: "return Task.CompletedTask;",
        deactivate: "throw new InvalidOperationException(\"deactivate boom\");",
        dispose: "throw new InvalidOperationException(\"dispose boom\");"));

    public static byte[] CtorThrows => Get("AxTestPlugin_CtorThrows", PluginSource(
        "P",
        init: "return Task.CompletedTask;",
        activate: "return Task.CompletedTask;",
        deactivate: "return Task.CompletedTask;",
        dispose: "",
        ctor: "public P() { throw new InvalidOperationException(\"ctor boom\"); }"));

    public static byte[] NoPlugin => Get("AxTestPlugin_NoPlugin", @"
namespace AxTestPlugins
{
    public sealed class NotAPlugin
    {
        public int Answer() => 42;
    }
}");

    public static byte[] MultiPlugin => Get("AxTestPlugin_Multi", @"
using System;
using System.Threading.Tasks;
using AgentX.Core.Services.Plugins;
namespace AxTestPlugins
{
    public sealed class First : IPlugin
    {
        public string Id => ""a""; public string Name => ""A""; public string Version => ""1.0.0"";
        public string Author => ""T""; public string Description => ""d""; public PluginType Type => PluginType.Custom;
        public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;
        public Task ActivateAsync() => Task.CompletedTask;
        public Task DeactivateAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
    public sealed class Second : IPlugin
    {
        public string Id => ""b""; public string Name => ""B""; public string Version => ""1.0.0"";
        public string Author => ""T""; public string Description => ""d""; public PluginType Type => PluginType.Custom;
        public Task InitializeAsync(IPluginContext context) => Task.CompletedTask;
        public Task ActivateAsync() => Task.CompletedTask;
        public Task DeactivateAsync() => Task.CompletedTask;
        public void Dispose() { }
    }
}");

    private static string PluginSource(
        string className,
        string init,
        string activate,
        string deactivate,
        string dispose,
        string ctor = "")
        => $@"
using System;
using System.Threading.Tasks;
using AgentX.Core.Services.Plugins;
namespace AxTestPlugins
{{
    public sealed class {className} : IPlugin
    {{
        {ctor}
        public string Id => ""test.plugin"";
        public string Name => ""Test Plugin"";
        public string Version => ""1.0.0"";
        public string Author => ""Tester"";
        public string Description => ""desc"";
        public PluginType Type => PluginType.Custom;
        public Task InitializeAsync(IPluginContext context) {{ {init} }}
        public Task ActivateAsync() {{ {activate} }}
        public Task DeactivateAsync() {{ {deactivate} }}
        public void Dispose() {{ {dispose} }}
    }}
}}";

    private static byte[] Get(string assemblyName, string source)
    {
        lock (Gate)
        {
            if (Cache.TryGetValue(assemblyName, out var cached))
                return cached;

            var tree = CSharpSyntaxTree.ParseText(source);

            // Reference the full set of platform assemblies plus the host's AgentX.Core so the
            // emitted plugin binds IPlugin to the SAME type the running host uses.
            var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator);
            var refPaths = new HashSet<string>(tpa, StringComparer.OrdinalIgnoreCase)
            {
                typeof(IPlugin).Assembly.Location,
            };
            var references = refPaths
                .Where(p => !string.IsNullOrEmpty(p) && File.Exists(p))
                .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
                .ToList();

            var compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { tree },
                references,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    optimizationLevel: OptimizationLevel.Release));

            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms);
            if (!emit.Success)
            {
                var errors = string.Join(
                    Environment.NewLine,
                    emit.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
                throw new InvalidOperationException(
                    $"Test plugin '{assemblyName}' failed to compile:{Environment.NewLine}{errors}");
            }

            var bytes = ms.ToArray();
            Cache[assemblyName] = bytes;
            return bytes;
        }
    }
}
