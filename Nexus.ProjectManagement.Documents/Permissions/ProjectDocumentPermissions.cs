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
        new(View, "ProjectDocuments", "مشاهده و دریافت مستندات پروژه"),
        new(Upload, "ProjectDocuments", "بارگذاری مستندات پروژه"),
        new(Edit, "ProjectDocuments", "ویرایش مشخصات مستندات پروژه"),
        new(Delete, "ProjectDocuments", "حذف مستندات پروژه"),
        new(Submit, "ProjectDocuments", "ارسال مستندات پروژه برای تأیید")
    ];
}

public sealed class ProjectDocumentPermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => ProjectDocumentPermissions.All;
}
