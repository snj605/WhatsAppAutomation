using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WhatsAppAutomation.Application.Common.Interfaces;
using WhatsAppAutomation.Application.Common.Models;
using WhatsAppAutomation.Application.Features.WhatsApp;

namespace WhatsAppAutomation.Infrastructure.WhatsApp;

/// <summary>Calls the gateway's small HTTP API (see whatsappautomation.gateway/src/http.ts).</summary>
internal sealed class HttpWhatsAppGateway(HttpClient http, IOptions<GatewayOptions> options, ILogger<HttpWhatsAppGateway> logger)
    : IWhatsAppGateway
{
    public Task<Result> LinkAsync(Guid accountId, LinkMethod method, string? phoneNumber, CancellationToken cancellationToken) =>
        PostAsync($"accounts/{accountId}/link", new
        {
            method = method == LinkMethod.Qr ? "qr" : "code",
            phoneNumber,
        }, cancellationToken);

    public Task<Result> UnlinkAsync(Guid accountId, CancellationToken cancellationToken) =>
        PostAsync($"accounts/{accountId}/unlink", new { }, cancellationToken);

    public async Task<Result<string>> SendTextAsync(
        Guid accountId, string toPhoneNumber, string text, Guid reference, CancellationToken cancellationToken, GatewayFile? file = null)
    {
        object payload = file is null
            ? new { to = toPhoneNumber, text, reference }
            : new { to = toPhoneNumber, text, reference, file = new { id = file.Id, name = file.FileName, mimeType = file.ContentType } };
        var (response, error) = await SendAsync($"accounts/{accountId}/messages", payload, cancellationToken);
        if (response is null)
        {
            return error!;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ToErrorAsync(response, cancellationToken);
            }

            var body = await response.Content.ReadFromJsonAsync<SendResponse>(cancellationToken);
            return string.IsNullOrEmpty(body?.MessageId) ? WhatsAppErrors.SendFailed : body.MessageId;
        }
    }

    public async Task<Result<IReadOnlyDictionary<string, bool>>> CheckNumbersAsync(
        Guid accountId, IReadOnlyList<string> phoneNumbers, CancellationToken cancellationToken)
    {
        var (response, error) = await SendAsync($"accounts/{accountId}/check-numbers", new { numbers = phoneNumbers }, cancellationToken);
        if (response is null)
        {
            return error!;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                return await ToErrorAsync(response, cancellationToken);
            }

            var body = await response.Content.ReadFromJsonAsync<CheckNumbersResponse>(cancellationToken);
            if (body?.Results is null)
            {
                return WhatsAppErrors.SendFailed;
            }

            var answer = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var item in body.Results)
            {
                if (!string.IsNullOrEmpty(item.Number))
                {
                    answer[item.Number] = item.OnWhatsApp;
                }
            }

            return Result<IReadOnlyDictionary<string, bool>>.Success(answer);
        }
    }

    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            return false;
        }

        try
        {
            using var response = await http.GetAsync("health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    private async Task<Result> PostAsync(string path, object body, CancellationToken cancellationToken)
    {
        var (response, error) = await SendAsync(path, body, cancellationToken);
        if (response is null)
        {
            return error!;
        }

        using (response)
        {
            return response.IsSuccessStatusCode ? Result.Success() : await ToErrorAsync(response, cancellationToken);
        }
    }

    private async Task<(HttpResponseMessage? Response, Error? Error)> SendAsync(
        string path, object body, CancellationToken cancellationToken)
    {
        if (!options.Value.IsConfigured)
        {
            logger.LogWarning("Gateway:Secret is not set (at least 32 characters); WhatsApp features are off");
            return (null, WhatsAppErrors.GatewayUnavailable);
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add(GatewayOptions.SecretHeader, options.Value.Secret);
        try
        {
            return (await http.SendAsync(request, cancellationToken), null);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            logger.LogWarning("WhatsApp gateway at {BaseUrl} is not reachable: {Error}", http.BaseAddress, ex.Message);
            return (null, WhatsAppErrors.GatewayUnavailable);
        }
    }

    private async Task<Error> ToErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        GatewayErrorResponse? body = null;
        try
        {
            body = await response.Content.ReadFromJsonAsync<GatewayErrorResponse>(cancellationToken);
        }
        catch (System.Text.Json.JsonException)
        {
            // not JSON: handled below
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            logger.LogError("The WhatsApp gateway refused our secret: Gateway:Secret differs between the API and the gateway");
            return WhatsAppErrors.GatewayUnavailable;
        }

        return body?.Code switch
        {
            "already_linked" => WhatsAppErrors.AlreadyLinked,
            "not_connected" => WhatsAppErrors.NotConnected,
            "not_on_whatsapp" => WhatsAppErrors.NotOnWhatsApp,
            "invalid_phone" => WhatsAppErrors.InvalidPhoneNumber,
            _ => LogUnexpected(response.StatusCode, body?.Code),
        };
    }

    private Error LogUnexpected(HttpStatusCode status, string? code)
    {
        logger.LogWarning("WhatsApp gateway answered {Status} {Code}", (int)status, code);
        return WhatsAppErrors.SendFailed;
    }

    private sealed record SendResponse(string? MessageId);

    private sealed record CheckNumbersResponse(IReadOnlyList<CheckNumbersItem>? Results);

    private sealed record CheckNumbersItem(string? Number, bool OnWhatsApp);

    private sealed record GatewayErrorResponse(string? Code, string? Message);
}
