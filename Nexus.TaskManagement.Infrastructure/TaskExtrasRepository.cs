using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Nexus.TaskManagement.Application;
using Nexus.TaskManagement.Domain;
using Nexus.TaskManagement.Infrastructure.Configurations;

namespace Nexus.TaskManagement.Infrastructure;

public sealed class TaskExtrasRepository(TaskManagementDbContext db) : ITaskExtrasRepository
{
    public async Task<IReadOnlyList<WorkTypeDefinition>> ListWorkTypesAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.WorkTypes.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<WorkTypeDefinition?> GetWorkTypeAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.WorkTypes.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<WorkTypeDefinition?> GetWorkTypeByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.WorkTypes.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedName == normalized, cancellationToken);
    }

    public async Task AddWorkTypeAsync(WorkTypeDefinition item, CancellationToken cancellationToken) => await db.WorkTypes.AddAsync(item, cancellationToken);

    public async Task RemoveWorkTypeAsync(WorkTypeDefinition item, CancellationToken cancellationToken)
    {
        // Tasks keep everything else; only the label goes. Archived ones too.
        var tasks = await db.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == item.TenantId && t.WorkTypeId == item.Id).ToListAsync(cancellationToken);
        foreach (var task in tasks) task.SetWorkType(null);
        db.WorkTypes.Remove(item);
    }

    public async Task<IReadOnlyList<CustomFieldDefinition>> ListFieldsAsync(Guid tenantId, CancellationToken cancellationToken) =>
        await db.CustomFields.Where(x => x.TenantId == tenantId).OrderBy(x => x.SortOrder).ThenBy(x => x.Name).ToListAsync(cancellationToken);

    public Task<CustomFieldDefinition?> GetFieldAsync(Guid tenantId, Guid id, CancellationToken cancellationToken) =>
        db.CustomFields.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.Id == id, cancellationToken);

    public Task<CustomFieldDefinition?> GetFieldByNameAsync(Guid tenantId, string name, CancellationToken cancellationToken)
    {
        var normalized = name.Trim().ToUpperInvariant();
        return db.CustomFields.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.NormalizedName == normalized, cancellationToken);
    }

    public async Task AddFieldAsync(CustomFieldDefinition item, CancellationToken cancellationToken) => await db.CustomFields.AddAsync(item, cancellationToken);

    public async Task RemoveFieldAsync(CustomFieldDefinition item, CancellationToken cancellationToken)
    {
        var values = await db.TaskFieldValues.Where(v => v.TenantId == item.TenantId && v.FieldId == item.Id).ToListAsync(cancellationToken);
        db.TaskFieldValues.RemoveRange(values);
        db.CustomFields.Remove(item);
    }

    public async Task<IReadOnlyList<TaskFieldValue>> ListValuesAsync(Guid tenantId, Guid taskId, CancellationToken cancellationToken) =>
        await db.TaskFieldValues.Where(v => v.TenantId == tenantId && v.TaskId == taskId).ToListAsync(cancellationToken);

    public async Task AddValueAsync(TaskFieldValue value, CancellationToken cancellationToken) => await db.TaskFieldValues.AddAsync(value, cancellationToken);

    public Task RemoveValueAsync(TaskFieldValue value, CancellationToken cancellationToken)
    {
        db.TaskFieldValues.Remove(value);
        return Task.CompletedTask;
    }
}

internal sealed class WorkTypeDefinitionConfiguration : IEntityTypeConfiguration<WorkTypeDefinition>
{
    public void Configure(EntityTypeBuilder<WorkTypeDefinition> builder)
    {
        builder.ToTable("WorkTypes", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Color).HasMaxLength(30);
        builder.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
    }
}

internal sealed class CustomFieldDefinitionConfiguration : IEntityTypeConfiguration<CustomFieldDefinition>
{
    public void Configure(EntityTypeBuilder<CustomFieldDefinition> builder)
    {
        builder.ToTable("CustomFields", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.OptionsJson).HasMaxLength(4000);
        builder.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
    }
}

internal sealed class TaskFieldValueConfiguration : IEntityTypeConfiguration<TaskFieldValue>
{
    public void Configure(EntityTypeBuilder<TaskFieldValue> builder)
    {
        builder.ToTable("TaskFieldValues", Schema.Name);
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Value).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => new { x.TaskId, x.FieldId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.FieldId });
    }
}
