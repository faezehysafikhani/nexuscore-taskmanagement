using Microsoft.Extensions.DependencyInjection;
using Nexus.Integrations.ProjectVisibility.Application;
using Nexus.ProjectManagement.Core.Application;

namespace Nexus.Integrations.ProjectVisibility;

public static class DependencyInjection
{
    /// <summary>Widens what Portfolio shows a user beyond the work they own, manage or are responsible for:
    /// to the projects they are a team member of, the units they manage (and everything below), and the
    /// projects/actions waiting for their approval. Each source is optional - install Team, Organization
    /// or Workflow, or not - and a missing one simply grants nothing. Registers no endpoints and owns no data.</summary>
    public static IServiceCollection AddProjectVisibilityIntegration(this IServiceCollection services)
    {
        services.AddScoped<IVisibilityProvider, TeamMembershipVisibilityProvider>();
        services.AddScoped<IVisibilityProvider, OrganizationUnitManagerVisibilityProvider>();
        services.AddScoped<IVisibilityProvider, PendingApprovalVisibilityProvider>();
        return services;
    }
}
