using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using Testcontainers.PostgreSql;
using WhatsAppAutomation.Api.Contracts;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Domain.Authorization;
using WhatsAppAutomation.Domain.Entities;
using WhatsAppAutomation.Infrastructure.Identity;
using WhatsAppAutomation.Infrastructure.Persistence;

[assembly: AssemblyFixture(typeof(WhatsAppAutomation.Api.Tests.Infrastructure.ApiFactory))]

namespace WhatsAppAutomation.Api.Tests.Infrastructure;

/// <summary>
/// Runs the real app against a real PostgreSQL database created for this test run.
/// Set WAA_TEST_POSTGRES to a connection string (any database on the server) to use an existing
/// server; otherwise a PostgreSQL container is started with Testcontainers (needs Docker).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestPassword = "correct horse battery staple";

    private PostgreSqlContainer? _container;
    private string _adminConnectionString = string.Empty;
    private string _databaseName = string.Empty;

    public const string GatewaySecret = "test-gateway-secret-that-is-long-enough-0123";

    public string ConnectionString { get; private set; } = string.Empty;

    public FakeWhatsAppGateway Gateway { get; } = new();

    public RecordingNotifier Notifier { get; } = new();

    public async ValueTask InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable("WAA_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(external))
        {
            _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
            await _container.StartAsync();
            _adminConnectionString = _container.GetConnectionString();
        }
        else
        {
            _adminConnectionString = external;
        }

        _databaseName = $"waa_test_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE {_databaseName}");
        ConnectionString = new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = _databaseName }.ConnectionString;

        await using var scope = Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public new async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        if (_container is null)
        {
            await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
        }
        else
        {
            await _container.DisposeAsync();
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", ConnectionString);
        builder.UseSetting("Auth:SigningKey", "test-signing-key-that-is-long-enough-0123456789");
        builder.UseSetting("RateLimits:AuthPermitsPerMinute", "100000");
        builder.UseSetting("RateLimits:SendPermitsPerMinute", "100000");
        builder.UseSetting("Gateway:Secret", GatewaySecret);
        builder.UseSetting("Files:Path", Path.Combine(Path.GetTempPath(), "whatsapp-tests-files", Guid.NewGuid().ToString("N")));

        // The tests run the paced sender step by step, with no waiting and no sending hours.
        builder.UseSetting("Broadcasting:Enabled", "false");
        builder.UseSetting("Broadcasting:NoWaits", "true");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWhatsAppGateway>();
            services.AddSingleton<IWhatsAppGateway>(Gateway);
            services.RemoveAll<IWhatsAppNotifier>();
            services.AddSingleton<IWhatsAppNotifier>(Notifier);
        });
    }

    /// <summary>The id of a user, by user name.</summary>
    public async Task<Guid> UserIdAsync(string userName)
    {
        await using var scope = Services.CreateAsyncScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync(userName);
        return user!.Id;
    }

    private static int _phone = Random.Shared.Next(10_000_000, 50_000_000);

    /// <summary>
    /// A WhatsApp number no other test uses: one number belongs to one user on the whole platform, and the
    /// tests share one database.
    /// </summary>
    public static string UniquePhone() => "+9199" + Interlocked.Increment(ref _phone).ToString("D8", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Creates a tenant with a unique name and returns its id.</summary>
    public async Task<Guid> CreateTenantAsync(bool isActive = true)
    {
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenant = new Tenant("Tenant " + Guid.NewGuid().ToString("N")[..10], DateTimeOffset.UtcNow);
        tenant.SetActive(isActive);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant.Id;
    }

    /// <summary>
    /// Creates a user with a unique name so tests do not interfere with each other. Without a tenant, a tenant
    /// admin or member gets a new tenant of their own.
    /// </summary>
    public async Task<string> CreateUserAsync(
        UserKind kind = UserKind.Member,
        bool isActive = true,
        Guid? tenantId = null,
        IReadOnlyCollection<string>? switches = null,
        bool mustChangePassword = false)
    {
        var tenant = kind == UserKind.PlatformAdmin ? (Guid?)null : tenantId ?? await CreateTenantAsync();
        await using var scope = Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var userName = $"user_{Guid.NewGuid():N}"[..20];
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = userName,
            DisplayName = "Test " + userName,
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
            Kind = kind,
            TenantId = tenant,
            MustChangePassword = mustChangePassword,
        };

        var created = await users.CreateAsync(user, TestPassword);
        Assert.True(created.Succeeded, string.Join(", ", created.Errors.Select(e => e.Description)));
        if (kind == UserKind.Member && switches is { Count: > 0 })
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.UserSwitches.AddRange(switches.Select(p => new UserSwitch(user.Id, p)));
            await db.SaveChangesAsync();
        }

        scope.ServiceProvider.GetRequiredService<IPermissionService>().InvalidateAll();
        return userName;
    }

    /// <summary>The tenant a user belongs to.</summary>
    public async Task<Guid?> TenantIdAsync(string userName)
    {
        await using var scope = Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>().FindByNameAsync(userName))!.TenantId;
    }

    /// <summary>An HTTP client already signed in as this user, as one device (for example the app or a browser).</summary>
    public async Task<HttpClient> CreateClientForAsync(string userName, string deviceId = "test-device")
    {
        var client = CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new LoginRequest(userName, TestPassword, deviceId, null));
        response.EnsureSuccessStatusCode();
        var session = (await response.Content.ReadFromJsonAsync<SessionResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        return client;
    }

    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        await using var scope = Services.CreateAsyncScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>An empty extra database on the same server, for tests that build their own schema (migrations).</summary>
    public async Task<string> CreateScratchDatabaseAsync()
    {
        var name = $"waa_scratch_{Guid.NewGuid():N}";
        await ExecuteAdminAsync($"CREATE DATABASE {name}");
        return new NpgsqlConnectionStringBuilder(_adminConnectionString) { Database = name }.ConnectionString;
    }

    public async Task DropScratchDatabaseAsync(string connectionString)
    {
        NpgsqlConnection.ClearAllPools();
        var name = new NpgsqlConnectionStringBuilder(connectionString).Database;
        await ExecuteAdminAsync($"DROP DATABASE IF EXISTS {name} WITH (FORCE)");
    }

    private async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_adminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
