using System.Diagnostics;
using Microsoft.Extensions.Options;
using Npgsql;
using WhatsAppAutomation.Infrastructure;
using WhatsAppAutomation.Infrastructure.WhatsApp;

namespace WhatsAppAutomation.Api.Development;

/// <summary>Section "GatewayDev": starting the WhatsApp gateway together with the API, in development only.</summary>
public sealed class GatewayDevOptions
{
    public const string SectionName = "GatewayDev";

    /// <summary>Start the gateway on F5. Turn off if you prefer to run "npm start" yourself.</summary>
    public bool AutoStart { get; set; }

    /// <summary>The gateway folder, relative to the Api project.</summary>
    public string ProjectPath { get; set; } = Path.Combine("..", "..", "..", "whatsappautomation.gateway");

    /// <summary>
    /// The key that encrypts the saved WhatsApp logins (32 bytes, base64; "dotnet run -- new-secret").
    /// Only passed to the gateway: the API itself never uses it. Changing it means linking again.
    /// </summary>
    public string AuthStateKey { get; set; } = string.Empty;

    /// <summary>Where the gateway posts its events. Plain http on this machine only.</summary>
    public Uri EventsUrl { get; set; } = new("http://localhost:5163/internal/whatsapp/events");
}

/// <summary>
/// Runs "node src/main.ts" in the gateway folder when the API starts in Development, the same way
/// Visual Studio's SPA proxy runs "npm start" for the Angular app. Production runs the gateway as
/// its own service instead. The gateway gets its settings as environment variables, and stops
/// when the API stops (its standard input closes).
/// </summary>
internal sealed class GatewayDevLauncher(
    IOptions<GatewayDevOptions> devOptions,
    IOptions<GatewayOptions> gatewayOptions,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    ILogger<GatewayDevLauncher> logger) : IHostedService, IDisposable
{
    private Process? _process;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var dev = devOptions.Value;
        var gateway = gatewayOptions.Value;
        if (!dev.AutoStart)
        {
            return;
        }

        var folder = Path.GetFullPath(Path.Combine(environment.ContentRootPath, dev.ProjectPath));
        if (!gateway.IsConfigured || !IsValidKey(dev.AuthStateKey))
        {
            logger.LogWarning(
                "WhatsApp gateway not started: set Gateway:Secret and GatewayDev:AuthStateKey in user secrets " +
                "(values from \"dotnet run -- new-secret\"). See the README.");
            return;
        }

        if (!Directory.Exists(Path.Combine(folder, "node_modules")))
        {
            logger.LogWarning("WhatsApp gateway not started: run \"npm install\" in {Folder} first.", folder);
            return;
        }

        if (await AlreadyRunningAsync(gateway.BaseUrl, cancellationToken))
        {
            logger.LogInformation("WhatsApp gateway is already running at {BaseUrl}; not starting another one.", gateway.BaseUrl);
            return;
        }

        var start = new ProcessStartInfo("node", "src/main.ts")
        {
            WorkingDirectory = folder,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        AddEnvironment(start.Environment, gateway, dev);

        try
        {
            _process = Process.Start(start);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            logger.LogWarning("WhatsApp gateway not started: could not run \"node\" ({Error}). Is Node.js installed?", ex.Message);
            return;
        }

        if (_process is null)
        {
            return;
        }

        _process.OutputDataReceived += (_, e) => Forward(e.Data, LogLevel.Information);
        _process.ErrorDataReceived += (_, e) => Forward(e.Data, LogLevel.Warning);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        logger.LogInformation("Started the WhatsApp gateway (process {Id}) from {Folder}", _process.Id, folder);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.StandardInput.Close(); // the gateway stops cleanly when its stdin closes
                if (!_process.WaitForExit(TimeSpan.FromSeconds(5)))
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (InvalidOperationException)
            {
                // already exited
            }
        }

        return Task.CompletedTask;
    }

    public void Dispose() => _process?.Dispose();

    private void AddEnvironment(IDictionary<string, string?> env, GatewayOptions gateway, GatewayDevOptions dev)
    {
        env["GATEWAY_SECRET"] = gateway.Secret;
        env["GATEWAY_AUTH_KEY"] = dev.AuthStateKey;
        env["GATEWAY_HOST"] = gateway.BaseUrl.Host;
        env["GATEWAY_PORT"] = gateway.BaseUrl.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["GATEWAY_EVENTS_URL"] = dev.EventsUrl.AbsoluteUri;
        env["GATEWAY_EXIT_ON_STDIN_CLOSE"] = "true";

        // The gateway uses the same database as the API, through the standard PostgreSQL variables.
        var db = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString(DependencyInjection.ConnectionStringName));
        env["PGHOST"] = db.Host;
        env["PGPORT"] = db.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
        env["PGDATABASE"] = db.Database;
        env["PGUSER"] = db.Username;
        env["PGPASSWORD"] = db.Password;
    }

    private async Task<bool> AlreadyRunningAsync(Uri baseUrl, CancellationToken cancellationToken)
    {
        using var http = httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(2);
        try
        {
            using var response = await http.GetAsync(new Uri(baseUrl, "/health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private void Forward(string? line, LogLevel level)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            logger.Log(level, "[gateway] {Line}", line);
        }
    }

    private static bool IsValidKey(string key)
    {
        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(key, buffer, out var length) && length == 32;
    }
}
