using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Permissions;
using NexusCore.Application.Common;

namespace Nexus.ProjectManagement.Contracts.Endpoints;

public static class ContractEndpoints
{
    public static IEndpointRouteBuilder MapContractEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/contracts").WithTags("Contracts").RequireAuthorization();

        group.MapGet("/", async (Guid projectId, IContractService service, CancellationToken cancellationToken) =>
                (await service.ListByProjectAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapGet("/summary", async (Guid projectId, IContractService service, CancellationToken cancellationToken) =>
                (await service.GetProjectSummaryAsync(projectId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapGet("/{id:guid}", async (Guid id, IContractService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/", async (CreateContractRequest request, IContractService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Create);

        group.MapPut("/{id:guid}", async (Guid id, UpdateContractRequest request, IContractService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapPut("/{id:guid}/status", async (Guid id, ChangeContractStatusRequest request, IContractService service, CancellationToken cancellationToken) =>
                (await service.ChangeStatusAsync(id, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Edit);

        group.MapDelete("/{id:guid}", async (Guid id, IContractService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Delete);

        group.MapPost("/{id:guid}/submit-for-approval", async (Guid id, IContractService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(id, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Submit);

        // Addenda
        group.MapGet("/{contractId:guid}/addenda", async (Guid contractId, IContractAddendumService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(contractId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/{contractId:guid}/addenda", async (Guid contractId, CreateContractAddendumRequest request, IContractAddendumService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(contractId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageAddenda);

        group.MapPut("/{contractId:guid}/addenda/{addendumId:guid}", async (Guid contractId, Guid addendumId, UpdateContractAddendumRequest request, IContractAddendumService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(contractId, addendumId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageAddenda);

        group.MapDelete("/{contractId:guid}/addenda/{addendumId:guid}", async (Guid contractId, Guid addendumId, IContractAddendumService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(contractId, addendumId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageAddenda);

        group.MapPost("/{contractId:guid}/addenda/{addendumId:guid}/submit-for-approval", async (Guid contractId, Guid addendumId, IContractAddendumService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(contractId, addendumId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Submit);

        // Invoices
        group.MapGet("/{contractId:guid}/invoices", async (Guid contractId, IContractInvoiceService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(contractId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.View);

        group.MapPost("/{contractId:guid}/invoices", async (Guid contractId, CreateContractInvoiceRequest request, IContractInvoiceService service, CancellationToken cancellationToken) =>
                (await service.CreateAsync(contractId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageInvoices);

        group.MapPut("/{contractId:guid}/invoices/{invoiceId:guid}", async (Guid contractId, Guid invoiceId, UpdateContractInvoiceRequest request, IContractInvoiceService service, CancellationToken cancellationToken) =>
                (await service.UpdateAsync(contractId, invoiceId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageInvoices);

        group.MapDelete("/{contractId:guid}/invoices/{invoiceId:guid}", async (Guid contractId, Guid invoiceId, IContractInvoiceService service, CancellationToken cancellationToken) =>
                (await service.DeleteAsync(contractId, invoiceId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.ManageInvoices);

        group.MapPost("/{contractId:guid}/invoices/{invoiceId:guid}/submit-for-approval", async (Guid contractId, Guid invoiceId, IContractInvoiceService service, CancellationToken cancellationToken) =>
                (await service.SubmitForApprovalAsync(contractId, invoiceId, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.Submit);

        group.MapPut("/{contractId:guid}/invoices/{invoiceId:guid}/payment", async (Guid contractId, Guid invoiceId, RecordInvoicePaymentRequest request, IContractInvoiceService service, CancellationToken cancellationToken) =>
                (await service.RecordPaymentAsync(contractId, invoiceId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(ContractPermissions.RecordPayments);

        return app;
    }
}
