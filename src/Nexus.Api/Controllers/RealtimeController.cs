using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Nexus.BuildingBlocks.Messaging;
using Nexus.SharedKernel.Context;
using Nexus.SharedKernel.Contracts;

namespace Nexus.Api.Controllers;

[ApiController]
[Route("api/v1/platform/realtime")]
public sealed class RealtimeController(
    IPlatformRealtimeSubscriber realtimeSubscriber,
    IPermissionEvaluator permissionEvaluator,
    ICurrentUser currentUser,
    ITenantContext tenantContext) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Authenticated, tenant-isolated, permission-filtered Server-Sent Events (SSE) operational stream.
    /// Supports Last-Event-ID replay and periodic keepalive heartbeats.
    /// </summary>
    [HttpGet("stream")]
    [Authorize]
    public async Task Stream(
        [FromQuery] int? maxEvents = null,
        CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated || !currentUser.UserId.HasValue)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var isPlatformScope = currentUser.IsPlatformUser && !tenantContext.HasTenant;
        IReadOnlyCollection<string> grantedPermissions = currentUser.IsServicePrincipal
            ? currentUser.GrantedScopes
            : await permissionEvaluator.GetPermissionsAsync(currentUser.UserId.Value, tenantContext.TenantId, isPlatformScope, ct);

        Response.StatusCode = StatusCodes.Status200OK;
        Response.Headers.ContentType = "text/event-stream; charset=utf-8";
        Response.Headers.CacheControl = "no-cache, no-store";
        Response.Headers["X-Accel-Buffering"] = "no";

        var lastEventId = Request.Headers["Last-Event-ID"].FirstOrDefault();
        var (subId, reader, replayEvents) = realtimeSubscriber.Subscribe(
            tenantContext.TenantId,
            isPlatformScope,
            grantedPermissions,
            lastEventId);

        try
        {
            var connectedPayload = JsonSerializer.Serialize(new
            {
                status = "connected",
                scope = isPlatformScope ? "platform" : "tenant",
                tenantId = tenantContext.TenantId,
                connectedAt = DateTimeOffset.UtcNow
            }, JsonOpts);

            await Response.WriteAsync($"event: connected\ndata: {connectedPayload}\n\n", ct);
            await Response.Body.FlushAsync(ct);

            var delivered = 0;
            foreach (var replayed in replayEvents)
            {
                await WriteEventFrameAsync(replayed, ct);
                delivered++;
                if (maxEvents.HasValue && delivered >= maxEvents.Value)
                {
                    return;
                }
            }

            if (maxEvents.HasValue && maxEvents.Value == 0)
            {
                return;
            }

            using var heartbeatTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));

            while (!ct.IsCancellationRequested)
            {
                var readTask = reader.WaitToReadAsync(ct).AsTask();
                var tickTask = heartbeatTimer.WaitForNextTickAsync(ct).AsTask();
                var completed = await Task.WhenAny(readTask, tickTask);

                if (completed == readTask)
                {
                    if (!await readTask)
                    {
                        break;
                    }

                    while (reader.TryRead(out var evt))
                    {
                        await WriteEventFrameAsync(evt, ct);
                        delivered++;
                        if (maxEvents.HasValue && delivered >= maxEvents.Value)
                        {
                            return;
                        }
                    }
                }
                else
                {
                    await Response.WriteAsync(": keepalive\n\n", ct);
                    await Response.Body.FlushAsync(ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal client disconnect
        }
        finally
        {
            realtimeSubscriber.Unsubscribe(subId);
        }
    }

    private async Task WriteEventFrameAsync(PlatformRealtimeEvent evt, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(evt, JsonOpts);
        await Response.WriteAsync($"id: {evt.EventId}\nevent: {evt.EventType}\ndata: {json}\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
