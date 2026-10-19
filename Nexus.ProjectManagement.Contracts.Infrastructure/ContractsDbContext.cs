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
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ContractMilestone> ContractMilestones => Set<ContractMilestone>();
    public DbSet<ContractObligation> ContractObligations => Set<ContractObligation>();
    public DbSet<ContractLink> ContractLinks => Set<ContractLink>();
    public DbSet<SupplierEvaluation> SupplierEvaluations => Set<SupplierEvaluation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ContractsDbContext).Assembly);
    }
}
