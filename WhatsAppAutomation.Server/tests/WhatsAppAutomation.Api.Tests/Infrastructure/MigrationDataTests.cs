using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WhatsAppAutomation.Infrastructure.Persistence;

namespace WhatsAppAutomation.Api.Tests.Infrastructure;

/// <summary>
/// The client's real data must survive every update. This builds the database as it was before occasions and the
/// tenant library (migration AddTenants), fills it with old-style data, upgrades it to the latest version and checks
/// that everything is still there and was moved to the new places.
/// </summary>
public sealed class MigrationDataTests(ApiFactory factory)
{
    private const string BeforeOccasions = "20261117205235_AddTenants";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Upgrading_keeps_old_data_and_moves_birthday_settings_queue_order_and_template_tenants()
    {
        var connectionString = await factory.CreateScratchDatabaseAsync();
        try
        {
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.SetConnectionString(connectionString);
                await db.GetService<IMigrator>().MigrateAsync(BeforeOccasions, cancellationToken: Ct);
            }

            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(Ct);

            var tenant = Guid.NewGuid();
            var member = Guid.NewGuid();
            var admin = Guid.NewGuid();
            var platform = Guid.NewGuid();
            var template = Guid.NewGuid();
            var wishes = Guid.NewGuid();
            var later = Guid.NewGuid();
            var contact = Guid.NewGuid();

            await InsertAsync(connection, "tenants", new() { ["id"] = tenant, ["name"] = "My company", ["is_active"] = true });
            await InsertAsync(connection, "users", new() { ["id"] = member, ["kind"] = "member", ["tenant_id"] = tenant });
            await InsertAsync(connection, "users", new() { ["id"] = admin, ["kind"] = "tenant_admin", ["tenant_id"] = tenant });
            await InsertAsync(connection, "users", new() { ["id"] = platform, ["kind"] = "platform_admin", ["tenant_id"] = null });
            await InsertAsync(connection, "message_templates", new() { ["id"] = template, ["user_id"] = member, ["name"] = "Wish", ["text"] = "Happy birthday {{name}}" });
            await InsertAsync(connection, "user_preferences", new()
            {
                ["user_id"] = member,
                ["birthday_auto_send"] = true,
                ["birthday_send_time"] = new TimeOnly(7, 15),
                ["birthday_template_id"] = template,
                ["birthday_auto_sent_on"] = new DateOnly(2026, 11, 17),
            });
            await InsertAsync(connection, "contacts", new()
            {
                ["id"] = contact, ["user_id"] = member, ["phone_number"] = "+919800000001", ["birth_day"] = 18, ["birth_month"] = 11,
            });
            await InsertAsync(connection, "broadcasts", new()
            {
                ["id"] = wishes, ["user_id"] = member, ["target"] = 4, ["target_id"] = null, ["status"] = "Completed",
                ["created_at"] = new DateTimeOffset(2026, 11, 17, 2, 0, 0, TimeSpan.Zero),
            });
            await InsertAsync(connection, "broadcasts", new()
            {
                ["id"] = later, ["user_id"] = member, ["target"] = 0, ["status"] = "Sending",
                ["created_at"] = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.Zero),
            });

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                db.Database.SetConnectionString(connectionString);
                await db.Database.MigrateAsync(Ct);
            }

            // Nothing was lost.
            Assert.Equal(3L, await ScalarAsync<long>(connection, "SELECT count(*) FROM users"));
            Assert.Equal(1L, await ScalarAsync<long>(connection, "SELECT count(*) FROM contacts WHERE birth_day = 18 AND birth_month = 11"));
            Assert.Equal(2L, await ScalarAsync<long>(connection, "SELECT count(*) FROM broadcasts"));

            // The member's birthday settings became their Birthday occasion; the admin got one too; the platform admin none.
            await using (var read = new NpgsqlCommand(
                "SELECT auto_send, send_time, template_id, auto_sent_on, name FROM occasion_modules WHERE user_id = @u AND kind = 'Birthday'", connection))
            {
                read.Parameters.AddWithValue("u", member);
                await using var row = await read.ExecuteReaderAsync(Ct);
                Assert.True(await row.ReadAsync(Ct));
                Assert.True(row.GetBoolean(0));
                Assert.Equal(new TimeOnly(7, 15), row.GetFieldValue<TimeOnly>(1));
                Assert.Equal(template, row.GetGuid(2));
                Assert.Equal(new DateOnly(2026, 11, 17), row.GetFieldValue<DateOnly>(3));
                Assert.Equal("Birthday", row.GetString(4));
                Assert.False(await row.ReadAsync(Ct));
            }

            Assert.Equal(1L, await ScalarAsync<long>(connection, $"SELECT count(*) FROM occasion_modules WHERE user_id = '{admin}' AND NOT auto_send"));
            Assert.Equal(0L, await ScalarAsync<long>(connection, $"SELECT count(*) FROM occasion_modules WHERE user_id = '{platform}'"));

            // Old birthday wishes belong to that occasion; broadcasts keep their order.
            var moduleId = await ScalarAsync<Guid>(connection, $"SELECT id FROM occasion_modules WHERE user_id = '{member}'");
            Assert.Equal(moduleId, await ScalarAsync<Guid>(connection, $"SELECT target_id FROM broadcasts WHERE id = '{wishes}'"));
            Assert.Equal(1, await ScalarAsync<int>(connection, $"SELECT queue_position FROM broadcasts WHERE id = '{wishes}'"));
            Assert.Equal(2, await ScalarAsync<int>(connection, $"SELECT queue_position FROM broadcasts WHERE id = '{later}'"));

            // Existing users get the default sending settings: 300 a day, 50 in one go, 30-90 s, 09:00-21:00.
            await using (var prefs = new NpgsqlCommand(
                "SELECT daily_limit, chunk_size, min_gap_seconds, max_gap_seconds, send_from, send_until FROM user_preferences WHERE user_id = @u", connection))
            {
                prefs.Parameters.AddWithValue("u", member);
                await using var row = await prefs.ExecuteReaderAsync(Ct);
                Assert.True(await row.ReadAsync(Ct));
                Assert.Equal(300, row.GetInt32(0));
                Assert.Equal(50, row.GetInt32(1));
                Assert.Equal(30, row.GetInt32(2));
                Assert.Equal(90, row.GetInt32(3));
                Assert.Equal(new TimeOnly(9, 0), row.GetFieldValue<TimeOnly>(4));
                Assert.Equal(new TimeOnly(21, 0), row.GetFieldValue<TimeOnly>(5));
            }

            // Templates belong to their creator's tenant and stay private.
            Assert.Equal(tenant, await ScalarAsync<Guid>(connection, $"SELECT tenant_id FROM message_templates WHERE id = '{template}'"));
            Assert.False(await ScalarAsync<bool>(connection, $"SELECT is_shared FROM message_templates WHERE id = '{template}'"));
        }
        finally
        {
            await factory.DropScratchDatabaseAsync(connectionString);
        }
    }

    /// <summary>
    /// Inserts one row. Required columns not given are filled with a harmless value of their type, so the test does
    /// not depend on every column of the old schema.
    /// </summary>
    private static async Task InsertAsync(NpgsqlConnection connection, string table, Dictionary<string, object?> values)
    {
        var required = new List<(string Name, string Type, int? Length)>();
        await using (var columns = new NpgsqlCommand(
            """
            SELECT column_name, data_type, character_maximum_length FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = @t AND is_nullable = 'NO' AND column_default IS NULL AND is_identity = 'NO'
            """, connection))
        {
            columns.Parameters.AddWithValue("t", table);
            await using var reader = await columns.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
            {
                required.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetInt32(2)));
            }
        }

        Assert.NotEmpty(required);
        var all = new Dictionary<string, object?>(values);
        foreach (var (name, type, length) in required.Where(c => !values.ContainsKey(c.Name)))
        {
            all[name] = type switch
            {
                "uuid" => Guid.NewGuid(),
                "boolean" => false,
                "smallint" => (short)1,
                "integer" => 1,
                "bigint" => 1L,
                "numeric" or "double precision" or "real" => 1,
                "timestamp with time zone" => DateTimeOffset.UtcNow,
                "timestamp without time zone" => DateTime.UtcNow,
                "date" => DateOnly.FromDateTime(DateTime.UtcNow),
                "time without time zone" => new TimeOnly(9, 0),
                "bytea" => Array.Empty<byte>(),
                "jsonb" or "json" => "{}",
                // Two-letter columns are language codes.
                _ when length == 2 => "en",
                _ => $"x{Guid.NewGuid():N}"[..Math.Min(12, length ?? 12)],
            };
        }

        var names = all.Keys.ToList();
        await using var insert = new NpgsqlCommand(
            $"INSERT INTO {table} ({string.Join(", ", names)}) VALUES ({string.Join(", ", names.Select((_, i) => $"@p{i}"))})", connection);
        for (var i = 0; i < names.Count; i++)
        {
            var type = required.FirstOrDefault(c => c.Name == names[i]).Type;
            var parameter = new NpgsqlParameter($"p{i}", all[names[i]] ?? DBNull.Value);
            if (type is "jsonb")
            {
                parameter.NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb;
            }

            insert.Parameters.Add(parameter);
        }

        await insert.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Ct))!;
    }
}
