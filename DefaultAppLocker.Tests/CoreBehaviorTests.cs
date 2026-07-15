using DefaultAppLocker.Core;
using Xunit;

namespace DefaultAppLocker.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public void Merge_Override_ReplacesSnapshotAssociation()
    {
        var service = new AssociationService();
        var snapshot = new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = ".txt", Kind = AssociationKind.FileExtension, ProgId = "Old.Txt" }] };
        var merged = service.Merge(snapshot, [new OverrideAssociation { Identifier = "txt", Kind = AssociationKind.FileExtension, ProgId = "Applications\\Editor.exe", SourceTemplate = "Text" }]);
        Assert.Single(merged.Associations);
        Assert.Equal(".txt", merged.Associations[0].Identifier);
        Assert.Equal("Applications\\Editor.exe", merged.Associations[0].ProgId);
    }

    [Fact]
    public void Compare_ReturnsOnlyChangedAssociations()
    {
        var service = new AssociationService();
        var current = new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = ".txt", Kind = AssociationKind.FileExtension, ProgId = "A" }, new AppAssociation { Identifier = ".pdf", Kind = AssociationKind.FileExtension, ProgId = "Same" }] };
        var target = new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = ".txt", Kind = AssociationKind.FileExtension, ProgId = "B" }, new AppAssociation { Identifier = ".pdf", Kind = AssociationKind.FileExtension, ProgId = "Same" }] };
        var diffs = service.Compare(current, target);
        Assert.Single(diffs);
        Assert.Equal(".txt", diffs[0].Identifier);
    }

    [Fact]
    public void GenerateSetUserFtaConfig_UsesCommaSeparatedLines()
    {
        var text = new AssociationService().GenerateSetUserFtaConfig(new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = "txt", Kind = AssociationKind.FileExtension, ProgId = "Applications\\Editor.exe" }, new AppAssociation { Identifier = "https", Kind = AssociationKind.Protocol, ProgId = "BrowserHTML" }] });
        Assert.Contains(".txt, Applications\\Editor.exe", text);
        Assert.Contains("https, BrowserHTML", text);
    }

    [Fact]
    public void TextTemplate_DoesNotIncludeDangerousScriptEntrypoints()
    {
        var extensions = new TextTemplate().GetExtensions();
        Assert.DoesNotContain(".bat", extensions);
        Assert.DoesNotContain(".cmd", extensions);
        Assert.DoesNotContain(".ps1", extensions);
        Assert.Contains(".json", extensions);
        Assert.Contains(".cs", extensions);
        Assert.Contains(".ps1xml", extensions);
    }

    [Fact]
    public void ProgIdResolver_UsesApplicationsProgIdForExe()
    {
        Assert.Equal("Applications\\notepad++.exe", ProgIdResolver.ResolveApplicationProgId(@"C:\\Tools\\notepad++.exe"));
    }

    [Fact]
    public void EdgePdfProgId_UsesMSEdgePdf_NotBrowserHtml()
    {
        Assert.Equal("MSEdgePDF", ProgIdResolver.ResolvePdfProgId("MSEdgeHTM"));
        Assert.Equal("MSEdgePDF", ProgIdResolver.ResolvePdfProgId("Applications\\msedge.exe"));
        Assert.Equal("AcroExch.Document.DC", ProgIdResolver.ResolvePdfProgId("AcroExch.Document.DC"));
    }

    [Fact]
    public void BrowserAndPdfTemplates_DefaultToEdge()
    {
        Assert.All(new BrowserTemplate().CreateOverrides(string.Empty), item => Assert.Equal("MSEdgeHTM", item.ProgId));
        Assert.Equal("MSEdgePDF", new PdfTemplate().CreateOverrides(string.Empty)[0].ProgId);
    }

    [Fact]
    public void TemplateCatalog_ExposesOneUnifiedOfficeTemplate()
    {
        var ids = new TemplateCatalog().AllTemplates.Select(t => t.Id).ToArray();
        Assert.Contains("Office", ids);
        Assert.DoesNotContain("OfficeWord", ids);
        Assert.DoesNotContain("OfficeExcel", ids);
        Assert.DoesNotContain("OfficePowerPoint", ids);
    }

    [Fact]
    public void OfficeSuiteTemplate_CreatesOverridesForAllSupportedOfficeApps()
    {
        var template = new OfficeSuiteTemplate([new OfficeApplicationDefinition("Word", "WINWORD.EXE", [".docx", ".txt", ".md"]), new OfficeApplicationDefinition("Excel", "EXCEL.EXE", [".xlsx", ".csv"]), new OfficeApplicationDefinition("PowerPoint", "POWERPNT.EXE", [".pptx"])]);
        var overrides = template.CreateOverrides(string.Empty);
        Assert.Contains(overrides, o => o.Identifier == ".docx" && o.ProgId == "Applications\\WINWORD.EXE");
        Assert.Contains(overrides, o => o.Identifier == ".xlsx" && o.ProgId == "Applications\\EXCEL.EXE");
        Assert.Contains(overrides, o => o.Identifier == ".pptx" && o.ProgId == "Applications\\POWERPNT.EXE");
        Assert.DoesNotContain(overrides, o => o.Identifier is ".txt" or ".md" or ".csv");
    }

    [Fact]
    public void ConfigurationStore_SavesMultipleSnapshotAndQuickProfilesWithAliases()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid().ToString("N"));
        var store = new ConfigurationStore(dir);
        var snapshot = new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = ".a", Kind = AssociationKind.FileExtension, ProgId = "A" }] };
        var s1 = store.CreateSnapshotProfile(snapshot, "Work");
        var s2 = store.CreateSnapshotProfile(snapshot, "Home");
        var q1 = store.CreateQuickTemplateProfile([new OverrideAssociation { Identifier = ".pdf", Kind = AssociationKind.FileExtension, ProgId = "MSEdgePDF" }], "Seven Templates");
        Assert.Equal(2, store.LoadSnapshotProfiles().Count);
        Assert.Contains(store.LoadSnapshotProfiles(), p => p.Id == s1.Id && p.Alias == "Work");
        Assert.Contains(store.LoadSnapshotProfiles(), p => p.Id == s2.Id && p.Alias == "Home");
        Assert.Single(store.LoadQuickTemplateProfiles());
        q1.Alias = "Renamed";
        store.SaveQuickTemplateProfile(q1);
        Assert.Equal("Renamed", store.LoadQuickTemplateProfiles()[0].Alias);
    }

    [Fact]
    public void SnapshotProfile_DisplayName_ReflectsAliasAfterRename()
    {
        var profile = new SnapshotProfile { Alias = "Original" };
        Assert.Contains("Original", profile.DisplayName);
        profile.Alias = "Renamed";
        Assert.Contains("Renamed", profile.DisplayName);
        Assert.Equal(profile.DisplayName, profile.ToString());
    }

    [Fact]
    public void ConfigurationStore_ExportsAndImportsSnapshotsAndTemplateProfiles()
    {
        var sourceDir = Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid().ToString("N"));
        var targetDir = Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid().ToString("N"));
        var packagePath = Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid() + ".json");
        var source = new ConfigurationStore(sourceDir);
        var snapshot = new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = ".a", Kind = AssociationKind.FileExtension, ProgId = "A" }] };
        source.CreateSnapshotProfile(snapshot, "Exported Snapshot");
        source.CreateQuickTemplateProfile([new OverrideAssociation { Identifier = ".pdf", Kind = AssociationKind.FileExtension, ProgId = "MSEdgePDF" }], "Default App Template Scheme");
        source.ExportPackage(packagePath);

        var target = new ConfigurationStore(targetDir);
        var imported = target.ImportPackage(packagePath);

        Assert.Equal(1, imported.Snapshots);
        Assert.Equal(1, imported.TemplateProfiles);
        Assert.Contains(target.LoadSnapshotProfiles(), p => p.Alias == "Exported Snapshot");
        Assert.Contains(target.LoadQuickTemplateProfiles(), p => p.Alias == "Default App Template Scheme");
    }

    [Fact]
    public async Task CommandLine_ExportsSnapshotsOnlyAndHandlesHelp()
    {
        var dir = Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid().ToString("N"));
        var exportPath = Path.Combine(dir, "snapshots.json");
        var store = new ConfigurationStore(dir);
        store.CreateSnapshotProfile(new DefaultAppSnapshot { Associations = [new AppAssociation { Identifier = ".a", Kind = AssociationKind.FileExtension, ProgId = "A" }] }, "Snapshot A");
        store.CreateQuickTemplateProfile([new OverrideAssociation { Identifier = ".pdf", Kind = AssociationKind.FileExtension, ProgId = "MSEdgePDF" }], "Template A");
        var cli = new DefaultAppLockerCommandLine(store, new AssociationService(), new FakeScanner());

        var result = await cli.RunAsync(["--export-snapshots", exportPath]);
        var imported = new ConfigurationStore(Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid().ToString("N")));
        var importResult = imported.ImportPackage(exportPath);

        Assert.True(result.Handled);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(1, importResult.Snapshots);
        Assert.Equal(0, importResult.TemplateProfiles);
        Assert.Contains("--apply-snapshot", (await cli.RunAsync(["--help"])).Message);
    }

    [Fact]
    public void ConfigurationStore_UsesEnvironmentOverrideForRegressionIsolation()
    {
        var previous = Environment.GetEnvironmentVariable("DEFAULTAPPLOCKER_CONFIG_ROOT");
        var dir = Path.Combine(Path.GetTempPath(), "DefaultAppLockerTests", Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("DEFAULTAPPLOCKER_CONFIG_ROOT", dir);
            var store = new ConfigurationStore();
            Assert.Equal(dir, store.RootDirectory);
            Assert.True(Directory.Exists(store.SnapshotProfilesDirectory));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DEFAULTAPPLOCKER_CONFIG_ROOT", previous);
        }
    }
    private sealed class FakeScanner : IDefaultAppScanner
    {
        public DefaultAppSnapshot ScanCurrentUser() => new()
        {
            Associations = [new AppAssociation { Identifier = ".fake", Kind = AssociationKind.FileExtension, ProgId = "Fake.App" }]
        };
    }
}
