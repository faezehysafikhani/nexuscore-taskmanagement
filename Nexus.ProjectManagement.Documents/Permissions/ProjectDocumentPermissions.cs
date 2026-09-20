using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Documents.Permissions;

public static class ProjectDocumentPermissions
{
    public const string View = "ProjectDocuments.View";
    public const string Upload = "ProjectDocuments.Upload";
    public const string Edit = "ProjectDocuments.Edit";
    public const string Delete = "ProjectDocuments.Delete";
    public const string Submit = "ProjectDocuments.Submit";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "ProjectDocuments", "View and download project documents"),
        new(Upload, "ProjectDocuments", "Upload project documents"),
        new(Edit, "ProjectDocuments", "Edit project document metadata"),
        new(Delete, "ProjectDocuments", "Delete project documents"),
        new(Submit, "ProjectDocuments", "Submit project documents for approval")
    ];
}

public sealed class ProjectDocumentPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectDocumentPermissions.All;
}
