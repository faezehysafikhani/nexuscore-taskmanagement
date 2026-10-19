using Nexus.ProjectManagement.Core.Application;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Contracts.Application;
using Nexus.ProjectManagement.Contracts.Application.Dtos;
using Nexus.ProjectManagement.Contracts.Application.EventHandlers;
using Nexus.ProjectManagement.Contracts.Application.Validators;
using Nexus.ProjectManagement.Contracts.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Contracts;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). Optional: AddWorkflowApplication() - without
    /// it contracts, addenda and invoices are approved as soon as they are submitted.</summary>
    public static IServiceCollection AddContractManagement(this IServiceCollection services)
    {
        
        services.AddScoped<IProjectDeletionParticipant, ContractsProjectDeletionParticipant>();services.AddScoped<IContractService, ContractService>();
        services.AddScoped<IContractAddendumService, ContractAddendumService>();
        services.AddScoped<IContractInvoiceService, ContractInvoiceService>();
        services.AddScoped<ISupplierService, SupplierService>();
        services.AddScoped<IContractCommitmentService, ContractCommitmentService>();
        services.AddScoped<IContractLinkService, ContractLinkService>();
        services.AddScoped<ISupplierEvaluationService, SupplierEvaluationService>();
        services.AddScoped<ISupplierDashboardService, SupplierDashboardService>();
        services.AddScoped<IChangeRequestService, ChangeRequestService>();

        services.AddScoped<IValidator<CreateContractRequest>, CreateContractRequestValidator>();
        services.AddScoped<IValidator<UpdateContractRequest>, UpdateContractRequestValidator>();
        services.AddScoped<IValidator<CreateContractAddendumRequest>, CreateContractAddendumRequestValidator>();
        services.AddScoped<IValidator<UpdateContractAddendumRequest>, UpdateContractAddendumRequestValidator>();
        services.AddScoped<IValidator<CreateContractInvoiceRequest>, CreateContractInvoiceRequestValidator>();
        services.AddScoped<IValidator<UpdateContractInvoiceRequest>, UpdateContractInvoiceRequestValidator>();
        services.AddScoped<IValidator<CreateSupplierRequest>, CreateSupplierRequestValidator>();
        services.AddScoped<IValidator<UpdateSupplierRequest>, UpdateSupplierRequestValidator>();

        services.AddSingleton<IPermissionCatalog, ContractPermissionCatalog>();
        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ContractApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ContractApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ContractPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
