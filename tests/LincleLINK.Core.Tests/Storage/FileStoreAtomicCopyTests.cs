using FluentAssertions;
using LincleLINK.Core.Abstractions.Dialogs;
using LincleLINK.Core.Abstractions.Instances;
using LincleLINK.Core.Abstractions.Paths;
using LincleLINK.Core.Application;
using LincleLINK.Core.Infrastructure.Paths;
using LincleLINK.Core.Infrastructure.Storage;
using LincleLINK.Core.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LincleLINK.Core.Tests.Storage;

/// <summary>
/// Storage is content-addressed and deduplicating: a file under a hash name is
/// trusted to be that content by every later add. So a copy may only ever show
/// up under its hash name complete. These tests pin that for cancelled, failed
/// and racing copies, and that a temp file left by a crash harms nothing.
/// </summary>
public sealed class FileStoreAtomicCopyTests : IDisposable
{
    private const string HashA = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA.2dx";
    private const string HashB = "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB.2dx";

    private readonly TempDir _temp = new();
    private readonly IAppPaths _paths;
    private readonly FileStore _store;

    public FileStoreAtomicCopyTests()
    {
        _paths = new AppPaths(Path.Combine(_temp.Root, "data"));
        _store = new FileStore(_paths);
    }

    public void Dispose() => _temp.Dispose();

    private string[] DbFiles()
        => Directory.Exists(_paths.DbDirectory)
            ? Directory.GetFiles(_paths.DbDirectory).Select(f => Path.GetFileName(f)!).ToArray()
            : [];

    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i * 31 % 251);
        }

        return bytes;
    }

    [Fact]
    public async Task A_completed_copy_is_byte_identical_and_leaves_no_temp_file()
    {
        var content = Pattern(300_000); // several copy buffers
        var source = _temp.CreateFile("src.2dx", content);

        await _store.CopyToStoreAsync(source, HashA, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(_store.GetPath(HashA), TestContext.Current.CancellationToken)).Should().Equal(content);
        DbFiles().Should().Equal(HashA);
    }

    [Fact]
    public async Task A_copy_cancelled_before_it_starts_leaves_nothing_under_the_hash_name()
    {
        var source = _temp.CreateFile("src.2dx", Pattern(1000));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var copy = () => _store.CopyToStoreAsync(source, HashA, cts.Token);

        await copy.Should().ThrowAsync<OperationCanceledException>();
        _store.Exists(HashA).Should().BeFalse();
        DbFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_copy_cancelled_midway_leaves_nothing_under_the_hash_name()
    {
        Directory.CreateDirectory(_paths.DbDirectory);
        using var cts = new CancellationTokenSource();
        await using var source = new InterruptedStream(Pattern(300_000), afterBytes: 100_000, () => cts.Cancel());

        var copy = () => FileStore.CopyAtomicallyAsync(source, _store.GetPath(HashA), cts.Token);

        await copy.Should().ThrowAsync<OperationCanceledException>();
        _store.Exists(HashA).Should().BeFalse();
        DbFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task A_copy_that_fails_midway_leaves_nothing_under_the_hash_name()
    {
        Directory.CreateDirectory(_paths.DbDirectory);
        await using var source = new InterruptedStream(
            Pattern(300_000), afterBytes: 100_000, () => throw new IOException("The device is not ready."));

        var copy = () => FileStore.CopyAtomicallyAsync(source, _store.GetPath(HashA), TestContext.Current.CancellationToken);

        await copy.Should().ThrowAsync<IOException>().WithMessage("The device is not ready.");
        _store.Exists(HashA).Should().BeFalse();
        DbFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task When_the_same_content_lands_first_the_copy_counts_as_done()
    {
        Directory.CreateDirectory(_paths.DbDirectory);
        var content = Pattern(300_000);
        var final = _store.GetPath(HashA);

        // Another copy of the same hash finishes while this one is still streaming.
        await using var source = new InterruptedStream(content, afterBytes: 100_000, () => File.WriteAllBytes(final, content));

        await FileStore.CopyAtomicallyAsync(source, final, TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(final, TestContext.Current.CancellationToken)).Should().Equal(content);
        DbFiles().Should().Equal(HashA);
    }

    [Fact]
    public async Task A_temp_file_left_by_a_crash_is_not_listed_as_content_and_gets_swept()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", Pattern(10)), HashA, TestContext.Current.CancellationToken);
        var leftover = Path.Combine(_paths.DbDirectory, FileStore.TempFileName());
        await File.WriteAllBytesAsync(leftover, Pattern(50), TestContext.Current.CancellationToken);

        var names = await _store.GetAllHashedFileNamesAsync(TestContext.Current.CancellationToken);
        var swept = await _store.DeleteLeftoverTempFilesAsync(TestContext.Current.CancellationToken);

        names.Should().Equal(HashA);
        swept.Should().Be(1);
        DbFiles().Should().Equal(HashA);
    }

    [Fact]
    public async Task The_first_copy_of_a_session_sweeps_what_a_crash_left_behind()
    {
        Directory.CreateDirectory(_paths.DbDirectory);
        var leftover = Path.Combine(_paths.DbDirectory, FileStore.TempFileName());
        await File.WriteAllBytesAsync(leftover, Pattern(50), TestContext.Current.CancellationToken);

        await _store.CopyToStoreAsync(_temp.CreateFile("b.2dx", Pattern(10)), HashB, TestContext.Current.CancellationToken);

        DbFiles().Should().Equal(HashB);
    }

    [Fact]
    public async Task A_file_that_is_no_stored_content_is_neither_listed_nor_touched()
    {
        // Anything can end up in db/: a crashed link preflight probe, a file the
        // user dropped there. Listing it made the storage cleanup throw on its name.
        Directory.CreateDirectory(_paths.DbDirectory);
        var foreign = Path.Combine(_paths.DbDirectory, "notes.txt");
        await File.WriteAllTextAsync(foreign, "mine", TestContext.Current.CancellationToken);

        var names = await _store.GetAllHashedFileNamesAsync(TestContext.Current.CancellationToken);
        await _store.DeleteLeftoverTempFilesAsync(TestContext.Current.CancellationToken);

        names.Should().BeEmpty();
        File.Exists(foreign).Should().BeTrue();
    }

    [Fact]
    public async Task The_storage_cleanup_works_around_leftovers_and_counts_only_real_content()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", Pattern(10)), HashA, TestContext.Current.CancellationToken);
        await _store.CopyToStoreAsync(_temp.CreateFile("b.2dx", Pattern(20)), HashB, TestContext.Current.CancellationToken);
        var leftover = Path.Combine(_paths.DbDirectory, FileStore.TempFileName());
        var foreign = Path.Combine(_paths.DbDirectory, "notes.txt");
        await File.WriteAllBytesAsync(leftover, Pattern(5000), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(foreign, "mine", TestContext.Current.CancellationToken);

        var repository = Substitute.For<IInstanceRepository>();
        repository.GetAllHashedFileNamesAsync(Arg.Any<CancellationToken>()).Returns([HashA]);
        var dialogs = Substitute.For<IDialogService>();
        dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var cleanup = new UnusedFilesService(_store, repository, dialogs, NullLogger<UnusedFilesService>.Instance);

        var result = await cleanup.CheckAndDeleteAsync(threadCount: 2, ct: TestContext.Current.CancellationToken);

        // Only HashB is unreferenced content. The temp file goes without being
        // asked about, and the foreign file is none of the cleanup's business.
        result.Found.Should().Be(1);
        result.Deleted.Should().Be(1);
        result.FoundBytes.Should().Be(20);
        DbFiles().Should().BeEquivalentTo(HashA, "notes.txt");
    }

    [Fact]
    public void A_temp_file_name_can_never_pass_for_a_hash_name()
    {
        var name = FileStore.TempFileName();

        var asContent = () => _store.GetPath(name);

        asContent.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task A_cancelled_export_leaves_no_partial_file_at_the_destination()
    {
        await _store.CopyToStoreAsync(_temp.CreateFile("a.2dx", Pattern(1000)), HashA, TestContext.Current.CancellationToken);
        var exportDir = Path.Combine(_temp.Root, "export");
        Directory.CreateDirectory(exportDir);
        var destination = Path.Combine(exportDir, HashA);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var export = () => _store.CopyFromStoreAsync(HashA, destination, cts.Token);

        await export.Should().ThrowAsync<OperationCanceledException>();
        Directory.GetFiles(exportDir).Should().BeEmpty("a rerun skips files that exist, so a partial one would stay truncated for good");
    }

    [Fact]
    public async Task A_sweep_that_fails_for_any_reason_fails_no_copy()
    {
        // Every copy of a session awaits the same sweep task. One that faulted, with
        // whatever exception, would fail this copy and every later one.
        var paths = new PathsFailingInTheSweep(_paths);
        var store = new FileStore(paths);
        var first = _temp.CreateFile("a.2dx", Pattern(1000));
        var second = _temp.CreateFile("b.2dx", Pattern(2000));

        await store.CopyToStoreAsync(first, HashA, TestContext.Current.CancellationToken);
        await store.CopyToStoreAsync(second, HashB, TestContext.Current.CancellationToken);

        paths.Failed.Should().BeTrue("the test has to reach the sweep to mean anything");
        DbFiles().Should().BeEquivalentTo(HashA, HashB);
    }

    /// <summary>
    /// Fails the third read of the db directory with an exception that is not an I/O
    /// one. A first copy reads it to look for the hash name, reads it to create the
    /// folder, and the third read is the sweep's.
    /// </summary>
    private sealed class PathsFailingInTheSweep(IAppPaths inner) : IAppPaths
    {
        private int _reads;

        public bool Failed { get; private set; }

        public string DataDirectory => inner.DataDirectory;

        public string InstanceDirectory => inner.InstanceDirectory;

        public string DbDirectory
        {
            get
            {
                if (Interlocked.Increment(ref _reads) == 3)
                {
                    Failed = true;
                    throw new NotSupportedException("The given path's format is not supported.");
                }

                return inner.DbDirectory;
            }
        }

        public void EnsureCreated() => inner.EnsureCreated();
    }

    /// <summary>A readable stream that does something once, after a number of bytes: cancels, throws, or races.</summary>
    private sealed class InterruptedStream(byte[] content, int afterBytes, Action interrupt) : MemoryStream(content)
    {
        private bool _interrupted;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_interrupted && Position >= afterBytes)
            {
                _interrupted = true;
                interrupt();
            }

            cancellationToken.ThrowIfCancellationRequested();
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
