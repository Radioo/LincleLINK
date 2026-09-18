using FluentAssertions;
using LincleLINK.Core.Abstractions.Filesystem;
using LincleLINK.Core.Abstractions.Storage;
using LincleLINK.Core.Application;
using LincleLINK.Core.Domain;
using LincleLINK.Core.Domain.Updates;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Application;

/// <summary>
/// <see cref="PlannedInstanceFileSystem"/> (plan 16 D10): an Instance that is
/// deployed nowhere, presented as folders and files for the game detector.
/// </summary>
public sealed class PlannedInstanceFileSystemTests
{
    private readonly IFileStore _store = Substitute.For<IFileStore>();
    private readonly IFileSystem _disk = Substitute.For<IFileSystem>();

    private PlannedInstanceFileSystem Create()
    {
        _store.GetPath(Arg.Any<string>()).Returns(call => "/storage/" + call.Arg<string>());
        var instance = Instance.Create(
            "IIDX 32",
            [
                new InstanceFile("ea3-config.xml", "prop", 10, "CFG.xml"),
                new InstanceFile("bm2dx.dll", "modules", 100, "OLD.dll"),
                new InstanceFile("gone.bin", "modules", 1, "GONE.bin"),
            ],
            ["prop", "modules", "empty"]);
        var plan = UpdatePlanner.Compute(
            instance,
            [new UpdateSource("", [new SourceFile("modules", "bm2dx.dll", 120, "NEW.dll") { FullPath = "/drop/bm2dx.dll" }], [])],
            removedPaths: ["modules/gone.bin"]);
        return new PlannedInstanceFileSystem(plan, _store, _disk);
    }

    private static string At(PlannedInstanceFileSystem view, params string[] segments)
        => Path.Combine([view.Root, .. segments]);

    [Fact]
    public void It_lists_the_resulting_folders_and_files_ignoring_case()
    {
        var view = Create();

        view.DirectoryExists(view.Root).Should().BeTrue();
        view.DirectoryExists(At(view, "PROP")).Should().BeTrue();
        view.FileExists(At(view, "Prop", "EA3-CONFIG.XML")).Should().BeTrue();
        view.FileExists(At(view, "modules", "gone.bin")).Should().BeFalse("a removed file is not part of the result");
        view.GetFileLength(At(view, "modules", "bm2dx.dll")).Should().Be(120);

        view.EnumerateDirectories(view.Root, recursive: false).Select(Path.GetFileName)
            .Should().BeEquivalentTo("prop", "modules", "empty");
        view.EnumerateFiles(At(view, "modules"), recursive: false).Select(Path.GetFileName)
            .Should().BeEquivalentTo("bm2dx.dll");
        view.EnumerateFiles(view.Root, recursive: true).Should().HaveCount(2);
    }

    [Fact]
    public void A_kept_file_is_read_from_storage_and_a_source_file_from_where_it_was_dropped()
    {
        var view = Create();
        _disk.ReadAllText("/storage/CFG.xml").Returns("<ea3/>");
        using var dropped = new MemoryStream([1, 2, 3]);
        _disk.OpenRead("/drop/bm2dx.dll").Returns(dropped);

        view.ReadAllText(At(view, "prop", "ea3-config.xml")).Should().Be("<ea3/>");
        view.OpenRead(At(view, "modules", "bm2dx.dll")).Should().BeSameAs(dropped);
    }

    [Fact]
    public void Nothing_outside_its_root_exists_and_nothing_can_be_changed()
    {
        var view = Create();
        var parent = Path.GetDirectoryName(view.Root)!;

        view.DirectoryExists(parent).Should().BeFalse();
        view.EnumerateDirectories(parent, recursive: false).Should().BeEmpty();
        view.EnumerateFiles(parent, recursive: true).Should().BeEmpty();
        view.FileExists(Path.Combine(parent, "prop", "ea3-config.xml")).Should().BeFalse();
        _disk.DidNotReceiveWithAnyArgs().FileExists(default!);

        var delete = () => view.DeleteFile(At(view, "prop", "ea3-config.xml"));
        var create = () => view.CreateDirectory(At(view, "new"));
        var move = () => view.MoveFile(At(view, "a"), At(view, "b"), overwrite: true);
        delete.Should().Throw<NotSupportedException>();
        create.Should().Throw<NotSupportedException>();
        move.Should().Throw<NotSupportedException>();
    }
}
