using Microsoft.EntityFrameworkCore;
using Nexus.Knowledge.Application;
using Nexus.Knowledge.Domain;

namespace Nexus.Knowledge.Infrastructure;

public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options)
    : DbContext(options), IKnowledgeUnitOfWork
{
    public DbSet<KnowledgeDocument> KnowledgeDocuments => Set<KnowledgeDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KnowledgeDbContext).Assembly);
    }
}
