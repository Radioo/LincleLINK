using FluentAssertions;

namespace LincleLINK.UITests;

/// <summary>Torrent pre-fill page: wizard gates and the full match/verify/link flow.</summary>
public sealed class TorrentTests : UITestBase
{
    [UIFact]
    public void Wizard_StartsLocked_WithExplanatoryHints()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");
            app.WaitForId("NavTorrent").Click();
            app.WaitForId("TorrentPageHeader");

            // Nothing selected or typed yet: every step is gated.
            app.WaitForId("TorrentMatch").Enabled.Should().BeFalse();
            app.WaitForId("TorrentVerify").Enabled.Should().BeFalse();
            app.WaitForId("TorrentLink").Enabled.Should().BeFalse();

            app.WaitForId("TorrentVerifyHint").Text.Should().Be("Match files first.");
            app.WaitForId("TorrentLinkHint").Text.Should().Be("Verify pieces first.");
        });
    }

    [UIFact]
    public void FullWizard_MatchVerifyLink_PreFillsDownloadFolder()
    {
        using var app = AppSession.Launch(seedSettings: true);
        Run(app, () =>
        {
            app.WaitForId("LibraryEmptyState");

            // A library entry plus a torrent generated from the same folder.
            var source = TestData.CreateSourceFolder(app.TempRoot, "TorrentSource");
            AddEntry(app, "Torrentable", source, keepOriginals: true);

            var (torrentPath, relativePath) = TestData
                .CreateTorrentAsync(source, Path.Combine(app.TempRoot, "fixture.torrent"))
                .GetAwaiter().GetResult();
            var downloadDir = Path.Combine(app.TempRoot, "download");
            Directory.CreateDirectory(downloadDir);

            app.WaitForId("NavTorrent").Click();
            app.WaitForId("TorrentPageHeader");

            app.SelectFirstComboItem("TorrentEntryCombo");
            app.SetText(app.WaitForId("TorrentFile"), torrentPath);
            if (relativePath.Length > 0)
            {
                app.SetText(app.WaitForId("TorrentRelativePath"), relativePath);
            }

            app.SetText(app.WaitForId("TorrentDownloadPath"), downloadDir);

            // Step 1: match by name and size.
            app.WaitUntil(() => app.WaitForId("TorrentMatch").Enabled, "match step unlocked");
            app.WaitForId("TorrentMatch").Click();
            app.WaitUntil(
                () => app.WaitForId("TorrentMatchSummary").Text
                    == $"{TestData.SourceFileCount} of {TestData.SourceFileCount} files matched.",
                "all files matched",
                TimeSpan.FromSeconds(60));

            // Step 2: byte-exact piece verification against storage.
            app.WaitUntil(() => app.WaitForId("TorrentVerify").Enabled, "verify step unlocked");
            app.WaitForId("TorrentVerify").Click();
            app.WaitUntil(
                () => app.WaitForId("TorrentVerifySummary").Text.Contains("pieces verified."),
                "pieces verified",
                TimeSpan.FromSeconds(60));

            // Step 3: the button states exactly what it will link, then links it.
            var link = app.WaitForId("TorrentLink");
            link.Text.Should().Be($"Link {TestData.SourceFileCount} files");
            app.WaitUntil(() => app.WaitForId("TorrentLink").Enabled, "link step unlocked");
            app.WaitForId("TorrentLink").Click();

            app.WaitUntil(
                () => app.WaitForId("TorrentLinkSummary").Text
                    == $"Linked {TestData.SourceFileCount} files, skipped 0.",
                "all files linked",
                TimeSpan.FromSeconds(60));
            app.WaitForOutcome("✓ Pre-fill finished");

            // The download folder now mirrors the torrent's layout with intact bytes.
            var prefix = relativePath.Length > 0 ? Path.Combine(downloadDir, relativePath) : downloadDir;
            File.ReadAllBytes(Path.Combine(prefix, "alpha.bin"))
                .Should().Equal(TestData.FileContent(0xA1, 2048));
            File.ReadAllBytes(Path.Combine(prefix, "sub", "beta.bin"))
                .Should().Equal(TestData.FileContent(0xB2, 3072));
            File.Exists(Path.Combine(prefix, "dupe1.bin")).Should().BeTrue();
            File.Exists(Path.Combine(prefix, "dupe2.bin")).Should().BeTrue();
        });
    }
}
