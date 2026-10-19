using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Contracts.Endpoints;

public static class CommitmentEndpoints
{
    /// <summary>Milestones and obligations of a contract. Reading needs Contracts.View; changing them is editing the contract (Contracts.Edit).</summary>
    public static IEndpointRouteBuilder MapCommitmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/contracts").WithTags("ContractCommitments").RequireAuthorization();

        group.MapGet("/{contractId:guid}/commitments", async (Guid contractId, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.GetSummaryAsync(contractId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapGet("/{contractId:guid}/milestones", async (Guid contractId, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.ListMilestonesAsync(contractId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/{contractId:guid}/milestones", async (Guid contractId, SaveContractMilestoneRequest request, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.CreateMilestoneAsync(contractId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapPut("/{contractId:guid}/milestones/{id:guid}", async (Guid contractId, Guid id, SaveContractMilestoneRequest request, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.UpdateMilestoneAsync(contractId, id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapDelete("/{contractId:guid}/milestones/{id:guid}", async (Guid contractId, Guid id, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.DeleteMilestoneAsync(contractId, id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapGet("/{contractId:guid}/obligations", async (Guid contractId, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.ListObligationsAsync(contractId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/{contractId:guid}/obligations", async (Guid contractId, SaveContractObligationRequest request, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.CreateObligationAsync(contractId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapPut("/{contractId:guid}/obligations/{id:guid}", async (Guid contractId, Guid id, SaveContractObligationRequest request, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.UpdateObligationAsync(contractId, id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapDelete("/{contractId:guid}/obligations/{id:guid}", async (Guid contractId, Guid id, IContractCommitmentService service, CancellationToken cancellationToken) =>
                (await service.DeleteObligationAsync(contractId, id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        return app;
    }
}
