using System.Text.Json.Serialization;

namespace AdminkaSim.Web.Merchant;

/// <summary>
/// Inbound FASTPAY v1 callback body adminka's WebhookDispatcher POSTs to the
/// sim's <c>/callback</c>. Mirrors adminka's <c>CallbackBody</c> /
/// <c>CallbackTransaction</c>. The dispatcher serializes the INNER body in
/// <b>camelCase</b> (gotcha 50838c70) — deserialize with web defaults.
/// </summary>
public sealed record AdminkaCallbackBody(
    [property: JsonPropertyName("transaction")] AdminkaCallbackTransaction Transaction,
    [property: JsonPropertyName("hash")] string Hash);

/// <summary>The merchant-facing transaction summary embedded in a callback.</summary>
public sealed record AdminkaCallbackTransaction(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("merchantTxId")] string MerchantTxId,
    [property: JsonPropertyName("status")] short Status,
    [property: JsonPropertyName("statusText")] string? StatusText,
    [property: JsonPropertyName("direction")] short Direction,
    [property: JsonPropertyName("amount")] decimal Amount,
    [property: JsonPropertyName("confirmedAmount")] decimal? ConfirmedAmount,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("occurredAt")] DateTimeOffset OccurredAt)
{
    /// <summary>adminka status codes: 1=confirmed, 2=denied, 3=cancelled (0=pending never on wire).</summary>
    public const short StatusConfirmed = 1;
    public const short StatusDenied = 2;
    public const short StatusCancelled = 3;
}
