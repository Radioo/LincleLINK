using Microsoft.Data.Sqlite;

namespace LincleLINK.Core.Infrastructure.Persistence;

/// <summary>
/// Shared connection-string and normalization helpers for the SQLite metadata DB.
/// </summary>
public static class LincleLinkPersistence
{
    /// <summary>Name of the SQLite database file, at the data root (never inside <c>db/</c>).</summary>
    public const string DatabaseFileName = "linclelink.db";

    /// <summary>
    /// Connection string for the data directory. A 30s default timeout maps to
    /// SQLite's busy timeout, so a briefly locked DB (a single-user desktop app)
    /// waits instead of erroring.
    /// </summary>
    public static string ConnectionStringFor(string dataDirectory)
        => new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, DatabaseFileName),
            DefaultTimeout = 30,
        }.ToString();

    /// <summary>
    /// Case-folding key backing case-insensitive uniqueness. Uses invariant
    /// uppercase so lookups behave like the JSON repository's
    /// <see cref="System.StringComparison.OrdinalIgnoreCase"/> for the same names,
    /// including non-ASCII ones (SQLite <c>NOCASE</c> is ASCII-only).
    /// </summary>
    public static string NameKeyOf(string instanceName) => instanceName.ToUpperInvariant();
}
