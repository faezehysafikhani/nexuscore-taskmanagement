using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.ProjectManagement.Contracts.Domain;

namespace Nexus.ProjectManagement.Contracts.Infrastructure.Configurations;

public sealed class ContractConfiguration : IEntityTypeConfiguration<Contract>
{
    public void Configure(EntityTypeBuilder<Contract> builder)
    {
        builder.ToTable("Contracts", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ContractNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Counterparty).HasMaxLength(300).IsRequired();
        builder.Property(x => x.SupervisorName).HasMaxLength(300);
        builder.Property(x => x.BeneficiaryName).HasMaxLength(300);
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.ScopeOfWork).HasMaxLength(4000);
        builder.Property(x => x.OriginalAmount).HasColumnType("decimal(19,0)");

        // A project never has two contracts with the same number.
        builder.HasIndex(x => new { x.ProjectId, x.ContractNumber }).IsUnique();
        builder.HasIndex(x => x.SupplierId);
    }
}

public sealed class ContractMilestoneConfiguration : IEntityTypeConfiguration<ContractMilestone>
{
    public void Configure(EntityTypeBuilder<ContractMilestone> builder)
    {
        builder.ToTable("ContractMilestones", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasIndex(x => x.ContractId);
    }
}

public sealed class ContractObligationConfiguration : IEntityTypeConfiguration<ContractObligation>
{
    public void Configure(EntityTypeBuilder<ContractObligation> builder)
    {
        builder.ToTable("ContractObligations", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.HasIndex(x => x.ContractId);
    }
}

public sealed class ContractLinkConfiguration : IEntityTypeConfiguration<ContractLink>
{
    public void Configure(EntityTypeBuilder<ContractLink> builder)
    {
        builder.ToTable("ContractLinks", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasIndex(x => x.ContractId);
        builder.HasIndex(x => x.SubjectId);
    }
}

public sealed class SupplierEvaluationConfiguration : IEntityTypeConfiguration<SupplierEvaluation>
{
    public void Configure(EntityTypeBuilder<SupplierEvaluation> builder)
    {
        builder.ToTable("SupplierEvaluations", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.OverallScore).HasColumnType("decimal(4,2)");
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.HasIndex(x => x.SupplierId);
        builder.HasIndex(x => x.TenantId);
    }
}

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.NationalId).HasMaxLength(50);
        builder.Property(x => x.ContactPerson).HasMaxLength(200);
        builder.Property(x => x.Phone).HasMaxLength(50);
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(1000);
        builder.Property(x => x.Description).HasMaxLength(4000);

        // One supplier per name (in its normalised form) within a tenant.
        builder.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
    }
}

public sealed class ContractAddendumConfiguration : IEntityTypeConfiguration<ContractAddendum>
{
    public void Configure(EntityTypeBuilder<ContractAddendum> builder)
    {
        builder.ToTable("ContractAddenda", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.AmountChange).HasColumnType("decimal(19,0)");

        builder.HasIndex(x => new { x.ContractId, x.Number }).IsUnique();
    }
}

public sealed class ContractInvoiceConfiguration : IEntityTypeConfiguration<ContractInvoice>
{
    public void Configure(EntityTypeBuilder<ContractInvoice> builder)
    {
        builder.ToTable("ContractInvoices", "project_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(2000);
        builder.Property(x => x.Title).HasMaxLength(300);
        builder.Property(x => x.Amount).HasColumnType("decimal(19,0)");
        builder.Property(x => x.PaidAmount).HasColumnType("decimal(19,0)");

        builder.HasIndex(x => new { x.ContractId, x.InvoiceNumber }).IsUnique();
    }
}
