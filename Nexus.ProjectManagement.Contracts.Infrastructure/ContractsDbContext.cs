using Microsoft.EntityFrameworkCore;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Infrastructure;

public sealed class ContractsDbContext(DbContextOptions<ContractsDbContext> options)
    : DbContext(options), IContractsUnitOfWork
{
    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractAddendum> ContractAddenda => Set<ContractAddendum>();
    public DbSet<ContractInvoice> ContractInvoices => Set<ContractInvoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractsDbContext).Assembly);
    }
}
