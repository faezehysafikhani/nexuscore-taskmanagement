using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Application.Dtos;
using Xunit.Abstractions;

namespace Nexus.CompositionTests;

/// <summary>
/// Phase 6.2 (import from a sheet: preview, all-or-nothing commit, only new codes, automatic links
/// and posts) and 6.5 (a node's optional code and details).
/// </summary>
public sealed partial class OrgNodeApiTests
{
    private readonly ITestOutputHelper? _output;

    public OrgNodeApiTests(ITestOutputHelper output) => _output = output;

    private static IReadOnlyList<ExcelStructureRow> Sample(string file)
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", file));
        var (rows, problem) = OrgSheetReader.Read(stream, file);
        Assert.Null(problem);
        return rows;
    }

    private static IReadOnlyList<ExcelStructureRow> Csv(string text)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        return OrgSheetReader.Read(stream, "x.csv").Rows;
    }

    private static async Task<(World W, Guid Company, IOrgImportService Import)> BankCompanyAsync(bool allowNesting = true)
    {
        var w = new World(allowNesting);
        // The bank's types, rules and positions come with the template (an empty bank: no nodes kept).
        var company = await w.Ok(w.Service.CreateCompanyAsync(Tenant, new CreateCompanyRequest("بانک", "B", null, OrgNodeService.SystemTemplateId(allowNesting ? "bank" : "deputy")), default));
        return (w, company.Id, new OrgImportService((OrgNodeService)w.Nodes, (OrgStructureService)w.Service));
    }

    [Fact]
    public async Task TheFaultySample_IsReportedRowByRow_AndNothingIsMade()
    {
        var (w, company, import) = await BankCompanyAsync();
        var before = (await w.Ok(w.Nodes.ListNodesAsync(Tenant, null, default))).Count;
        var preview = await w.Ok(import.PreviewAsync(Tenant, new ImportTargetRequest(company), Sample("bank-structure-faulty.csv"), default));
        Assert.Equal([5, 6, 7, 8, 9, 10], preview.Errors.Select(e => e.Row!.Value).Distinct().Order());
        Assert.Empty(preview.Nodes);
        Assert.Equal("validation.error", (await import.CommitAsync(Tenant, new ImportTargetRequest(company), Sample("bank-structure-faulty.csv"), default)).Error.Code);
        Assert.Equal(before, (await w.Ok(w.Nodes.ListNodesAsync(Tenant, null, default))).Count);
    }

    [Fact]
    public async Task TheBankSample_IsImported_WithItsManagerPosts_AndOnlyOnce()
    {
        var (w, company, import) = await BankCompanyAsync();
        var rows = Sample("bank-structure.csv");
        var preview = await w.Ok(import.PreviewAsync(Tenant, new ImportTargetRequest(company), rows, default));
        Assert.Empty(preview.Errors);
        Assert.Equal(40, preview.Nodes.Count);

        var result = await w.Ok(import.CommitAsync(Tenant, new ImportTargetRequest(company), rows, default));
        Assert.Equal(40, result.Nodes.Count);
        var byCode = result.Nodes.ToDictionary(n => n.Code!);
        Assert.Equal(byCode["B03"].Id, byCode["B04"].ParentNodeId);
        Assert.Null(byCode["B01"].ParentNodeId);
        Assert.Equal(company, byCode["B04"].CompanyId);
        // The manager posts the sheet names: one per such node, of that position.
        var posts = (await w.Ok(w.Service.ListUnitPositionsAsync(Tenant, null, default))).Where(p => byCode.Values.Any(n => n.Id == p.UnitId)).ToList();
        var withManager = rows.Count(r => !string.IsNullOrWhiteSpace(r.ManagerPostName));
        Assert.Equal(withManager, posts.Count(p => p.IsManagerPost));
        Assert.Equal("مدیر کل", posts.Single(p => p.UnitId == byCode["B04"].Id && p.IsManagerPost).Title);
        Assert.True(result.Posts >= withManager);

        // Again: every code is already there, so nothing is made.
        var again = await w.Ok(import.PreviewAsync(Tenant, new ImportTargetRequest(company), rows, default));
        Assert.Equal(40, again.Errors.Count);
        Assert.Equal("validation.error", (await import.CommitAsync(Tenant, new ImportTargetRequest(company), rows, default)).Error.Code);
    }

    [Fact]
    public async Task FourHundredRows_AreImportedInAcceptableTime()
    {
        var (w, company, import) = await BankCompanyAsync();
        await w.Ok(w.Service.CreatePositionAsync(Tenant, new CreatePositionRequest("مدیر شعب استان",
            (await w.Ok(w.Service.ListPositionsAsync(Tenant, default))).Single(p => p.Title == "مدیرعامل").Id, [company]), default));
        var rows = Sample("large-bank-structure.csv");
        var clock = Stopwatch.StartNew();
        var preview = await w.Ok(import.PreviewAsync(Tenant, new ImportTargetRequest(company), rows, default));
        var previewTime = clock.Elapsed;
        Assert.Empty(preview.Errors);
        var result = await w.Ok(import.CommitAsync(Tenant, new ImportTargetRequest(company), rows, default));
        clock.Stop();
        Assert.Equal(412, result.Nodes.Count);
        // The five committees: the company now has two boards (the template's and the sheet's), so they are left to choose.
        Assert.Equal(5, result.AutoLinkChoices.Count);
        _output?.WriteLine($"412 rows: preview {previewTime.TotalMilliseconds:F0} ms, preview+commit {clock.Elapsed.TotalMilliseconds:F0} ms, {result.Posts} posts");
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(30), $"took {clock.Elapsed}");
    }

    [Fact]
    public async Task ASheetUnderANode_AndTheRules_AndTheSwitch()
    {
        var (w, company, import) = await BankCompanyAsync();
        var deputy = (await w.Ok(w.Nodes.ListNodesAsync(Tenant, company, default))).First(n => n.NodeTypeName == "معاونت");
        var sheet = Csv("کد,نام,کد والد,نوع,سمت مدیر\nX1,اداره کل تازه,,general-department,مدیر کل\nX2,اداره‌ی تازه,X1,office,\n");
        var made = await w.Ok(import.CommitAsync(Tenant, new ImportTargetRequest(ParentNodeId: deputy.Id), sheet, default));
        Assert.Equal(deputy.Id, made.Nodes.Single(n => n.Code == "X1").ParentNodeId);
        Assert.Equal(made.Nodes.Single(n => n.Code == "X1").Id, made.Nodes.Single(n => n.Code == "X2").ParentNodeId);

        // A type not allowed there, an unknown manager's position, both at once.
        var bad = Csv("کد,نام,کد والد,نوع,سمت مدیر\nY1,شعبه زیر معاونت,,branch,\nY2,اداره کل,,general-department,سمت ناشناخته\n");
        var preview = await w.Ok(import.PreviewAsync(Tenant, new ImportTargetRequest(ParentNodeId: deputy.Id), bad, default));
        Assert.Equal([2, 3], preview.Errors.Select(e => e.Row!.Value).Order());
        Assert.Equal("validation.error", (await import.PreviewAsync(Tenant, new ImportTargetRequest(), bad, default)).Error.Code);

        // With nesting off: a flat sheet is fine, a nested one is not.
        var (flatWorld, flatCompany, flatImport) = await BankCompanyAsync(allowNesting: false);
        Assert.Equal(2, (await flatWorld.Ok(flatImport.CommitAsync(Tenant, new ImportTargetRequest(flatCompany), Csv("کد,نام,کد والد,نوع\nF1,معاونت یک,,deputy\nF2,معاونت دو,,deputy\n"), default))).Nodes.Count);
        var nested = await flatWorld.Ok(flatImport.PreviewAsync(Tenant, new ImportTargetRequest(flatCompany), Csv("کد,نام,کد والد\nG1,یک\nG2,دو,G1\n"), default));
        Assert.Contains(nested.Errors, e => e.Message.Contains("AllowNesting"));
    }

    [Fact]
    public void AnXlsxSheet_IsRead_WithItsRowNumbers()
    {
        using var file = new MemoryStream();
        using (var zip = new ZipArchive(file, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Put(string path, string xml)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(xml);
            }

            const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            Put("xl/workbook.xml", $"<workbook xmlns=\"{ns}\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"s\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
            Put("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
            Put("xl/sharedStrings.xml", $"<sst xmlns=\"{ns}\"><si><t>کد</t></si><si><t>معاونت فناوری</t></si></sst>");
            Put("xl/worksheets/sheet1.xml", $"<worksheet xmlns=\"{ns}\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c></row>" +
                "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>D1</t></is></c><c r=\"B3\" t=\"s\"><v>1</v></c><c r=\"D3\" t=\"inlineStr\"><is><t>deputy</t></is></c></row>" +
                "</sheetData></worksheet>");
        }

        file.Position = 0;
        var (rows, problem) = OrgSheetReader.Read(file, "s.xlsx");
        Assert.Null(problem);
        var row = Assert.Single(rows);
        Assert.Equal((3, "D1", "معاونت فناوری", (string?)null, "deputy"), (row.Row, row.Code, row.Name, row.ParentCode, row.TypeCode));
        Assert.NotNull(OrgSheetReader.Read(new MemoryStream([1, 2, 3]), "bad.xlsx").Problem);
    }

    // ------------------------------------------------------------------ 6.5: code and details

    [Fact]
    public async Task ANodesCodeAndDetails_AreOptional_AndTheCodeIsUniqueInItsCompany()
    {
        var w = await World.BankAsync();
        var it = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت فناوری", CompanyId: w.Company, NodeTypeId: w.Type("deputy")), default));
        Assert.Null(it.Code);
        Assert.Null(it.LatinName);

        var set = await w.Ok(w.Nodes.SetDetailsAsync(Tenant, it.Id, new SetNodeDetailsRequest("IT", " IT Deputy ", "معاونت محترم فناوری اطلاعات", "021-1234", "تهران"), default));
        Assert.Equal(("IT", "IT Deputy", "معاونت محترم فناوری اطلاعات", "021-1234", "تهران"), (set.Code, set.LatinName, set.CorrespondenceTitle, set.Phone, set.Address));
        Assert.Equal("معاونت فناوری", set.Name);
        Assert.Equal("IT", (await w.Ok(w.Nodes.GetNodeAsync(Tenant, it.Id, default))).Code);

        // The same code in the same company: refused; in another company: fine. Moving there with it: refused.
        Assert.Equal("conflict", (await w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("دیگری", CompanyId: w.Company, NodeTypeId: w.Type("deputy"), Code: "it"), default)).Error.Code);
        var other = await w.CompanyAsync("C2");
        var there = await w.Ok(w.Nodes.CreateNodeAsync(Tenant, new CreateNodeRequest("معاونت شرکت دوم", CompanyId: other, NodeTypeId: w.Type("deputy"), Code: "IT"), default));
        Assert.Equal("IT", there.Code);
        Assert.Equal("conflict", (await w.Nodes.MoveNodeAsync(Tenant, it.Id, new MoveNodeRequest(null, other), default)).Error.Code);

        // Emptied: none again; other nodes untouched.
        var cleared = await w.Ok(w.Nodes.SetDetailsAsync(Tenant, it.Id, new SetNodeDetailsRequest(), default));
        Assert.Null(cleared.Code);
        Assert.Null(cleared.Address);
        Assert.Equal("IT", (await w.Ok(w.Nodes.GetNodeAsync(Tenant, there.Id, default))).Code);
    }
}
