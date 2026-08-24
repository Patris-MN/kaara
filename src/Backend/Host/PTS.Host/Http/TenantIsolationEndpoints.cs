using Microsoft.EntityFrameworkCore;
using PTS.Host.Persistence.Testing;
using PTS.Host.TenantAccess;
using PTS.Modules.Identity;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Http;

public static class TenantIsolationEndpoints
{
    public static IEndpointRouteBuilder MapTenantIsolationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/tenants/{tenantId:guid}")
            .RequireAuthorization();

        group.MapGet("/isolation-records", ListAsync);
        group.MapPost("/isolation-records", CreateAsync);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var records = await session.DbContext.TenantIsolationTestRecords
                .AsNoTracking()
                .ToListAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);

            return Results.Ok(records.Select(r => new IsolationRecordResponse(r.Id, r.TenantId, r.Value)));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }

    private static async Task<IResult> CreateAsync(
        Guid tenantId,
        CreateIsolationRecordRequest request,
        ICurrentUser currentUser,
        ITenantRlsSessionFactory sessions,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
        {
            return Results.Unauthorized();
        }

        try
        {
            await using var session = await sessions.OpenAsync(tenantId, cancellationToken);
            var record = new TenantIsolationTestRecord
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Value = request.Value,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            };
            session.DbContext.TenantIsolationTestRecords.Add(record);
            await session.DbContext.SaveChangesAsync(cancellationToken);
            await session.CommitAsync(cancellationToken);

            return Results.Created(
                $"/tenants/{tenantId}/isolation-records",
                new IsolationRecordResponse(record.Id, record.TenantId, record.Value));
        }
        catch (AuthenticationRequiredException)
        {
            return Results.Unauthorized();
        }
        catch (UnknownAuthenticatedUserException)
        {
            return Results.Unauthorized();
        }
        catch (TenantAccessDeniedException)
        {
            return Results.Json(new { error = "tenant_access_denied" }, statusCode: StatusCodes.Status403Forbidden);
        }
    }
}

public sealed record CreateIsolationRecordRequest(string Value);

public sealed record IsolationRecordResponse(Guid Id, Guid TenantId, string Value);
