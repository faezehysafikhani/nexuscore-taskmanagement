using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace Nexus.OrgStructure.Endpoints;

/// <summary>
/// The dynamic structure's nodes, node types and «what may sit under what», and the impact preview
/// (/api/org-structure). Each endpoint asks for one operation's permission, which the older
/// permission covering it also passes (OrgStructurePermissions.NodePolicies). The endpoints that
/// already existed (/api/organization/units, /company-units, /chart...) are unchanged.
/// </summary>
public static class OrgNodeEndpoints
{
    public static IEndpointRouteBuilder MapOrgNodeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/org-structure").WithTags("Organization structure: nodes").RequireAuthorization();

        group.MapGet("/nodes", (Guid? companyId, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListNodesAsync(tenant, companyId, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapGet("/nodes/search", (string? q, Guid? companyId, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SearchAsync(tenant, q, companyId, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapGet("/nodes/{id:guid}", (Guid id, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetNodeAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapGet("/nodes/{id:guid}/ancestors", (Guid id, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetAncestorsAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/nodes", (CreateNodeRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreateNodeAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeCreate);

        group.MapPut("/nodes/{id:guid}", (Guid id, UpdateNodeRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdateNodeAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeUpdate);

        group.MapPut("/nodes/{id:guid}/manager", (Guid id, SetNodeManagerRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetManagerAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeAssign);

        group.MapPut("/nodes/{id:guid}/details", (Guid id, SetNodeDetailsRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetDetailsAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeUpdate);

        // --- dates, merge, the log and undo (phase 6.4)
        group.MapPut("/nodes/{id:guid}/validity", (Guid id, NodeValidityRequest request, ICurrentUserContext user, IOrgNodeHistoryService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetValidityAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeUpdate);

        group.MapPost("/nodes/{id:guid}/merge", (Guid id, MergeNodeRequest request, ICurrentUserContext user, IOrgNodeHistoryService service, CancellationToken ct) =>
                InTenant(user, tenant => service.MergeAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeMove);

        group.MapGet("/nodes/{id:guid}/changes", (Guid id, int? take, ICurrentUserContext user, IOrgNodeHistoryService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListChangesAsync(tenant, id, take ?? 50, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/nodes/{id:guid}/undo", (Guid id, NodeVersionRequest? request, ICurrentUserContext user, IOrgNodeHistoryService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UndoLastChangeAsync(tenant, id, request ?? new NodeVersionRequest(), ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeMove);

        group.MapPost("/nodes/{id:guid}/move", (Guid id, MoveNodeRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.MoveNodeAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeMove);

        group.MapPost("/nodes/{id:guid}/reorder", (Guid id, ReorderNodeRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ReorderNodeAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeMove);

        group.MapPost("/nodes/{id:guid}/deactivate", (Guid id, NodeVersionRequest? request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeactivateNodeAsync(tenant, id, request ?? new NodeVersionRequest(), ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeDeactivate);

        group.MapPost("/nodes/{id:guid}/activate", (Guid id, NodeVersionRequest? request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ActivateNodeAsync(tenant, id, request ?? new NodeVersionRequest(), ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeDeactivate);

        // --- node types and what may sit under what
        group.MapGet("/node-types", (ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListNodeTypesAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/node-types", (CreateNodeTypeRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreateNodeTypeAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapPut("/node-types/{id:guid}", (Guid id, UpdateNodeTypeRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdateNodeTypeAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapDelete("/node-types/{id:guid}", (Guid id, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeleteNodeTypeAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapGet("/node-type-rules", (ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetRulesAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPut("/node-type-rules", (SetNodeTypeRulesRequest request, ICurrentUserContext user, IOrgNodeService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetRulesAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        // --- secondary links (phase 4): their types, the automatic rules and the links themselves
        group.MapGet("/link-types", (ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListLinkTypesAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/link-types", (CreateLinkTypeRequest request, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreateLinkTypeAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapPut("/link-types/{id:guid}", (Guid id, UpdateLinkTypeRequest request, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.UpdateLinkTypeAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapDelete("/link-types/{id:guid}", (Guid id, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeleteLinkTypeAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapGet("/auto-link-rules", (ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListAutoLinkRulesAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPut("/auto-link-rules", (SetAutoLinkRulesRequest request, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetAutoLinkRulesAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapPost("/auto-link-rules/preview", (ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.PlanAutoLinksAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/auto-link-rules/apply", (ApplyAutoLinksRequest request, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ApplyAutoLinksAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapGet("/links", (Guid? nodeId, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListLinksAsync(tenant, nodeId, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/links", (CreateLinkRequest request, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.CreateLinkAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeUpdate);

        group.MapDelete("/links/{id:guid}", (Guid id, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeleteLinkAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeUpdate);

        group.MapPut("/companies/{id:guid}/ui-level", (Guid id, SetUiLevelRequest request, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetCompanyUiLevelAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        // A user's extra managers (through links that count as manager): their own, or anyone's with a read permission.
        group.MapGet("/users/{userId:guid}/extra-managers", (Guid userId, ICurrentUserContext user, IOrgLinkService service, CancellationToken ct) =>
                SelfOrReader(user, userId) ?? InTenant(user, tenant => service.GetExtraManagersAsync(tenant, userId, ct)));

        // --- structure templates: the built-in ones and those saved from a company (a company is made from one with POST /api/org-structure/companies and templateId)
        group.MapGet("/templates", (ICurrentUserContext user, IOrgTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ListTemplatesAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapGet("/templates/{id:guid}", (Guid id, ICurrentUserContext user, IOrgTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetTemplateAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPost("/templates", (SaveStructureTemplateRequest request, ICurrentUserContext user, IOrgTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SaveCompanyAsTemplateAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageTemplates);

        group.MapDelete("/templates/{id:guid}", (Guid id, ICurrentUserContext user, IOrgTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.DeleteTemplateAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageTemplates);

        // --- posts from a pattern (phase 6.1): the manager post, the posts each node type gets, applying them
        group.MapPut("/unit-positions/{id:guid}/manager-post", (Guid id, SetManagerPostRequest request, ICurrentUserContext user, IOrgPostTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetManagerPostAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        group.MapGet("/node-types/{id:guid}/posts", (Guid id, ICurrentUserContext user, IOrgPostTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetNodeTypePostsAsync(tenant, id, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        group.MapPut("/node-types/{id:guid}/posts", (Guid id, SetNodeTypePostsRequest request, ICurrentUserContext user, IOrgPostTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetNodeTypePostsAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageRules);

        group.MapPost("/post-templates/apply", (ApplyPostTemplatesRequest request, ICurrentUserContext user, IOrgPostTemplateService service, CancellationToken ct) =>
                InTenant(user, tenant => service.ApplyPostTemplatesAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManagePositions);

        // --- import from a sheet (phase 6.2): multipart form with "file" (.xlsx or .csv) and companyId or parentNodeId
        group.MapPost("/import/preview", (HttpRequest http, ICurrentUserContext user, IOrgImportService service, CancellationToken ct) =>
                ImportAsync(http, user, (tenant, target, rows) => service.PreviewAsync(tenant, target, rows, ct), ct))
            .RequireAuthorization(OrgStructurePermissions.NodeCreate);

        group.MapPost("/import/commit", (HttpRequest http, ICurrentUserContext user, IOrgImportService service, CancellationToken ct) =>
                ImportAsync(http, user, (tenant, target, rows) => service.CommitAsync(tenant, target, rows, ct), ct))
            .RequireAuthorization(OrgStructurePermissions.NodeCreate);

        // --- company groups (phase 6.3)
        group.MapPut("/companies/{id:guid}/parent", (Guid id, SetParentCompanyRequest request, ICurrentUserContext user, IOrgCompanyGroupService service, CancellationToken ct) =>
                InTenant(user, tenant => service.SetParentCompanyAsync(tenant, id, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.ManageCompanies);

        group.MapGet("/company-groups", (ICurrentUserContext user, IOrgCompanyGroupService service, CancellationToken ct) =>
                InTenant(user, tenant => service.GetCompanyGroupsAsync(tenant, ct)))
            .RequireAuthorization(OrgStructurePermissions.ReadPolicy);

        // --- what a structure change would do, before it is made (nothing is saved)
        group.MapPost("/impact-preview", (ImpactPreviewRequest request, ICurrentUserContext user, IOrgImpactPreview service, CancellationToken ct) =>
                InTenant(user, tenant => service.PreviewAsync(tenant, request, ct)))
            .RequireAuthorization(OrgStructurePermissions.NodeView);

        return app;
    }

    private static async Task<IResult> InTenant<T>(ICurrentUserContext user, Func<Guid, Task<Result<T>>> action) =>
        user.TenantId is { } tenant ? (await action(tenant)).ToApiResult() : Results.Unauthorized();

    private static async Task<IResult> InTenant(ICurrentUserContext user, Func<Guid, Task<Result>> action) =>
        user.TenantId is { } tenant ? (await action(tenant)).ToApiResult() : Results.Unauthorized();

    public const long MaxImportFileBytes = 5 * 1024 * 1024;

    /// <summary>Reads the uploaded sheet and where its nodes go, then runs the import step.</summary>
    private static async Task<IResult> ImportAsync<T>(
        HttpRequest http, ICurrentUserContext user, Func<Guid, ImportTargetRequest, IReadOnlyList<ExcelStructureRow>, Task<Result<T>>> step, CancellationToken ct)
    {
        if (user.TenantId is not { } tenant)
        {
            return Results.Unauthorized();
        }

        if (!http.HasFormContentType)
        {
            return Result.Failure<T>(Error.Validation("فایل را به‌صورت multipart/form-data با نام file بفرستید.")).ToApiResult();
        }

        var form = await http.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null || file.Length == 0)
        {
            return Result.Failure<T>(Error.Validation("فایلی فرستاده نشد.")).ToApiResult();
        }

        if (file.Length > MaxImportFileBytes)
        {
            return Result.Failure<T>(Error.Validation("حجم فایل حداکثر ۵ مگابایت است.")).ToApiResult();
        }

        static Guid? Id(string? value) => Guid.TryParse(value, out var id) ? id : null;
        await using var content = new MemoryStream();
        await file.CopyToAsync(content, ct);
        content.Position = 0;
        var (rows, problem) = OrgSheetReader.Read(content, file.FileName);
        if (problem is not null)
        {
            return Result.Failure<T>(Error.Validation(problem)).ToApiResult();
        }

        var target = new ImportTargetRequest(Id(form["companyId"]), Id(form["parentNodeId"]));
        return (await step(tenant, target, rows)).ToApiResult();
    }

    /// <summary>Null when the caller may read this user's place: it is themselves, or they hold a read permission.</summary>
    private static Task<IResult>? SelfOrReader(ICurrentUserContext user, Guid userId) =>
        user.UserId == userId || OrgStructurePermissions.All.Any(p => user.HasPermission(p.Name))
            ? null
            : Task.FromResult(Results.Forbid());
}
