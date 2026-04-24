using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SimplerJiangAiAgent.Api.Infrastructure.Storage;

/// <summary>
/// Sets PRAGMA busy_timeout on every opened SQLite connection so concurrent writers
/// wait instead of immediately failing with "database table is locked".
/// </summary>
public sealed class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    private readonly int _timeoutMs;

    public SqliteBusyTimeoutInterceptor(int timeoutMs = 15000)
    {
        _timeoutMs = timeoutMs;
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetBusyTimeout(connection);
    }

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        SetBusyTimeout(connection);
        return Task.CompletedTask;
    }

    private void SetBusyTimeout(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        cmd.CommandText = $"PRAGMA busy_timeout = {_timeoutMs};";
        cmd.ExecuteNonQuery();
    }
}
