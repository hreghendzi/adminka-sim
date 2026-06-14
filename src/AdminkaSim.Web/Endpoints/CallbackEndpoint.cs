using AdminkaSim.Web.Merchant;
using AdminkaSim.Web.Wallet;

namespace AdminkaSim.Web.Endpoints;

/// <summary>
/// The merchant callback receiver — the role GhostMerchant's
/// <c>/api/v1/merchant-callback</c> plays for adminka's WebhookDispatcher
/// (gotcha 55ba362e). A machine-to-machine JSON POST: authenticated by the v1
/// hash (verified in <see cref="WalletService.ProcessCallbackAsync"/>), so it is
/// <c>AllowAnonymous</c> w.r.t. cookie auth and not a browser form (no antiforgery).
/// </summary>
public static class CallbackEndpoint
{
    public static IEndpointConventionBuilder MapAdminkaCallback(this IEndpointRouteBuilder app) =>
        app.MapPost("/callback", async (
                AdminkaCallbackBody body,
                WalletService wallet,
                CancellationToken ct) =>
            {
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
}
