using Microsoft.EntityFrameworkCore;
using Nexus.Organization.Domain;
using Nexus.Organization.Infrastructure;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Nexus.OrgStructure.Infrastructure;
using NexusCore.Application.Files;

namespace Nexus.CompositionTests;

/// <summary>A company's profile: details, head title, logo, automatic code, units on create, default level.</summary>
public sealed class CompanyProfileTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    [Fact]
    public async Task Create_WithoutCode_GetsTheNextFreeCode_AndStartsAdvanced()
    {
        var world = new World();
        var first = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("سازمان یک"), default)).Value!;
        Assert.Equal("C-001", first.Code);
        Assert.Equal("advanced", first.UiLevel);

        // A hand-made code that takes the next number is skipped.
        Assert.True((await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("دستی", "C-002"), default)).IsSuccess);
        var third = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("سازمان سه", "  "), default)).Value!;
        Assert.Equal("C-003", third.Code);
    }

    [Fact]
    public async Task Details_AreSavedOnCreate_AndReplacedWhole()
    {
        var world = new World();
        var details = new SetCompanyDetailsRequest("88731601", "info@example.com", "example.ir", "10101234567", "411111111111", " تهران ", "مدیرعامل");
        var company = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("مدیریت پروژه پارس", Details: details), default)).Value!;
        Assert.Equal(("88731601", "info@example.com", "example.ir", "10101234567", "411111111111", "تهران", "مدیرعامل"),
            (company.Phone, company.Email, company.Website, company.NationalId, company.EconomicCode, company.Address, company.HeadTitle));

        // Replaces all of them: what is left out is cleared.
        var changed = (await world.Profile.SetCompanyDetailsAsync(Tenant, company.Id, new SetCompanyDetailsRequest(Phone: "021"), default)).Value!;
        Assert.Equal("021", changed.Phone);
        Assert.Null(changed.Email);
        Assert.Null(changed.HeadTitle);

        // The chart carries the head's title.
        await world.Profile.SetCompanyDetailsAsync(Tenant, company.Id, new SetCompanyDetailsRequest(HeadTitle: "مدیر سازمان"), default);
        var chart = (await world.Service.GetChartAsync(Tenant, null, default)).Value!;
        Assert.Equal("مدیر سازمان", chart.Single().HeadTitle);
    }

    [Theory]
    [InlineData("not-an-email", null)]
    [InlineData(null, "12a45")]
    public async Task Details_AreChecked(string? email, string? nationalId)
    {
        var world = new World();
        var company = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("الف"), default)).Value!;
        var result = await world.Profile.SetCompanyDetailsAsync(Tenant, company.Id, new SetCompanyDetailsRequest(Email: email, NationalId: nationalId), default);
        Assert.Equal("validation.error", result.Error.Code);

        var onCreate = await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("ب", Details: new SetCompanyDetailsRequest(Email: email, NationalId: nationalId)), default);
        Assert.False(onCreate.IsSuccess);
    }

    [Fact]
    public async Task Create_LinksTheChosenUnits_OnlyFreeTopOnes()
    {
        var world = new World();
        var a = world.Unit("معاونت الف");
        var b = world.Unit("معاونت ب");
        var under = world.Unit("اداره زیر الف", a.Id);

        var company = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("سازمان", UnitIds: [a.Id, b.Id]), default)).Value!;
        Assert.Equal(new[] { a.Id, b.Id }.Order(), company.LinkedUnitIds.Order());

        // A unit under another, or one already in a company: nothing is made.
        var nested = await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("دوم", UnitIds: [under.Id]), default);
        Assert.Equal("validation.error", nested.Error.Code);
        var taken = await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("سوم", UnitIds: [a.Id]), default);
        Assert.Equal("conflict", taken.Error.Code);
        Assert.Single((await world.Service.ListCompaniesAsync(Tenant, default)).Value!);
    }

    [Fact]
    public async Task Logo_IsSaved_Replaced_AndRemoved()
    {
        var world = new World();
        var company = (await world.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("الف"), default)).Value!;
        Assert.Null(company.LogoUrl);

        var withLogo = (await world.Profile.SetCompanyLogoAsync(Tenant, company.Id, Upload(Png), default)).Value!;
        Assert.StartsWith($"/api/org-structure/companies/{company.Id}/logo?v=", withLogo.LogoUrl);
        var file = await world.Profile.OpenCompanyLogoAsync(Tenant, company.Id, default);
        Assert.Equal("image/png", file!.ContentType);

        // A new logo is a new address, and the old file goes.
        var replaced = (await world.Profile.SetCompanyLogoAsync(Tenant, company.Id, Upload([.. Png, 9]), default)).Value!;
        Assert.NotEqual(withLogo.LogoUrl, replaced.LogoUrl);
        Assert.Single(world.Files.Stored);

        // Not an image (an SVG is text): refused.
        var svg = await world.Profile.SetCompanyLogoAsync(Tenant, company.Id, Upload("<svg/>"u8.ToArray()), default);
        Assert.Equal("validation.error", svg.Error.Code);

        Assert.Null((await world.Profile.RemoveCompanyLogoAsync(Tenant, company.Id, default)).Value!.LogoUrl);
        Assert.Empty(world.Files.Stored);
        Assert.Null(await world.Profile.OpenCompanyLogoAsync(Tenant, company.Id, default));
    }

    private static CompanyLogoUpload Upload(byte[] bytes) => new("logo.png", new MemoryStream(bytes), bytes.Length);

    private sealed class World
    {
        private readonly OrganizationDbContext _organization;

        public World()
        {
            var name = Guid.NewGuid().ToString();
            _organization = new OrganizationDbContext(new DbContextOptionsBuilder<OrganizationDbContext>().UseInMemoryDatabase("org-" + name).Options);
            var structure = new OrgStructureDbContext(new DbContextOptionsBuilder<OrgStructureDbContext>().UseInMemoryDatabase("orgs-" + name).Options);
            var service = new OrgStructureService(new OrgStructureRepository(structure), structure, new OrganizationUnitRepository(_organization),
                new OrgGoldenTests.Directory(), new OrgChartTeamsAndTitlesTests.NoWorkTeams(), files: Files);
            Service = service;
            Profile = service;
        }

        public MemoryFiles Files { get; } = new();

        public IOrgStructureService Service { get; }

        public IOrgCompanyProfileService Profile { get; }

        public OrganizationUnit Unit(string name, Guid? parentId = null)
        {
            var unit = new OrganizationUnit(Guid.NewGuid(), Tenant, name, null);
            unit.SetParent(parentId);
            _organization.OrganizationUnits.Add(unit);
            _organization.SaveChanges();
            return unit;
        }
    }

    private sealed class MemoryFiles : IFileStorage
    {
        public Dictionary<string, byte[]> Stored { get; } = [];

        public async Task<StoredFile> SaveAsync(string fileName, string contentType, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            var key = Guid.NewGuid().ToString("N") + Path.GetExtension(fileName);
            Stored[key] = buffer.ToArray();
            return new StoredFile(key, fileName, contentType, buffer.Length);
        }

        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(Stored.TryGetValue(storageKey, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
        {
            Stored.Remove(storageKey);
            return Task.CompletedTask;
        }
    }
}
