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
        new(View, "Knowledge", "Search and view knowledge documents"),
        new(Upload, "Knowledge", "Upload knowledge documents"),
        new(Edit, "Knowledge", "Edit knowledge document metadata"),
        new(Delete, "Knowledge", "Delete knowledge documents")
    ];
}

public sealed class KnowledgePermissionCatalog : IPermissionCatalog
{
    public IReadOnlyList<PermissionDefinition> GetPermissions() => KnowledgePermissions.All;
}
