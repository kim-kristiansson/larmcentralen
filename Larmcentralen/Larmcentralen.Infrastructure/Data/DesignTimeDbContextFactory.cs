using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Larmcentralen.Infrastructure.Data;

/// <summary>Used by <c>dotnet ef</c> so migrations can be created without starting the API.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=larmcentralen.db")
            .Options;
        return new AppDbContext(options);
    }
}
