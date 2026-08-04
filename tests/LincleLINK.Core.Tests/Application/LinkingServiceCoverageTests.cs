using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Linking;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Tests.TestHelpers;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// Error-collapsing summary and missing-instance branches of <see cref="LinkingService"/>.
/// </summary>
public sealed class LinkingServiceCoverageTests
{
    private readonly IFileSystem _fs = Substitute.For<IFileSystem>();
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IHardLinker _hardLinker = Substitute.For<IHardLinker>();
    private readonly IHardLinkPreflight _preflight = Substitute.For<IHardLinkPreflight>();
    private readonly IInstanceRepository _repository = Substitute.For<IInstanceRepository>();
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    private LinkingService CreateService() => new(_fs, _store, _hardLinker, _preflight, _repository, _dialogs);

    private static Instance SampleInstance() => Instance.Create(
        "inst",
        [
            new InstanceFile("a.bin", "", 10, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.bin"),
            new InstanceFile("b.bin", "sub", 20, "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.bin"),
        ],
        ["sub"]);

    [Fact]
    public async Task More_than_20_errors_are_collapsed_into_a_summary_line()
    {
        var files = Enumerable.Range(0, 25)
            .Select(i => new InstanceFile($"f{i}.bin", "", 1, $"{i:X}".PadRight(32, '0') + ".bin"))
            .ToList();
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>())
            .Returns(Instance.Create("inst", files, []));
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = "boom";
            return false;
        });

        var logs = new List<string>();
        var result = await CreateService().LinkInstanceAsync("inst", new SynchronousProgress<string>(logs.Add));

        result.Failed.Should().Be(25);
        logs.Should().Contain(m => m.Contains("25 failed"));
        logs.Should().Contain(m => m.Contains("and 5 more"));
    }

    [Fact]
    public async Task Copy_hashed_missing_instance_returns_error()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\dest");
        _repository.GetAsync("nope", Arg.Any<CancellationToken>()).Returns((Instance?)null);

        var result = await CreateService().CopyHashedFilesAsync("nope");

        result.Cancelled.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Percent_progress_reaches_one_hundred()
    {
        _dialogs.PickFolderAsync(Arg.Any<string>()).Returns("C:\\target");
        _repository.GetAsync("inst", Arg.Any<CancellationToken>()).Returns(SampleInstance());
        _fs.FileExists(Arg.Any<string>()).Returns(false);
        _hardLinker.TryCreateLink(Arg.Any<string>(), Arg.Any<string>(), out _).Returns(x =>
        {
            x[2] = null;
            return true;
        });

        var percents = new List<double>();
        var result = await CreateService().LinkInstanceAsync(
            "inst", percent: new SynchronousProgress<double>(percents.Add));

        result.Linked.Should().Be(2);
        percents.Should().NotBeEmpty();
        percents.Last().Should().Be(100);
    }
}
