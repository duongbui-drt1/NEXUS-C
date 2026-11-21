using System.Data;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nexus.SharedKernel.Context;

namespace Nexus.BuildingBlocks.Persistence;

/// <summary>
/// Enforces PostgreSQL Row-Level Security (RLS) session variables on every connection and transaction,
/// and clears them on connection close so pooled Npgsql connections never leak tenant context.
/// </summary>
public sealed class TenantRlsInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    internal sealed class AppliedState
    {
        public string TenantId { get; set; } = string.Empty;
        public string Bypass { get; set; } = "off";
        public bool IsSet { get; set; }
    }

    internal static readonly ConditionalWeakTable<DbConnection, AppliedState> ConnectionStates = new();

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ApplySessionVariables(connection, transaction: null, isLocal: false);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplySessionVariablesAsync(connection, transaction: null, isLocal: false, cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        ClearSessionVariables(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection,
        ConnectionEventData eventData,
        InterceptionResult result)
    {
        await ClearSessionVariablesAsync(connection);
        return await base.ConnectionClosingAsync(connection, eventData, result);
    }

    internal void ApplySessionVariables(DbConnection connection, DbTransaction? transaction, bool isLocal)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        var (tid, bypass) = GetDesiredValues();
        using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tid, @is_local), set_config('app.bypass_rls', @bypass, @is_local);";
        AddParam(cmd, "@tid", tid);
        AddParam(cmd, "@bypass", bypass);
        AddParam(cmd, "@is_local", isLocal);
        cmd.ExecuteNonQuery();

        var state = ConnectionStates.GetOrCreateValue(connection);
        state.TenantId = tid;
        state.Bypass = bypass;
        state.IsSet = true;
    }

    internal async Task ApplySessionVariablesAsync(
        DbConnection connection,
        DbTransaction? transaction,
        bool isLocal,
        CancellationToken ct)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        var (tid, bypass) = GetDesiredValues();
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT set_config('app.current_tenant_id', @tid, @is_local), set_config('app.bypass_rls', @bypass, @is_local);";
        AddParam(cmd, "@tid", tid);
        AddParam(cmd, "@bypass", bypass);
        AddParam(cmd, "@is_local", isLocal);
        await cmd.ExecuteNonQueryAsync(ct);

        var state = ConnectionStates.GetOrCreateValue(connection);
        state.TenantId = tid;
        state.Bypass = bypass;
        state.IsSet = true;
    }

    private static void ClearSessionVariables(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false), set_config('app.bypass_rls', 'off', false);";
            cmd.ExecuteNonQuery();
        }
        catch
        {
            // Connection may already be broken/terminated; Npgsql will discard broken connections.
        }
        finally
        {
            ConnectionStates.Remove(connection);
        }
    }

    private static async Task ClearSessionVariablesAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        try
        {
            await using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT set_config('app.current_tenant_id', '', false), set_config('app.bypass_rls', 'off', false);";
            await cmd.ExecuteNonQueryAsync();
        }
        catch
        {
            // Connection may already be broken/terminated; Npgsql will discard broken connections.
        }
        finally
        {
            ConnectionStates.Remove(connection);
        }
    }

    internal (string TenantId, string Bypass) GetDesiredValues()
    {
        var tid = tenantContext.HasTenant ? tenantContext.TenantId!.Value.ToString("D") : string.Empty;
        var bypass = tenantContext.IsPlatformBypass ? "on" : "off";
        return (tid, bypass);
    }

    private static void AddParam(DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}

public sealed class TenantRlsTransactionInterceptor(TenantRlsInterceptor connectionInterceptor) : DbTransactionInterceptor
{
    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
    {
        connectionInterceptor.ApplySessionVariables(connection, result, isLocal: true);
        return base.TransactionStarted(connection, eventData, result);
    }

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await connectionInterceptor.ApplySessionVariablesAsync(connection, result, isLocal: true, cancellationToken);
        return await base.TransactionStartedAsync(connection, eventData, result, cancellationToken);
    }
}

public sealed class TenantRlsCommandInterceptor(TenantRlsInterceptor connectionInterceptor) : DbCommandInterceptor
{
    private const string ConfigMarker = "set_config('app.current_tenant_id'";

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result)
    {
        EnsureSynced(command);
        return base.ReaderExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureSyncedAsync(command, cancellationToken);
        return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result)
    {
        EnsureSynced(command);
        return base.NonQueryExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureSyncedAsync(command, cancellationToken);
        return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
    }

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result)
    {
        EnsureSynced(command);
        return base.ScalarExecuting(command, eventData, result);
    }

    public override async ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<object> result,
        CancellationToken cancellationToken = default)
    {
        await EnsureSyncedAsync(command, cancellationToken);
        return await base.ScalarExecutingAsync(command, eventData, result, cancellationToken);
    }

    private void EnsureSynced(DbCommand command)
    {
        if (command.Connection is null || command.CommandText.Contains(ConfigMarker, StringComparison.Ordinal))
        {
            return;
        }

        var (tid, bypass) = connectionInterceptor.GetDesiredValues();
        var state = TenantRlsInterceptor.ConnectionStates.GetOrCreateValue(command.Connection);
        if (!state.IsSet || state.TenantId != tid || state.Bypass != bypass)
        {
            connectionInterceptor.ApplySessionVariables(command.Connection, command.Transaction, command.Transaction is not null);
        }
    }

    private async Task EnsureSyncedAsync(DbCommand command, CancellationToken ct)
    {
        if (command.Connection is null || command.CommandText.Contains(ConfigMarker, StringComparison.Ordinal))
        {
            return;
        }

        var (tid, bypass) = connectionInterceptor.GetDesiredValues();
        var state = TenantRlsInterceptor.ConnectionStates.GetOrCreateValue(command.Connection);
        if (!state.IsSet || state.TenantId != tid || state.Bypass != bypass)
        {
            await connectionInterceptor.ApplySessionVariablesAsync(command.Connection, command.Transaction, command.Transaction is not null, ct);
        }
    }
}
