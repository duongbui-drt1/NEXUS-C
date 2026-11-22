using System.Collections.Concurrent;
using System.Threading.Channels;
using Nexus.SharedKernel.Contracts;

namespace Nexus.BuildingBlocks.Messaging;

public sealed record RealtimeSubscriberContext(
    Guid SubscriberId,
    Guid? TenantId,
    bool IsPlatformScope,
    IReadOnlySet<string> GrantedPermissions);

public interface IPlatformRealtimeSubscriber
{
    (Guid SubscriberId, ChannelReader<PlatformRealtimeEvent> Reader, IReadOnlyList<PlatformRealtimeEvent> ReplayEvents) Subscribe(
        Guid? tenantId,
        bool isPlatformScope,
        IReadOnlyCollection<string> grantedPermissions,
        string? lastEventId = null);

    void Unsubscribe(Guid subscriberId);
}

/// <summary>
/// Bounded, tenant-isolated, permission-filtered real-time event hub for Server-Sent Events (SSE).
/// Enforces DropOldest backpressure so slow clients never cause unbounded memory growth,
/// and strictly filters every event by tenant scope and granted RBAC permissions before delivery.
/// </summary>
public sealed class PlatformRealtimeHub : IPlatformRealtimePublisher, IPlatformRealtimeSubscriber
{
    private const int ChannelCapacity = 128;
    private const int ReplayBufferCapacity = 100;

    private readonly ConcurrentDictionary<Guid, (RealtimeSubscriberContext Context, Channel<PlatformRealtimeEvent> Channel)> _subscribers = new();
    private readonly object _replayLock = new();
    private readonly Queue<PlatformRealtimeEvent> _replayBuffer = new();

    public ValueTask PublishAsync(PlatformRealtimeEvent evt, CancellationToken ct = default)
    {
        lock (_replayLock)
        {
            _replayBuffer.Enqueue(evt);
            while (_replayBuffer.Count > ReplayBufferCapacity)
            {
                _replayBuffer.Dequeue();
            }
        }

        foreach (var (_, (ctx, channel)) in _subscribers)
        {
            if (IsAuthorizedForEvent(ctx, evt))
            {
                channel.Writer.TryWrite(evt);
            }
        }

        return ValueTask.CompletedTask;
    }

    public (Guid SubscriberId, ChannelReader<PlatformRealtimeEvent> Reader, IReadOnlyList<PlatformRealtimeEvent> ReplayEvents) Subscribe(
        Guid? tenantId,
        bool isPlatformScope,
        IReadOnlyCollection<string> grantedPermissions,
        string? lastEventId = null)
    {
        var id = Guid.CreateVersion7();
        var permSet = grantedPermissions.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ctx = new RealtimeSubscriberContext(id, tenantId, isPlatformScope, permSet);

        var channel = Channel.CreateBounded<PlatformRealtimeEvent>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropOldest
        });

        List<PlatformRealtimeEvent> replay = [];
        if (!string.IsNullOrWhiteSpace(lastEventId))
        {
            lock (_replayLock)
            {
                var found = false;
                foreach (var evt in _replayBuffer)
                {
                    if (!found)
                    {
                        if (string.Equals(evt.EventId, lastEventId.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            found = true;
                        }
                        continue;
                    }

                    if (IsAuthorizedForEvent(ctx, evt))
                    {
                        replay.Add(evt);
                    }
                }
            }
        }

        _subscribers[id] = (ctx, channel);
        return (id, channel.Reader, replay);
    }

    public void Unsubscribe(Guid subscriberId)
    {
        if (_subscribers.TryRemove(subscriberId, out var entry))
        {
            entry.Channel.Writer.TryComplete();
        }
    }

    private static bool IsAuthorizedForEvent(RealtimeSubscriberContext sub, PlatformRealtimeEvent evt)
    {
        // Permission check is mandatory for both platform and tenant subscribers
        if (!string.IsNullOrWhiteSpace(evt.RequiredPermission) &&
            !sub.GrantedPermissions.Contains(evt.RequiredPermission))
        {
            return false;
        }

        if (sub.IsPlatformScope)
        {
            return true;
        }

        // Strict tenant isolation: tenant subscribers NEVER receive platform-level or other-tenant events!
        return sub.TenantId.HasValue && evt.TenantId.HasValue && evt.TenantId.Value == sub.TenantId.Value;
    }
}
