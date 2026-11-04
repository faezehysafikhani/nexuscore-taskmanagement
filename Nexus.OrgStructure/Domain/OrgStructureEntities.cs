using NexusCore.SharedKernel.Domain;

namespace Nexus.OrgStructure.Domain;

/// <summary>
/// A company inside a tenant. Several companies can share one tenant (a holding and its
/// subsidiaries): their people can see and work with each other, unlike two tenants.
/// </summary>
public sealed class Company : AuditableEntity<Guid>
{
    private Company() : base(Guid.Empty)
    {
        Name = string.Empty;
        Code = string.Empty;
    }

    public Company(Guid id, Guid tenantId, string name, string code, string? description, Guid? managerUserId) : base(id)
    {
        TenantId = tenantId;
        Name = name.Trim();
        Code = code.Trim();
        Description = Normalize(description);
        ManagerUserId = managerUserId;
        IsActive = true;
        UiLevel = OrgUiLevel.Advanced;
    }

    public Guid TenantId { get; private set; }
    public string Name { get; private set; }
    public string Code { get; private set; }
    public string? Description { get; private set; }

    /// <summary>
    /// The company's head (e.g. its CEO): the manager of whoever has no other manager in it. Not
    /// chosen on the company: whoever is placed directly in the company with its position that has
    /// no superior becomes it (see OrgStructureService).
    /// </summary>
    public Guid? ManagerUserId { get; private set; }

    public bool IsActive { get; private set; }

    /// <summary>The parent company in a group (holding); null: a company of its own.</summary>
    public Guid? ParentCompanyId { get; private set; }

    /// <summary>The structure template the company was made from (a copy; later edits do not change it).</summary>
    public Guid? TemplateId { get; private set; }

    /// <summary>How much of the structure tools the screens show (only hides things in the UI).</summary>
    public OrgUiLevel UiLevel { get; private set; }

    public void SetGroup(Guid? parentCompanyId, Guid? templateId)
    {
        ParentCompanyId = parentCompanyId;
        TemplateId = templateId;
    }

    public void SetUiLevel(OrgUiLevel level) => UiLevel = level;

    public void Update(string name, string code, string? description, bool isActive)
    {
        Name = name.Trim();
        Code = code.Trim();
        Description = Normalize(description);
        IsActive = isActive;
    }

    public void SetHead(Guid? userId) => ManagerUserId = userId;

    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Website { get; private set; }
    public string? NationalId { get; private set; }
    public string? EconomicCode { get; private set; }
    public string? Address { get; private set; }

    /// <summary>What the chart calls the company's head (e.g. «مدیرعامل»); null: «رئیس کل».</summary>
    public string? HeadTitle { get; private set; }

    /// <summary>The logo in the file storage: "storageKey|contentType"; null: none.</summary>
    public string? Logo { get; private set; }

    /// <summary>Replaces all the optional details at once (empty means none).</summary>
    public void SetDetails(string? phone, string? email, string? website, string? nationalId, string? economicCode, string? address, string? headTitle)
    {
        Phone = Normalize(phone);
        Email = Normalize(email);
        Website = Normalize(website);
        NationalId = Normalize(nationalId);
        EconomicCode = Normalize(economicCode);
        Address = Normalize(address);
        HeadTitle = Normalize(headTitle);
    }

    public void SetLogo(string? logo) => Logo = Normalize(logo);

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Screen detail level of a company. The default (0) is Normal, so existing rows need no value.</summary>
public enum OrgUiLevel
{
    Normal = 0,
    Simple = 1,
    Advanced = 2,
}

/// <summary>
/// Puts an organization unit (a deputy, from Nexus.Organization) and everything under it in a
/// company. Only the top unit of a company's branch is linked; its sub-units belong to the same
/// company through their parent.
/// </summary>
public sealed class CompanyUnit : AuditableEntity<Guid>
{
    private CompanyUnit() : base(Guid.Empty)
    {
    }

    public CompanyUnit(Guid id, Guid tenantId, Guid companyId, Guid unitId) : base(id)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        UnitId = unitId;
    }

    public Guid TenantId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UnitId { get; private set; }

    public void MoveTo(Guid companyId) => CompanyId = companyId;
}

/// <summary>A job title (سِمَت) such as «کارشناس» or «معاون». Not a permission role.</summary>
public sealed class Position : AuditableEntity<Guid>
{
    private Position() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public Position(Guid id, Guid tenantId, string title, Guid? parentPositionId = null) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        ParentPositionId = parentPositionId;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public Guid? ParentPositionId { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string title, Guid? parentPositionId, bool isActive)
    {
        Title = title.Trim();
        ParentPositionId = parentPositionId;
        IsActive = isActive;
    }
}

/// <summary>
/// A company a position is for. A position may be for several companies; it is given only to
/// people of those companies and to their deputies. In each company, the one position with no
/// superior is the head's.
/// </summary>
public sealed class PositionCompany : AuditableEntity<Guid>
{
    private PositionCompany() : base(Guid.Empty)
    {
    }

    public PositionCompany(Guid id, Guid tenantId, Guid positionId, Guid companyId) : base(id)
    {
        TenantId = tenantId;
        PositionId = positionId;
        CompanyId = companyId;
    }

    public Guid TenantId { get; private set; }
    public Guid PositionId { get; private set; }
    public Guid CompanyId { get; private set; }
}

/// <summary>
/// Where a user sits: their company, optionally their unit (deputy, department, ...) inside it,
/// their position, and optionally a direct manager. Without an explicit manager, the manager is
/// derived from the unit's manager (see HierarchyResolver). One per user per tenant.
/// </summary>
public sealed class UserAssignment : AuditableEntity<Guid>
{
    private UserAssignment() : base(Guid.Empty)
    {
    }

    public UserAssignment(Guid id, Guid tenantId, Guid userId) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid? UnitId { get; private set; }
    public Guid? PositionId { get; private set; }
    public Guid? ManagerUserId { get; private set; }

    public void Set(Guid companyId, Guid? unitId, Guid? positionId, Guid? managerUserId)
    {
        CompanyId = companyId;
        UnitId = unitId;
        PositionId = positionId;
        ManagerUserId = managerUserId;
    }

}

/// <summary>
/// A post (پُست سازمانی): one place for one person, such as «کارشناس (کد ۷) – معاونت فناوری».
/// In a unit (deputy), in the unit's own order, or directly in the company (UnitId null: e.g. the
/// head's post). Its code is unique in its company. The chart shows it even while vacant. A unit
/// may have several posts of the same position.
/// </summary>
public sealed class UnitPosition : AuditableEntity<Guid>
{
    private UnitPosition() : base(Guid.Empty)
    {
        Code = string.Empty;
    }

    public UnitPosition(Guid id, Guid tenantId, Guid companyId, Guid? unitId, Guid positionId, int sortOrder, string code) : base(id)
    {
        TenantId = tenantId;
        CompanyId = companyId;
        UnitId = unitId;
        PositionId = positionId;
        SortOrder = sortOrder;
        Code = code.Trim();
    }

    public Guid TenantId { get; private set; }

    /// <summary>The company of the post: its unit's, or the one it is directly in.</summary>
    public Guid CompanyId { get; private set; }

    /// <summary>Null: directly in the company, in no unit.</summary>
    public Guid? UnitId { get; private set; }

    public Guid PositionId { get; private set; }
    public int SortOrder { get; private set; }
    public string Code { get; private set; }

    /// <summary>
    /// The post above this one in its place (same unit, or both directly in the company): its holder
    /// manages this post's holder. Null: under the unit's manager (or the company's head).
    /// </summary>
    public Guid? ParentPostId { get; private set; }

    /// <summary>The post that manages its unit (its holder is the unit's manager when the unit has none set).</summary>
    public bool IsManagerPost { get; private set; }

    /// <summary>The company head's post. Until the data is migrated, a directly-in-company post whose position has no superior counts too.</summary>
    public bool IsCompanyHead { get; private set; }

    /// <summary>Concurrency token: saving with an older version is refused.</summary>
    public byte[] RowVersion { get; private set; } = [];

    public void SetManagerPost(bool isManagerPost) => IsManagerPost = isManagerPost;

    public void SetCompanyHead(bool isCompanyHead) => IsCompanyHead = isCompanyHead;

    public void MoveTo(int sortOrder) => SortOrder = sortOrder;

    public void PlaceUnder(Guid? parentPostId) => ParentPostId = parentPostId;

    /// <summary>To another node of the same company (a merge): the post, its holder and its tasks stay as they are.</summary>
    public void MoveToUnit(Guid unitId) => UnitId = unitId;

    /// <summary>Its unit moved to another company.</summary>
    public void MoveToCompany(Guid companyId) => CompanyId = companyId;

    public void ChangeCode(string code) => Code = code.Trim();
}

/// <summary>
/// Someone holding a post: one person per post. A person may hold several posts, in any unit;
/// their <see cref="UserAssignment"/> stays their one place in the hierarchy (company, unit, manager).
/// </summary>
public sealed class UserPosition : AuditableEntity<Guid>
{
    private UserPosition() : base(Guid.Empty)
    {
    }

    public UserPosition(Guid id, Guid tenantId, Guid userId, Guid unitPositionId, DateTimeOffset? startDate = null) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        UnitPositionId = unitPositionId;
        StartDate = startDate;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid UnitPositionId { get; private set; }

    /// <summary>
    /// The appointment's period (phase 9): from StartDate (null: since always) until EndDate (null: still
    /// on). An appointment that has ended stays as history; at most one is open (no EndDate) per post.
    /// </summary>
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }

    /// <summary>A new appointment with no start given starts when it is saved.</summary>
    public void StartOn(DateTimeOffset at) => StartDate ??= at;

    public void End(DateTimeOffset at) => EndDate = at;

    /// <summary>The appointment that was to end when a later one starts goes on (that later one was cancelled).</summary>
    public void Reopen() => EndDate = null;

    public bool IsActiveAt(DateTimeOffset at) => (StartDate is not { } from || from <= at) && (EndDate is not { } to || at < to);
}

/// <summary>
/// A post a task is given to (where tasks are given to posts): as its creator's (Role Owner),
/// responsible or with access. HolderUserId: who stood in for the post on the task when last
/// brought up to date - a holder change is put on the task, then recorded here.
/// </summary>
public sealed class TaskPost : AuditableEntity<Guid>
{
    private TaskPost() : base(Guid.Empty)
    {
    }

    public TaskPost(Guid id, Guid tenantId, Guid taskId, Guid postId, int role, Guid? holderUserId) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        PostId = postId;
        Role = role;
        HolderUserId = holderUserId;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }
    public Guid PostId { get; private set; }

    /// <summary>NexusCore's TaskPostRole: 0 owner, 1 responsible, 2 access.</summary>
    public int Role { get; private set; }

    public Guid? HolderUserId { get; private set; }

    public void HeldBy(Guid? userId) => HolderUserId = userId;
}

/// <summary>
/// One stretch of time someone was on a task through one of its posts (where tasks are given to
/// posts): as its creator (Role 0), responsible (1) or the stand-in for its vacant responsible
/// post (3, جانشین) - from StartedAtUtc until EndedAtUtc (null: still). What the post and the
/// person's own position were then is kept as it was, so a report shows each person's position
/// on the dates they were on the task, not their position today.
/// </summary>
public sealed class TaskPostPeriod : AuditableEntity<Guid>
{
    private TaskPostPeriod() : base(Guid.Empty)
    {
        PostTitle = string.Empty;
        PostCode = string.Empty;
        Place = string.Empty;
        PositionTitle = string.Empty;
    }

    public TaskPostPeriod(
        Guid id, Guid tenantId, Guid taskId, Guid postId, int role, Guid userId,
        string postTitle, string postCode, string place, Guid positionId, string positionTitle, Guid? actingPostId,
        DateTimeOffset startedAtUtc) : base(id)
    {
        TenantId = tenantId;
        TaskId = taskId;
        PostId = postId;
        Role = role;
        UserId = userId;
        PostTitle = postTitle;
        PostCode = postCode;
        Place = place;
        PositionId = positionId;
        PositionTitle = positionTitle;
        ActingPostId = actingPostId;
        StartedAtUtc = startedAtUtc;
    }

    public Guid TenantId { get; private set; }
    public Guid TaskId { get; private set; }

    /// <summary>The task's post: for a stand-in, the vacant post they stand in for.</summary>
    public Guid PostId { get; private set; }

    /// <summary>NexusCore's TaskPostRole: 0 owner, 1 responsible, 3 acting (stand-in).</summary>
    public int Role { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>The task's post then: its position's title («کارشناس»), its code and where it was.</summary>
    public string PostTitle { get; private set; }
    public string PostCode { get; private set; }
    public string Place { get; private set; }

    /// <summary>The person's own position then: the post's own, or for a stand-in their own post's.</summary>
    public Guid PositionId { get; private set; }
    public string PositionTitle { get; private set; }

    /// <summary>A stand-in's own post (the one put in charge of the vacant post).</summary>
    public Guid? ActingPostId { get; private set; }

    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? EndedAtUtc { get; private set; }

    public void End(DateTimeOffset atUtc) => EndedAtUtc ??= atUtc;
}

/// <summary>
/// A post standing in for a vacant one (سرپرستی): its holder sees the vacant post's tasks and
/// comments on them as its stand-in (جانشین), nothing more, until someone takes the vacant post.
/// AppliedUserId: who was last given that view of the tasks.
/// </summary>
public sealed class PostActing : AuditableEntity<Guid>
{
    private PostActing() : base(Guid.Empty)
    {
    }

    public PostActing(Guid id, Guid tenantId, Guid vacantPostId, Guid actingPostId) : base(id)
    {
        TenantId = tenantId;
        VacantPostId = vacantPostId;
        ActingPostId = actingPostId;
    }

    public Guid TenantId { get; private set; }
    public Guid VacantPostId { get; private set; }
    public Guid ActingPostId { get; private set; }
    public Guid? AppliedUserId { get; private set; }

    /// <summary>
    /// A stand-in for a set time (from StartDate, null: now, until EndDate): also while the post has
    /// a holder (leave, a mission). Without an end it lasts while the post is vacant.
    /// </summary>
    public DateTimeOffset? StartDate { get; private set; }
    public DateTimeOffset? EndDate { get; private set; }

    public bool IsDated => EndDate is not null;

    /// <summary>Whether it is on at <paramref name="at"/>: a dated one in its time, else while the post is vacant.</summary>
    public bool IsOnAt(DateTimeOffset at, bool postVacant) =>
        IsDated ? (StartDate is not { } start || start <= at) && at < EndDate!.Value : postVacant;

    /// <summary>Whether it is over: a dated one after its end, else once the post has a holder.</summary>
    public bool IsOverAt(DateTimeOffset at, bool postVacant) => IsDated ? at >= EndDate!.Value : !postVacant;

    public void ActBy(Guid actingPostId) => ActingPostId = actingPostId;

    public void SetPeriod(DateTimeOffset? start, DateTimeOffset? end)
    {
        StartDate = start;
        EndDate = end;
    }

    public void Applied(Guid? userId) => AppliedUserId = userId;
}

/// <summary>A title put before people's names, such as «آقای»، «خانم» or «جناب مهندس».</summary>
public sealed class Honorific : AuditableEntity<Guid>
{
    private Honorific() : base(Guid.Empty)
    {
        Title = string.Empty;
    }

    public Honorific(Guid id, Guid tenantId, string title, int sortOrder) : base(id)
    {
        TenantId = tenantId;
        Title = title.Trim();
        SortOrder = sortOrder;
        IsActive = true;
    }

    public Guid TenantId { get; private set; }
    public string Title { get; private set; }
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public void Update(string title, int sortOrder, bool isActive)
    {
        Title = title.Trim();
        SortOrder = sortOrder;
        IsActive = isActive;
    }
}

/// <summary>Which title a user has. Their display name starts with it (see HonorificService).</summary>
public sealed class UserHonorific : AuditableEntity<Guid>
{
    private UserHonorific() : base(Guid.Empty)
    {
    }

    public UserHonorific(Guid id, Guid tenantId, Guid userId, Guid honorificId) : base(id)
    {
        TenantId = tenantId;
        UserId = userId;
        HonorificId = honorificId;
    }

    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public Guid HonorificId { get; private set; }

    public void Change(Guid honorificId) => HonorificId = honorificId;
}
