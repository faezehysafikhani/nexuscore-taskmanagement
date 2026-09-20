using NexusCore.SharedKernel.Interfaces;

namespace Nexus.Organization.Application;

/// <summary>
/// Every module registers IUnitOfWork against its own DbContext. Since .NET DI resolves a
/// non-keyed service to the LAST registration, sharing the bare NexusCore.SharedKernel
/// IUnitOfWork type across modules would mean only one module's SaveChangesAsync ever actually
/// runs once two or more modules are composed together - every other module's changes would
/// silently never persist. A distinct marker interface per module (same inherited method
/// shape, distinct type identity) avoids the collision at compile time.
/// </summary>
public interface IOrganizationUnitOfWork : IUnitOfWork;
