using Microsoft.Extensions.DependencyInjection;
using Nexus.Integrations.ProjectDeletion.Application;
using Nexus.ProjectManagement.Core.Application;

namespace Nexus.Integrations.ProjectDeletion;

public static class DependencyInjection
{
    /// <summary>Lets Project Core's delete take Team, Deliverables, KPIs and Actions into account. Each of those is optional;
    /// without this integration they are simply not consulted. Registers no endpoints and owns no data.</summary>
    public static IServiceCollection AddProjectDeletionIntegration(this IServiceCollection services)
    {
        services.AddScoped<IProjectDeletionParticipant, ProjectDataDeletionParticipant>();
        return services;
    }
}
