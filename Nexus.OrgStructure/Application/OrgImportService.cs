using Nexus.OrgStructure.Application.Dtos;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Application;

/// <summary>
/// Importing nodes from a sheet (phase 6.2): preview (nothing is made) and commit (all or nothing).
/// Commit makes the nodes, then the automatic links, then the posts (the sheet's manager posts and
/// each type's posts). Only new nodes: a code the company already has is an error.
/// </summary>
public interface IOrgImportService
{
    Task<Result<ImportPreviewDto>> PreviewAsync(Guid tenantId, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken);
    Task<Result<ImportResultDto>> CommitAsync(Guid tenantId, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken);
}

public sealed class OrgImportService(OrgNodeService nodes, OrgStructureService structure) : IOrgImportService
{
    public async Task<Result<ImportPreviewDto>> PreviewAsync(Guid tenantId, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken)
    {
        var plan = await nodes.PlanImportAsync(tenantId, target, rows, cancellationToken);
        if (!plan.IsSuccess)
        {
            return Result.Failure<ImportPreviewDto>(plan.Error);
        }

        var count = rows.Count(row => !string.IsNullOrWhiteSpace(row.Code) || !string.IsNullOrWhiteSpace(row.Name) || !string.IsNullOrWhiteSpace(row.ParentCode));
        return Result.Success(new ImportPreviewDto(
            count,
            plan.Value!.Errors.Count > 0 ? [] : plan.Value.Items.Select(item => new ImportNodePreviewDto(item.Row, item.Code, item.Name, item.ParentCode, item.TypeName, item.ManagerPositionTitle, item.Depth)).ToList(),
            plan.Value.Errors));
    }

    public async Task<Result<ImportResultDto>> CommitAsync(Guid tenantId, ImportTargetRequest target, IReadOnlyList<ExcelStructureRow> rows, CancellationToken cancellationToken)
    {
        var imported = await nodes.ImportNodesAsync(tenantId, target, rows, cancellationToken);
        if (!imported.IsSuccess)
        {
            return Result.Failure<ImportResultDto>(imported.Error);
        }

        var (made, choices, links) = imported.Value;
        var (posts, skipped) = await structure.MakeImportPostsAsync(tenantId, made.Select(m => (m.Unit.Id, m.ManagerPositionId)).ToList(), cancellationToken);
        // One read for all of them (a sheet may have hundreds of rows).
        var ids = made.Select(m => m.Unit.Id).ToHashSet();
        var all = await nodes.ListNodesAsync(tenantId, null, cancellationToken);
        var list = all.IsSuccess ? all.Value!.Where(node => ids.Contains(node.Id)).ToList() : [];

        return Result.Success(new ImportResultDto(list, links, posts, choices, skipped));
    }
}
