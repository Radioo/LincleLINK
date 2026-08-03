using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LincleLINK.Core.Infrastructure.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> scaffold migrations against Core without launching the
/// Avalonia app (plan 13 §6). Only used at design time; the path is irrelevant to
/// the generated migration/snapshot.
/// </summary>
public sealed class LincleLinkDbContextFactory : IDesignTimeDbContextFactory<LincleLinkDbContext>
{
    public LincleLinkDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<LincleLinkDbContext>()
            .UseSqlite(LincleLinkPersistence.ConnectionStringFor(Path.GetTempPath()))
            .Options;
        return new LincleLinkDbContext(options);
    }
}
