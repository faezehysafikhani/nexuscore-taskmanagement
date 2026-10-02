using NexusCore.Application.Identity.Permissions;

namespace Nexus.Knowledge.Permissions;

public static class KnowledgePermissions
{
    public const string View = "Knowledge.View";
    public const string Upload = "Knowledge.Upload";
    public const string Edit = "Knowledge.Edit";
    public const string Delete = "Knowledge.Delete";

    public static IReadOnlyList<PermissionDefinition> All { get; } =
    [
        new(View, "Knowledge", "جستجو و مشاهده مستندات دانشی"),
        new(Upload, "Knowledge", "بارگذاری مستندات دانشی"),
        new(Edit, "Knowledge", "ویرایش مشخصات مستندات دانشی"),
        new(Delete, "Knowledge", "حذف مستندات دانشی")
    ];
}

public sealed class KnowledgePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => KnowledgePermissions.All;
}
