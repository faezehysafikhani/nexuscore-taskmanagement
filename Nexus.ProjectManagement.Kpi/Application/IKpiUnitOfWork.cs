using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Kpi.Application;

/// <summary>Distinct type identity so DI doesn't collide with other modules' IUnitOfWork
/// registrations - see Nexus.Organization.Application.IOrganizationUnitOfWork for the full note.</summary>
public interface IKpiUnitOfWork : IUnitOfWork;
