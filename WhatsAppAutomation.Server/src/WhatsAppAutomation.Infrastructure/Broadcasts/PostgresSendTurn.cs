using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Infrastructure.Broadcasts;

/// <summary>
/// The turn is a PostgreSQL advisory lock held by this request's database connection. The connection stays
/// open until the turn is given back; if the process dies, PostgreSQL releases the lock with the connection.
/// </summary>
internal sealed class PostgresSendTurn(AppDbContext db) : ISendTurn
{
    public async Task<IAsyncDisposable?> TryTakeAsync(Guid accountId, CancellationToken cancellationToken)
    {
        // Advisory locks are numbered: the first eight bytes of the account id are unique enough for a lock.
        var key = BitConverter.ToInt64(accountId.ToByteArray(), 0);
        await db.Database.OpenConnectionAsync(cancellationToken);
        var taken = false;
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT pg_try_advisory_lock(@key)";
            AddKey(command, key);
            taken = await command.ExecuteScalarAsync(cancellationToken) is true;
            return taken ? new Turn(db, key) : null;
        }
        finally
        {
            if (!taken)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }

    private static void AddKey(DbCommand command, long key)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = "key";
        parameter.Value = key;
        command.Parameters.Add(parameter);
    }

    private sealed class Turn(AppDbContext db, long key) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT pg_advisory_unlock(@key)";
                AddKey(command, key);
                await command.ExecuteScalarAsync(CancellationToken.None);
            }
            finally
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
