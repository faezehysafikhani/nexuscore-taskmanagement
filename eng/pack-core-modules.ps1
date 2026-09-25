param(
    [string]$Configuration = "Release",
    [string]$Version = "0.1.0",
    [string]$Output = "artifacts/packages"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

$outputPath = Join-Path $repoRoot $Output

$solutionPath = Join-Path $repoRoot "NexusCore.sln"

New-Item -ItemType Directory -Force -Path $outputPath |
    Out-Null


# ============================================================
# 1. Shared Core
# ============================================================

$coreProjects = @(

    "NexusCore.SharedKernel/NexusCore.SharedKernel.csproj",

    "NexusCore.Domain/NexusCore.Domain.csproj",

    "NexusCore.Application/NexusCore.Application.csproj",

    "NexusCore.Infrastructure/NexusCore.Infrastructure.csproj"

)


# ============================================================
# 2. Chat Module
# ============================================================

$chatProjects = @(

    "Chat.Domain/Chat.Domain.csproj",

    "Chat.Application/Chat.Application.csproj",

    "Chat.Infrastructure/Chat.Infrastructure.csproj",

    "Chat.Api/Chat.Api.csproj"

)


# ============================================================
# 3. Notifications Module
# ============================================================

$notificationProjects = @(

    "Notifications.Domain/Notifications.Domain.csproj",

    "Notifications.Application/Notifications.Application.csproj",

    "Notifications.Infrastructure/Notifications.Infrastructure.csproj",

    "Notifications.Api/Notifications.Api.csproj"

)


# ============================================================
# 4. Events Module
# ============================================================

$eventProjects = @(

    "Events.Domain/Events.Domain.csproj",

    "Events.Application/Events.Application.csproj",

    "Events.Infrastructure/Events.Infrastructure.csproj",

    "Events.Api/Events.Api.csproj"

)


# ============================================================
# 5. Ticketing Module
# ============================================================

$ticketingProjects = @(

    "Ticketing.Domain/Ticketing.Domain.csproj",

    "Ticketing.Application/Ticketing.Application.csproj",

    "Ticketing.Infrastructure/Ticketing.Infrastructure.csproj",

    "Ticketing.Api/Ticketing.Api.csproj"

)


# ============================================================
# 6. Task Management
# ============================================================

$taskManagementProjects = @(

    "Nexus.TaskManagement/Nexus.TaskManagement.csproj",

    "Nexus.TaskManagement.Infrastructure/Nexus.TaskManagement.Infrastructure.csproj"

)


# ============================================================
# 7. Organization
# ============================================================

$organizationProjects = @(

    "Nexus.Organization/Nexus.Organization.csproj",

    "Nexus.Organization.Infrastructure/Nexus.Organization.Infrastructure.csproj"

)


# ============================================================
# 8. Calendar
# ============================================================

$calendarProjects = @(

    "Nexus.Calendar/Nexus.Calendar.csproj",

    "Nexus.Calendar.Infrastructure/Nexus.Calendar.Infrastructure.csproj"

)


# ============================================================
# 9. Actions
# ============================================================

$actionProjects = @(

    "Nexus.Actions/Nexus.Actions.csproj",

    "Nexus.Actions.Infrastructure/Nexus.Actions.Infrastructure.csproj"

)


# ============================================================
# 10. Knowledge
# ============================================================

$knowledgeProjects = @(

    "Nexus.Knowledge/Nexus.Knowledge.csproj",

    "Nexus.Knowledge.Infrastructure/Nexus.Knowledge.Infrastructure.csproj"

)


# ============================================================
# 11. Strategy
# ============================================================

$strategyProjects = @(

    "Nexus.Strategy/Nexus.Strategy.csproj",

    "Nexus.Strategy.Infrastructure/Nexus.Strategy.Infrastructure.csproj"

)


# ============================================================
# 12. Workflow
# ============================================================

$workflowProjects = @(

    "Nexus.Workflow/Nexus.Workflow.csproj",

    "Nexus.Workflow.Infrastructure/Nexus.Workflow.Infrastructure.csproj"

)


# ============================================================
# 13. Reporting
# ============================================================

$reportingProjects = @(

    "Nexus.Reporting/Nexus.Reporting.csproj"

)


# ============================================================
# 14. Portfolio
# ============================================================

$portfolioProjects = @(

    "Nexus.Portfolio/Nexus.Portfolio.csproj"

)


# ============================================================
# 15. Project Management - Core
# ============================================================

$projectCoreProjects = @(

    "Nexus.ProjectManagement.Core/Nexus.ProjectManagement.Core.csproj",

    "Nexus.ProjectManagement.Core.Infrastructure/Nexus.ProjectManagement.Core.Infrastructure.csproj"

)


# ============================================================
# 16. Project Management - Agile
# ============================================================

$agileProjects = @(

    "Nexus.ProjectManagement.Agile/Nexus.ProjectManagement.Agile.csproj",

    "Nexus.ProjectManagement.Agile.Infrastructure/Nexus.ProjectManagement.Agile.Infrastructure.csproj"

)


# ============================================================
# 17. Project Management - Waterfall
# ============================================================

$waterfallProjects = @(

    "Nexus.ProjectManagement.Waterfall/Nexus.ProjectManagement.Waterfall.csproj",

    "Nexus.ProjectManagement.Waterfall.Infrastructure/Nexus.ProjectManagement.Waterfall.Infrastructure.csproj"

)


# ============================================================
# 18. Project Management - Deliverables
# ============================================================

$deliverablesProjects = @(

    "Nexus.ProjectManagement.Deliverables/Nexus.ProjectManagement.Deliverables.csproj",

    "Nexus.ProjectManagement.Deliverables.Infrastructure/Nexus.ProjectManagement.Deliverables.Infrastructure.csproj"

)


# ============================================================
# 19. Project Management - Documents
# ============================================================

$documentsProjects = @(

    "Nexus.ProjectManagement.Documents/Nexus.ProjectManagement.Documents.csproj",

    "Nexus.ProjectManagement.Documents.Infrastructure/Nexus.ProjectManagement.Documents.Infrastructure.csproj"

)


# ============================================================
# 20. Project Management - KPI
# ============================================================

$kpiProjects = @(

    "Nexus.ProjectManagement.Kpi/Nexus.ProjectManagement.Kpi.csproj",

    "Nexus.ProjectManagement.Kpi.Infrastructure/Nexus.ProjectManagement.Kpi.Infrastructure.csproj"

)


# ============================================================
# 21. Project Management - Progress
# ============================================================

$progressProjects = @(

    "Nexus.ProjectManagement.Progress/Nexus.ProjectManagement.Progress.csproj",

    "Nexus.ProjectManagement.Progress.Infrastructure/Nexus.ProjectManagement.Progress.Infrastructure.csproj"

)


# ============================================================
# 22. Project Management - Risk
# ============================================================

$riskProjects = @(

    "Nexus.ProjectManagement.Risk/Nexus.ProjectManagement.Risk.csproj",

    "Nexus.ProjectManagement.Risk.Infrastructure/Nexus.ProjectManagement.Risk.Infrastructure.csproj"

)


# ============================================================
# 23. Project Management - Stakeholder
# ============================================================

$stakeholderProjects = @(

    "Nexus.ProjectManagement.Stakeholder/Nexus.ProjectManagement.Stakeholder.csproj",

    "Nexus.ProjectManagement.Stakeholder.Infrastructure/Nexus.ProjectManagement.Stakeholder.Infrastructure.csproj"

)


# ============================================================
# 24. Project Management - Team
# ============================================================

$teamProjects = @(

    "Nexus.ProjectManagement.Team/Nexus.ProjectManagement.Team.csproj",

    "Nexus.ProjectManagement.Team.Infrastructure/Nexus.ProjectManagement.Team.Infrastructure.csproj"

)


# ============================================================
# 25. Optional Integrations
# ============================================================

$integrationProjects = @(

    "Nexus.Integrations.TaskNotifications/Nexus.Integrations.TaskNotifications.csproj",

    "Nexus.Integrations.ProjectWorkflow/Nexus.Integrations.ProjectWorkflow.csproj",

    "Nexus.Integrations.ProjectStrategyAlignment/Nexus.Integrations.ProjectStrategyAlignment.csproj",

    "Nexus.Integrations.ProjectStrategyAlignment.Infrastructure/Nexus.Integrations.ProjectStrategyAlignment.Infrastructure.csproj"

)


# ============================================================
# Combine package projects
# ============================================================

$projects = @(

    $coreProjects

    $chatProjects

    $notificationProjects

    $eventProjects

    $ticketingProjects

    $taskManagementProjects

    $organizationProjects

    $calendarProjects

    $actionProjects

    $knowledgeProjects

    $strategyProjects

    $workflowProjects

    $reportingProjects

    $portfolioProjects

    $projectCoreProjects

    $agileProjects

    $waterfallProjects

    $deliverablesProjects

    $documentsProjects

    $kpiProjects

    $progressProjects

    $riskProjects

    $stakeholderProjects

    $teamProjects

    $integrationProjects

) | ForEach-Object { $_ } | Select-Object -Unique


# ============================================================
# Excluded projects
# ============================================================

$excludedProjects = @(

    "NexusCore.Tests/NexusCore.Tests.csproj",

    "Nexus.TaskManagement.Tests/Nexus.TaskManagement.Tests.csproj",

    "Nexus.CompositionTests/Nexus.CompositionTests.csproj",

    "NexusCore.Api/NexusCore.Api.csproj",

    "Rozet.Api/Rozet.Api.csproj",

    "PostbankPM/PostbankPM.csproj"

)


# ============================================================
# Validate project paths
# ============================================================

Write-Host ""
Write-Host "Validating package projects..."
Write-Host ""

foreach ($project in $projects) {

    $projectPath = Join-Path $repoRoot $project

    if (-not (Test-Path $projectPath)) {

        throw "Project not found: $project"

    }

}


# ============================================================
# Detect projects not classified
# ============================================================

$solutionProjects = @(

    dotnet sln $solutionPath list |

    Where-Object {

        $_ -match '\.csproj$'

    } |

    ForEach-Object {

        $_.Trim().Replace('\', '/')

    }

)

if ($LASTEXITCODE -ne 0) {

    throw "Failed to read NexusCore.sln"

}


$unclassifiedProjects = @(

    $solutionProjects |

    Where-Object {

        $_ -notin $projects -and
        $_ -notin $excludedProjects

    }

)


if ($unclassifiedProjects.Count -gt 0) {

    Write-Host ""
    Write-Warning "Some solution projects are not classified:"
    Write-Host ""

    $unclassifiedProjects | ForEach-Object {

        Write-Host $_

    }

    throw "Review unclassified projects before packaging."

}


# ============================================================
# Package generation
# ============================================================

Write-Host ""
Write-Host "=========================================="
Write-Host "NexusCore NuGet Package Generation"
Write-Host "=========================================="
Write-Host ""

Write-Host "Version: $Version"

Write-Host "Configuration: $Configuration"

Write-Host "Projects selected: $($projects.Count)"

Write-Host "Output: $outputPath"

Write-Host ""


$completedPackages = @()


foreach ($project in $projects) {

    $projectPath = Join-Path $repoRoot $project

    $packageId = [System.IO.Path]::GetFileNameWithoutExtension(
        $projectPath
    )

    Write-Host ""
    Write-Host "------------------------------------------"
    Write-Host "Packing: $packageId"
    Write-Host "------------------------------------------"
    Write-Host ""


    dotnet pack $projectPath `
        --configuration $Configuration `
        -p:PackageVersion=$Version `
        -p:IsPackable=true `
        -p:ContinuousIntegrationBuild=true `
        -p:IncludeSymbols=false `
        --output $outputPath


    if ($LASTEXITCODE -ne 0) {

        throw "Failed to pack $packageId. Exit code: $LASTEXITCODE"

    }


    $expectedPackage = Join-Path $outputPath "$packageId.$Version.nupkg"


    if (-not (Test-Path $expectedPackage)) {

        throw "Package output not found: $expectedPackage"

    }


    $completedPackages += $expectedPackage

}


# ============================================================
# Final verification
# ============================================================

Write-Host ""
Write-Host "=========================================="
Write-Host "PACKAGING COMPLETED SUCCESSFULLY"
Write-Host "=========================================="
Write-Host ""

Write-Host "Selected projects: $($projects.Count)"

Write-Host "Completed packages: $($completedPackages.Count)"

Write-Host ""

Write-Host "Generated package files:"
Write-Host ""


$completedPackages | ForEach-Object {

    Write-Host ([System.IO.Path]::GetFileName($_))

}


Write-Host ""
Write-Host "Output folder:"
Write-Host $outputPath
Write-Host ""