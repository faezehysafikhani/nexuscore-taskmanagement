using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Contracts.Endpoints;

public static class SupplierEndpoints
{
    /// <summary>Suppliers belong to the caller's tenant: it is read from the signed-in user, never from the request.</summary>
    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/suppliers").WithTags("Suppliers").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext currentUser, ISupplierService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.ListAsync(tenantId, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, ICurrentUserContext currentUser, ISupplierService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.GetAsync(tenantId, id, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/", async (CreateSupplierRequest request, ICurrentUserContext currentUser, ISupplierService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.CreateAsync(tenantId, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.ManageSuppliers);

        group.MapPut("/{id:guid}", async (Guid id, UpdateSupplierRequest request, ICurrentUserContext currentUser, ISupplierService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.UpdateAsync(tenantId, id, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.ManageSuppliers);

        group.MapDelete("/{id:guid}", async (Guid id, ICurrentUserContext currentUser, ISupplierService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.DeleteAsync(tenantId, id, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.ManageSuppliers);

        // Periodic evaluations of a supplier (reading needs Contracts.View, recording one Contracts.EvaluateSuppliers).
        group.MapGet("/{supplierId:guid}/evaluations", async (Guid supplierId, ICurrentUserContext currentUser, ISupplierEvaluationService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.ListAsync(tenantId, supplierId, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.View);

        group.MapGet("/{supplierId:guid}/evaluation-metrics", async (
                Guid supplierId, DateOnly from, DateOnly to, Guid? contractId, ICurrentUserContext currentUser, ISupplierEvaluationService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.GetMetricsAsync(tenantId, supplierId, from, to, contractId, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/{supplierId:guid}/evaluations", async (
                Guid supplierId, SaveSupplierEvaluationRequest request, ICurrentUserContext currentUser, ISupplierEvaluationService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.CreateAsync(tenantId, supplierId, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.EvaluateSuppliers);

        group.MapPut("/{supplierId:guid}/evaluations/{id:guid}", async (
                Guid supplierId, Guid id, SaveSupplierEvaluationRequest request, ICurrentUserContext currentUser, ISupplierEvaluationService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.UpdateAsync(tenantId, supplierId, id, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.EvaluateSuppliers);

        group.MapDelete("/{supplierId:guid}/evaluations/{id:guid}", async (
                Guid supplierId, Guid id, ICurrentUserContext currentUser, ISupplierEvaluationService service, CancellationToken cancellationToken) =>
                currentUser.TenantId is { } tenantId
                    ? (await service.DeleteAsync(tenantId, supplierId, id, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(ContractPermissions.EvaluateSuppliers);

        return app;
    }
}
