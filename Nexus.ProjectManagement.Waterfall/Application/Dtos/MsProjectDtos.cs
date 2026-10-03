namespace Nexus.ProjectManagement.Waterfall.Application.Dtos;

/// <summary>What an MS Project import did. Warnings name everything that was left out and why.</summary>
public sealed record MsProjectImportResultDto(
    int ActivitiesImported, int DependenciesImported, int ActivitiesReplaced, IReadOnlyList<string> Warnings);

public sealed record MsProjectExportDto(string FileName, byte[] Content);
