using Microsoft.Extensions.Diagnostics.HealthChecks;
using WhatsAppAutomation.Application.Common.Interfaces;

namespace WhatsAppAutomation.Infrastructure.WhatsApp;

/// <summary>/health reports "Degraded" (not "Unhealthy") when the gateway is down: sign-in and the rest still work.</summary>
internal sealed class WhatsAppGatewayHealthCheck(IWhatsAppGateway gateway) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        await gateway.IsHealthyAsync(cancellationToken)
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Degraded("The WhatsApp gateway is not configured or not reachable.");
}
