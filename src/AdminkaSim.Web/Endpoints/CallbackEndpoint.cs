using System.Text.Json;
using AdminkaSim.Web.Merchant;
using AdminkaSim.Web.Wallet;

namespace AdminkaSim.Web.Endpoints;

/// <summary>
/// The merchant callback receiver — the role GhostMerchant's
/// <c>/api/v1/merchant-callback</c> plays for adminka's WebhookDispatcher
/// (gotcha 55ba362e). A machine-to-machine JSON POST: authenticated by the v1
/// hash (verified in <see cref="WalletService.ProcessCallbackAsync"/>), so it is
/// <c>AllowAnonymous</c> w.r.t. cookie auth and not a browser form (no antiforgery).
/// <para>
/// The body is read RAW and logged verbatim before deserialization: adminka's
/// webhook_delivery stores only the merchant's <i>response</i> excerpt, so this
/// log line is the only byte-level record of what the wire actually delivered —
/// the evidence the byte-parity plan's §8 E2E matrix compares across phases
/// (Phase-0 baseline anomaly #3). Deserialization mirrors the previous
/// model-binding behaviour: default Web JSON options, 400 on malformed JSON.
/// </para>
/// </summary>
public static partial class CallbackEndpoint
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IEndpointConventionBuilder MapAdminkaCallback(this IEndpointRouteBuilder app) =>
        app.MapPost("/callback", async (
                HttpRequest request,
                WalletService wallet,
                ILoggerFactory loggerFactory,
                CancellationToken ct) =>
            {
                var logger = loggerFactory.CreateLogger("AdminkaSim.Web.Endpoints.CallbackEndpoint");

                string raw;
                using (var reader = new StreamReader(request.Body))
                {
                    raw = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
                }

                LogRawCallback(logger, raw);

                AdminkaCallbackBody? body;
                try
                {
                    body = JsonSerializer.Deserialize<AdminkaCallbackBody>(raw, WebJson);
                }
                catch (JsonException)
                {
                    body = null;
                }

                if (body is null)
                {
                    // Same visible behaviour a model-binding failure produced before.
                    return Results.BadRequest(new { received = false, reason = "malformed body" });
                }

                var outcome = await wallet.ProcessCallbackAsync(body, ct).ConfigureAwait(false);
                return outcome switch
                {
                    // 200 on accepted AND on idempotent re-delivery so adminka stops retrying.
                    CallbackOutcome.Accepted or CallbackOutcome.AlreadyProcessed
                        => Results.Ok(new { received = true }),
                    // 401 on bad hash — wallet untouched (plan §3.1.2a).
                    CallbackOutcome.HashInvalid
                        => Results.Json(new { received = false, reason = "invalid hash" }, statusCode: StatusCodes.Status401Unauthorized),
                    // 404 for an unknown txn — a real signal, not a silent 200.
                    CallbackOutcome.NotFound
                        => Results.NotFound(new { received = false, reason = "unknown transaction" }),
                    _ => Results.Ok(new { received = true }),
                };
            })
            .AllowAnonymous()
            .WithName("AdminkaMerchantCallback");

    [LoggerMessage(Level = LogLevel.Information, Message = "Adminka callback raw body: {RawBody}")]
    private static partial void LogRawCallback(ILogger logger, string rawBody);
}
