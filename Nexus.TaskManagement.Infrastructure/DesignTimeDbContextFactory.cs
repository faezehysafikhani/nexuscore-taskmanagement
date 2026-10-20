using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Nexus.TaskManagement.Infrastructure;

/// <summary>
/// Lets `dotnet ef migrations add` run against this project directly, without needing a host
/// project wired up with a real connection string. Only used by the EF CLI at design time.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TaskManagementDbContext>
{
    public TaskManagementDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<TaskManagementDbContext>();
        builder.UseSqlServer("Server=.;Database=NexusCore_DesignTime;Trusted_Connection=True;TrustServerCertificate=True;");
        return new TaskManagementDbContext(builder.Options);
    }
}
