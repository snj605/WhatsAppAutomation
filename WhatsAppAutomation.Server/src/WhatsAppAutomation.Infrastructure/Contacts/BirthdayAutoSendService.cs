using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Features.Broadcasts;
using WhatsAppAutomation.Application.Features.Contacts;

namespace WhatsAppAutomation.Infrastructure.Contacts;

/// <summary>
/// Checks every 30 seconds whose automatic birthday wishes are due (see BirthdayAutoSender). Switched off together with
/// the broadcast sender (Broadcasting:Enabled = false), as in the automated tests.
/// </summary>
internal sealed class BirthdayAutoSendService(
    IServiceScopeFactory scopes, IOptions<BroadcastOptions> options, ILogger<BirthdayAutoSendService> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(Tick);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<BirthdayAutoSender>().RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The automatic birthday wishes failed; they are tried again in {Seconds} seconds.", Tick.TotalSeconds);
            }
        }
    }
}
