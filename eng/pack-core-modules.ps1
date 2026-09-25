param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0",
    [string]$Output = "artifacts/packages"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $repoRoot $Output
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$projects = @(
    "NexusCore.SharedKernel/NexusCore.SharedKernel.csproj",
    "NexusCore.Domain/NexusCore.Domain.csproj",
    "NexusCore.Application/NexusCore.Application.csproj",
    "NexusCore.Infrastructure/NexusCore.Infrastructure.csproj",
    "Chat.Domain/Chat.Domain.csproj",
    "Chat.Application/Chat.Application.csproj",
    "Chat.Infrastructure/Chat.Infrastructure.csproj",
    "Chat.Api/Chat.Api.csproj",
    "Notifications.Domain/Notifications.Domain.csproj",
    "Notifications.Application/Notifications.Application.csproj",
    "Notifications.Infrastructure/Notifications.Infrastructure.csproj",
    "Notifications.Api/Notifications.Api.csproj",
    "Nexus.TaskManagement/Nexus.TaskManagement.csproj",
    "Nexus.TaskManagement.Infrastructure/Nexus.TaskManagement.Infrastructure.csproj",
    "Nexus.Integrations.TaskNotifications/Nexus.Integrations.TaskNotifications.csproj"
)

foreach ($project in $projects) {
    $projectPath = Join-Path $repoRoot $project
    $packageId = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)

    Write-Host "Packing $packageId from $project"
    dotnet pack $projectPath `
        --configuration $Configuration `
        -p:PackageId=$packageId `
        -p:PackageVersion=$Version `
        -p:IsPackable=true `
        -p:ContinuousIntegrationBuild=true `
        -p:IncludeSymbols=false `
        --output $outputPath
}

Write-Host "Packages written to $outputPath"
Write-Host "Copy this folder to the approved internal NuGet feed or to an offline package share."
