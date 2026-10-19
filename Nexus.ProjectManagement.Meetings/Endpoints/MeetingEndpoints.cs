using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Nexus.ProjectManagement.Meetings.Application;
using Nexus.ProjectManagement.Meetings.Application.Dtos;
using Nexus.ProjectManagement.Meetings.Domain;
using Nexus.ProjectManagement.Meetings.Permissions;
using NexusCore.Application.Common;
using NexusCore.SharedKernel.Interfaces;

namespace Nexus.ProjectManagement.Meetings.Endpoints;

public static class MeetingEndpoints
{
    public static IEndpointRouteBuilder MapMeetingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/project-management/meetings").WithTags("Meetings").RequireAuthorization();

        group.MapGet("/", async (ICurrentUserContext user, Guid projectId, MeetingStatus? status, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.ListByProjectAsync(tenant, projectId, status, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.View);

        // The meetings of several projects in one call (what a dashboard or calendar needs).
        group.MapPost("/for-projects", async (ICurrentUserContext user, ListMeetingsForProjectsRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.ListForProjectsAsync(tenant, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.View);

        group.MapGet("/{id:guid}", async (ICurrentUserContext user, Guid id, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.GetAsync(tenant, id, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.View);

        group.MapPost("/", async (ICurrentUserContext user, CreateMeetingRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.CreateAsync(tenant, user.UserId, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Create);

        group.MapPut("/{id:guid}", async (ICurrentUserContext user, Guid id, UpdateMeetingRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.UpdateAsync(tenant, id, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Edit);

        group.MapPut("/{id:guid}/status", async (ICurrentUserContext user, Guid id, ChangeMeetingStatusRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.ChangeStatusAsync(tenant, id, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Edit);

        group.MapPut("/{id:guid}/participants", async (ICurrentUserContext user, Guid id, SetMeetingParticipantsRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.SetParticipantsAsync(tenant, id, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Edit);

        group.MapPut("/{id:guid}/participants/{userId:guid}/attendance", async (ICurrentUserContext user, Guid id, Guid userId, SetAttendanceRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.SetAttendanceAsync(tenant, id, userId, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Edit);

        group.MapPut("/{id:guid}/minutes", async (ICurrentUserContext user, Guid id, SetMinutesRequest request, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.SetMinutesAsync(tenant, id, user.UserId, request, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Edit);

        group.MapDelete("/{id:guid}", async (ICurrentUserContext user, Guid id, IMeetingService service, CancellationToken cancellationToken) =>
                user.TenantId is { } tenant
                    ? (await service.DeleteAsync(tenant, id, cancellationToken)).ToApiResult()
                    : Results.Unauthorized())
            .RequireAuthorization(MeetingPermissions.Delete);

        return app;
    }
}
