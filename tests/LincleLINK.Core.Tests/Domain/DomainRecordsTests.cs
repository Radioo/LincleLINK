using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Domain;
using Xunit;

namespace LincleLINK.Core.Tests.Domain;

/// <summary>
/// Pure-domain records and value objects that are too small for their own test
/// class but still need coverage (records, static factories, enum usage).
/// </summary>
public sealed class DomainRecordsTests
{
    [Fact]
    public void FileType_carries_label_and_patterns()
    {
        var fileType = new FileType("Torrent files", new[] { "*.torrent" });

        fileType.Label.Should().Be("Torrent files");
        fileType.Patterns.Should().Equal("*.torrent");
    }

    [Fact]
    public void GameVersionInfo_None_returns_empty_code_and_no_confidence()
    {
        var none = GameVersionInfo.None;

        none.GameCode.Should().BeEmpty();
        none.GameTitle.Should().BeEmpty();
        none.Confidence.Should().Be(DetectionConfidence.None);
        none.LogoKey.Should().BeNull();
    }

    [Fact]
    public void GameVersionInfo_holds_all_fields()
    {
        var info = new GameVersionInfo(
            "KFC", "SOUND VOLTEX", "J", "A", "1", "2013060500",
            "kfc-5a01c0a8_1000", "SOUND VOLTEX II -infinite infection-",
            "SDVX/SDVX_II_logo", DetectionConfidence.XmlAndPe);

        info.Dest.Should().Be("J");
        info.Spec.Should().Be("A");
        info.Rev.Should().Be("1");
        info.DateCode.Should().Be("2013060500");
        info.PeIdentifier.Should().Be("kfc-5a01c0a8_1000");
        info.DisplayTitle.Should().Be("SOUND VOLTEX II -infinite infection-");
        info.LogoKey.Should().Be("SDVX/SDVX_II_logo");
        info.Confidence.Should().Be(DetectionConfidence.XmlAndPe);
    }

    [Fact]
    public void InstanceListEntry_From_projects_an_instance()
    {
        var instance = Instance.Create(
            "IIDX28",
            [new InstanceFile("a.2dx", @"sound\25063", 100, "AAAA.bin")],
            [@"sound\25063"]);
        instance.DetectedGame = GameVersionInfo.None;
        instance.CustomLogoSource = "custom";

        var entry = InstanceListEntry.From(instance);

        entry.InstanceName.Should().Be("IIDX28");
        entry.FileCount.Should().Be(1);
        entry.TotalFileSize.Should().Be(100);
        entry.TotalFileSizeString.Should().Be("100 B");
        entry.DetectedGame.Should().BeSameAs(instance.DetectedGame);
        entry.CustomLogoSource.Should().Be("custom");
    }

    [Fact]
    public void InstanceListEntry_can_be_constructed_with_optional_name_key()
    {
        var entry = new InstanceListEntry("IIDX28", 2, 200, "200 B", NameKey: "IIDX28");

        entry.NameKey.Should().Be("IIDX28");
    }

    [Fact]
    public void InstanceListEntry_logo_uri_is_settable_via_with()
    {
        var entry = new InstanceListEntry("IIDX28", 1, 10, "10 B") with { LogoUri = "avares://Logos/IIDX/x.png" };

        entry.LogoUri.Should().Be("avares://Logos/IIDX/x.png");
    }

    [Fact]
    public void Instance_sets_detected_game_and_custom_logo_ignored_by_json()
    {
        var instance = Instance.Create("X", [], []);
        var none = GameVersionInfo.None;
        instance.DetectedGame = none;
        instance.CustomLogoSource = "custom";

        instance.DetectedGame.Should().BeSameAs(none);
        instance.CustomLogoSource.Should().Be("custom");
    }
}
