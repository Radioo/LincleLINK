using FluentAssertions;

namespace LincleLINK.UITests;

/// <summary>
/// Library page: the add flow (both modes and its error path), filtering,
/// selection + inspector, remove, deploy, export, and storage cleanup.
/// </summary>
public sealed class LibraryTests : UITestBase
{
    [UIFact]
    public void AddFolder_KeepOriginals_AddsEntry_AndLeavesOriginalsUntouched()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "GameData v1");

            AddEntry(app, "Game v1", source, keepOriginals: true);

            app.WaitForOutcome("✓ Added to library");

            // Dedup: 4 files, 2 identical, so storage holds 3 blobs.
            StorageBlobCount(app).Should().Be(TestData.UniqueBlobCount);

            // Keep mode: originals are still plain files with their content intact.
            File.ReadAllBytes(Path.Combine(source, "alpha.bin"))
                .Should().Equal(TestData.FileContent(0xA1, 2048));
            File.ReadAllBytes(Path.Combine(source, "sub", "beta.bin"))
                .Should().Equal(TestData.FileContent(0xB2, 3072));
        });
    }

    [UIFact]
    public void AddFolder_ReclaimSpace_AddsEntry_AndFolderKeepsWorking()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "ReclaimSource");

            // Reclaim is the pre-selected default; sanity-check that before confirming.
            app.WaitForId("AddFirstFolder").Click();
            app.SetText(app.WaitForId("AddName"), "Reclaimed");
            app.SetText(app.WaitForId("AddFolderPath"), source);
            app.WaitUntil(() => app.WaitForId("AddModeReclaim").Selected, "Reclaim mode selected by default");
            app.WaitForId("AddConfirm").Click();

            app.WaitForGoneById("AddName", TimeSpan.FromSeconds(60));
            app.WaitForText("Reclaimed");
            app.WaitForOutcome("✓ Added to library");

            // Reclaim mode: the folder's files became hard links into storage but
            // read back exactly as before.
            File.ReadAllBytes(Path.Combine(source, "alpha.bin"))
                .Should().Equal(TestData.FileContent(0xA1, 2048));
            File.ReadAllBytes(Path.Combine(source, "dupe2.bin"))
                .Should().Equal(TestData.FileContent(0xC3, 1024));
            StorageBlobCount(app).Should().Be(TestData.UniqueBlobCount);
        });
    }

    [UIFact]
    public void AddFolder_EmptyFolder_ShowsError_AndPanelStaysOpen()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var empty = TestData.CreateEmptyFolder(app.TempRoot, "EmptyFolder");

            app.WaitForId("AddFirstFolder").Click();
            app.SetText(app.WaitForId("AddName"), "Nothing here");
            app.SetText(app.WaitForId("AddFolderPath"), empty);
            app.WaitForId("AddConfirm").Click();

            // Error dialog from the service; dismiss it.
            app.WaitForText("The folder contains no files.");
            app.ClickMessageButton("OK");

            // The panel is still open for corrections; close it explicitly.
            app.WaitForId("AddName");
            app.WaitForId("AddClose").Click();
            app.WaitForGoneById("AddName");
            app.WaitForId("LibraryEmptyState");
        });
    }

    [UIFact]
    public void Filter_NarrowsGrid_AndClearingRestoresIt()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "Shared");
            AddEntry(app, "Alpha One", source, keepOriginals: true);
            AddEntry(app, "Beta Two", source, keepOriginals: true);

            app.SetText(app.WaitForId("LibraryFilter"), "Alpha");
            app.WaitForGoneByName("Beta Two");
            app.WaitForText("Alpha One");

            app.SetText(app.WaitForId("LibraryFilter"), string.Empty);
            app.WaitForText("Beta Two");
        });
    }

    [UIFact]
    public void SelectingRow_ShowsInspector_WithEnabledActions()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "InspectMe");
            AddEntry(app, "Inspectable", source, keepOriginals: true);

            app.WaitForText("Inspectable").Click();

            app.WaitForId("InspectorName").Text.Should().Be("Inspectable");

            // The unique-size figure resolves from "…" to a real size.
            app.WaitUntil(() =>
            {
                var text = app.WaitForId("InspectorUniqueSize").Text;
                return text.Length > 0 && text != "…";
            }, "unique size computed");

            app.WaitForId("InspectorDeploy").Enabled.Should().BeTrue();
            app.WaitForId("InspectorExport").Enabled.Should().BeTrue();
            app.WaitForId("InspectorRemove").Enabled.Should().BeTrue();
        });
    }

    [UIFact]
    public void RemoveFromLibrary_No_KeepsEntry_Yes_RemovesIt()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "RemoveMe");
            AddEntry(app, "Removable", source, keepOriginals: true);

            app.WaitForText("Removable").Click();

            // Decline: the entry stays.
            app.WaitForId("InspectorRemove").Click();
            app.ClickMessageButton("No");
            app.WaitForGoneByName("No");
            app.WaitForText("Removable");

            // Confirm: the entry is removed and the empty state returns.
            app.WaitForId("InspectorRemove").Click();
            app.ClickMessageButton("Yes");
            app.WaitForOutcome("✓ Removed Removable from the library");
            app.WaitForId("LibraryEmptyState");

            // Removal keeps the blobs in storage.
            StorageBlobCount(app).Should().Be(TestData.UniqueBlobCount);
        });
    }

    [UIFact]
    public void Deploy_ToFolder_RecreatesEntryAtTarget()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "DeploySource");
            AddEntry(app, "Deployable", source, keepOriginals: true);

            app.WaitForText("Deployable").Click();
            app.WaitForId("InspectorDeploy").Click();

            var target = Path.Combine(app.TempRoot, "deploy-target");
            app.CompleteFolderPicker("select a target folder", target);

            app.WaitForOutcome($"✓ Deployed {TestData.SourceFileCount} files", TimeSpan.FromSeconds(60));

            // Original names and structure, via hard links, with intact content.
            File.ReadAllBytes(Path.Combine(target, "alpha.bin"))
                .Should().Equal(TestData.FileContent(0xA1, 2048));
            File.ReadAllBytes(Path.Combine(target, "sub", "beta.bin"))
                .Should().Equal(TestData.FileContent(0xB2, 3072));
            File.Exists(Path.Combine(target, "dupe1.bin")).Should().BeTrue();
            File.Exists(Path.Combine(target, "dupe2.bin")).Should().BeTrue();
        });
    }

    [UIFact]
    public void Export_ToFolder_CopiesHashedBlobs()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "ExportSource");
            AddEntry(app, "Exportable", source, keepOriginals: true);

            app.WaitForText("Exportable").Click();
            app.WaitForId("InspectorExport").Click();

            var dest = Path.Combine(app.TempRoot, "export-dest");
            app.CompleteFolderPicker("select a destination folder", dest);

            app.WaitForOutcome($"✓ Exported {TestData.UniqueBlobCount} files", TimeSpan.FromSeconds(60));

            Directory.GetFiles(dest, "*", SearchOption.AllDirectories)
                .Should().HaveCount(TestData.UniqueBlobCount, "the entry's unique blobs are exported under hashed names");
        });
    }

    [UIFact]
    public void CleanupStorage_WhenClean_SaysSo()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");

            app.WaitForId("CleanupStorage").Click();

            app.WaitForText("Storage is clean - every file belongs to a library entry.");
            app.ClickMessageButton("OK");
            app.WaitForOutcome("✓ Storage is clean");
        });
    }

    [UIFact]
    public void CleanupStorage_AfterRemove_DeletesOrphanedBlobs()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            var source = TestData.CreateSourceFolder(app.TempRoot, "OrphanSource");
            AddEntry(app, "Orphaned", source, keepOriginals: true);

            app.WaitForText("Orphaned").Click();
            app.WaitForId("InspectorRemove").Click();
            app.ClickMessageButton("Yes");
            app.WaitForId("LibraryEmptyState");

            app.WaitForId("CleanupStorage").Click();

            // 3 orphaned blobs are reported and deleted after confirmation.
            app.WaitForText("Yes");
            app.ClickMessageButton("Yes");

            app.WaitForOutcome($"✓ Deleted {TestData.UniqueBlobCount} files from storage");
            app.WaitUntil(() => StorageBlobCount(app) == 0, "storage emptied");
        });
    }

    private static int StorageBlobCount(AppSession app)
    {
        var db = Path.Combine(app.DataDirectory, "db");
        return Directory.Exists(db)
            ? Directory.GetFiles(db, "*", SearchOption.AllDirectories).Length
            : 0;
    }
}
