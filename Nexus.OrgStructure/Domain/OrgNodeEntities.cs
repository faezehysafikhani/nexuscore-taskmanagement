using NexusCore.SharedKernel.Domain;

namespace Nexus.OrgStructure.Domain;

// The dynamic structure's own tables (schema org_structure). Plain data for now: nothing reads
// or writes them but the repository, so the structure behaves exactly as before.

/// <summary>A kind of node («معاونت»، «اداره کل»، «کمیته»...). Editable data, not code.</summary>
public sealed class NodeType : AuditableEntity<Guid>
{
    private NodeType() : base(Guid.Empty)
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public NodeType(Guid id, Guid tenantId, string code, string name, bool isDomainBoundary = true, int sortOrder = 0) : base(id)
    {
        TenantId = tenantId;
        Code = code.Trim();
        Name = name.Trim();
        IsDomainBoundary = isDomainBoundary;
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }

    /// <summary>Whether a node of this type is the boundary of «the same deputy»: its people are one team scope.</summary>
    public bool IsDomainBoundary { get; private set; }

    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string name, bool isDomainBoundary, int sortOrder, bool isActive)
    {
        Name = name.Trim();
        IsDomainBoundary = isDomainBoundary;
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>What may go under what: a node of the child type may sit under a node of the parent type (null parent: directly under the company).</summary>
public sealed class NodeTypeRule : AuditableEntity<Guid>
{
    private NodeTypeRule() : base(Guid.Empty)
    {
    }

    public NodeTypeRule(Guid id, Guid tenantId, Guid? parentNodeTypeId, Guid childNodeTypeId) : base(id)
    {
        TenantId = tenantId;
        ParentNodeTypeId = parentNodeTypeId;
        ChildNodeTypeId = childNodeTypeId;
    }

    public Guid TenantId { get; private set; }
    public Guid? ParentNodeTypeId { get; private set; }
    public Guid ChildNodeTypeId { get; private set; }
}

/// <summary>A post every node of a type starts with (e.g. the manager's post of a «معاونت»).</summary>
public sealed class NodeTypePostTemplate : AuditableEntity<Guid>
{
    private NodeTypePostTemplate() : base(Guid.Empty)
    {
    }

    public NodeTypePostTemplate(Guid id, Guid tenantId, Guid nodeTypeId, Guid positionId, bool isManagerPost, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        NodeTypeId = nodeTypeId;
        PositionId = positionId;
        IsManagerPost = isManagerPost;
        SortOrder = sortOrder;
    }

    public Guid TenantId { get; private set; }
    public Guid NodeTypeId { get; private set; }
    public Guid PositionId { get; private set; }
    public bool IsManagerPost { get; private set; }
    public int SortOrder { get; private set; }
}

/// <summary>A kind of secondary relation between nodes (dotted / matrix line).</summary>
public sealed class LinkType : AuditableEntity<Guid>
{
    private LinkType() : base(Guid.Empty)
    {
        Code = string.Empty;
        Name = string.Empty;
        LineStyle = "dashed";
    }

    public LinkType(Guid id, Guid tenantId, string code, string name, bool countsAsManager, string lineStyle) : base(id)
    {
        TenantId = tenantId;
        Code = code.Trim();
        Name = name.Trim();
        CountsAsManager = countsAsManager;
        LineStyle = lineStyle.Trim();
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }

    /// <summary>Whether the other end counts as a manager. A dotted line never does unless this is set.</summary>
    public bool CountsAsManager { get; private set; }

    /// <summary>How the chart draws it: «solid»، «dashed»، «dotted».</summary>
    public string LineStyle { get; private set; }

    public bool IsActive { get; private set; }
}

/// <summary>A relation made automatically when a node of the source type is put under / next to one of the target type.</summary>
public sealed class AutoLinkRule : AuditableEntity<Guid>
{
    private AutoLinkRule() : base(Guid.Empty)
    {
    }

    public AutoLinkRule(Guid id, Guid tenantId, Guid sourceNodeTypeId, Guid targetNodeTypeId, Guid linkTypeId) : base(id)
    {
        TenantId = tenantId;
        SourceNodeTypeId = sourceNodeTypeId;
        TargetNodeTypeId = targetNodeTypeId;
        LinkTypeId = linkTypeId;
    }

    public Guid TenantId { get; private set; }
    public Guid SourceNodeTypeId { get; private set; }
    public Guid TargetNodeTypeId { get; private set; }
    public Guid LinkTypeId { get; private set; }
}

/// <summary>A secondary relation from one node to another, beside the node's main parent.</summary>
public sealed class OrgLink : AuditableEntity<Guid>
{
    private OrgLink() : base(Guid.Empty)
    {
    }

    public OrgLink(Guid id, Guid tenantId, Guid fromUnitId, Guid toUnitId, Guid linkTypeId) : base(id)
    {
        TenantId = tenantId;
        FromUnitId = fromUnitId;
        ToUnitId = toUnitId;
        LinkTypeId = linkTypeId;
    }

    public Guid TenantId { get; private set; }
    public Guid FromUnitId { get; private set; }
    public Guid ToUnitId { get; private set; }
    public Guid LinkTypeId { get; private set; }
}

/// <summary>A ready structure a company can be made from (copied when the company is created).</summary>
public sealed class StructureTemplate : AuditableEntity<Guid>
{
    private StructureTemplate() : base(Guid.Empty)
    {
        Code = string.Empty;
        Name = string.Empty;
    }

    public StructureTemplate(Guid id, Guid tenantId, string code, string name, string? description, bool isSystem) : base(id)
    {
        TenantId = tenantId;
        Code = code.Trim();
        Name = name.Trim();
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        IsSystem = isSystem;
    }

    public Guid TenantId { get; private set; }
    public string Code { get; private set; }
    public string Name { get; private set; }
    public string? Description { get; private set; }
    public bool IsSystem { get; private set; }
}

/// <summary>One node of a template. ParentKey points at another item's Key of the same template (null: top).</summary>
public sealed class StructureTemplateItem : AuditableEntity<Guid>
{
    private StructureTemplateItem() : base(Guid.Empty)
    {
        Key = string.Empty;
        Name = string.Empty;
    }

    public StructureTemplateItem(Guid id, Guid tenantId, Guid templateId, string key, string? parentKey, string? nodeTypeCode, string name, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        TemplateId = templateId;
        Key = key.Trim();
        ParentKey = string.IsNullOrWhiteSpace(parentKey) ? null : parentKey.Trim();
        NodeTypeCode = string.IsNullOrWhiteSpace(nodeTypeCode) ? null : nodeTypeCode.Trim();
        Name = name.Trim();
        SortOrder = sortOrder;
    }

    public Guid TenantId { get; private set; }
    public Guid TemplateId { get; private set; }
    public string Key { get; private set; }
    public string? ParentKey { get; private set; }
    public string? NodeTypeCode { get; private set; }
    public string Name { get; private set; }
    public int SortOrder { get; private set; }
}

/// <summary>Who changed what in the structure, and when.</summary>
public sealed class OrgChangeLog : AuditableEntity<Guid>
{
    private OrgChangeLog() : base(Guid.Empty)
    {
        EntityType = string.Empty;
        Action = string.Empty;
    }

    public OrgChangeLog(Guid id, Guid tenantId, string entityType, Guid entityId, string action, Guid? userId, DateTimeOffset atUtc, string? details) : base(id)
    {
        TenantId = tenantId;
        EntityType = entityType.Trim();
        EntityId = entityId;
        Action = action.Trim();
        UserId = userId;
        AtUtc = atUtc;
        Details = details;
    }

    public Guid TenantId { get; private set; }
    public string EntityType { get; private set; }
    public Guid EntityId { get; private set; }
    public string Action { get; private set; }
    public Guid? UserId { get; private set; }
    public DateTimeOffset AtUtc { get; private set; }

    /// <summary>What changed (JSON or text).</summary>
    public string? Details { get; private set; }
}
