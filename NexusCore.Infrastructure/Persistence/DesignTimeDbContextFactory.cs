using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NexusCore.Infrastructure.Persistence;

// Model generation never starts the application or its database-writing hosted services.
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NexusCoreDbContext>
{
    public NexusCoreDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<NexusCoreDbContext>()
            .UseSqlServer("Server=(localdb)\\MSSQLLocalDB;Database=NexusCoreDesignTime;Trusted_Connection=True;TrustServerCertificate=True")
            .Options);
}
