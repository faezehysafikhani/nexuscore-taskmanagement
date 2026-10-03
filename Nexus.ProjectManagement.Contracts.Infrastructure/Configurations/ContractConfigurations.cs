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
        builder.Property(x => x.Description).HasMaxLength(4000);
        builder.Property(x => x.OriginalAmount).HasColumnType("decimal(19,0)");

        // A project never has two contracts with the same number.
        builder.HasIndex(x => new { x.ProjectId, x.ContractNumber }).IsUnique();
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
        builder.Property(x => x.Amount).HasColumnType("decimal(19,0)");
        builder.Property(x => x.PaidAmount).HasColumnType("decimal(19,0)");

        builder.HasIndex(x => new { x.ContractId, x.InvoiceNumber }).IsUnique();
    }
}
