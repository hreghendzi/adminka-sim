using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AdminkaSim.Web.Merchant;

/// <summary>
/// Inbound FASTPAY v1 callback body adminka's WebhookDispatcher POSTs to the
/// sim's <c>/callback</c>. Mirrors adminka's <c>CallbackBody</c> /
/// <c>CallbackTransaction</c>. The dispatcher serializes the INNER body in
/// <b>camelCase</b> (gotcha 50838c70) — deserialize with web defaults.
/// <para>
/// <see cref="Transaction"/> is nullable because the sim cannot assume a
/// well-formed body from the wire: <c>{"hash":"…"}</c> alone binds fine and must
/// not NRE. Key order is irrelevant to binding, so the byte-parity plan's C5
/// (<c>hash</c> before <c>transaction</c>) needs no change here.
/// </para>
/// </summary>
public sealed record AdminkaCallbackBody(
    [property: JsonPropertyName("transaction")] AdminkaCallbackTransaction? Transaction,
    [property: JsonPropertyName("hash")] string Hash);

/// <summary>
/// The merchant-facing transaction summary embedded in a callback, bound to the
/// <b>STRICT FASTPAY v1.1 body ONLY</b> (business-logic.md §13, ratified gate
/// G2, owner 2026-07-27). The Phase-1 tolerance that also bound adminka's older
/// body is <b>gone</b> (byte-parity plan Phase 5 / WS-B.2, board TASK-278): the
/// legacy additive fields <c>merchantTxId</c>, <c>direction</c> and
/// <c>occurredAt</c> — dropped from the wire by that same §13 MUST — are
/// deliberately ABSENT here.
/// <para>
/// That absence is the POINT, not cleanup. The sim is a living
/// FASTPAY-strictness canary: if adminka's wire ever drifts back toward the
/// legacy shape, the callback loses its <c>clientId</c> and sim settlement
/// fails loudly and immediately (HTTP 404 → §13 retry ladder → DLQ) instead of
/// silently continuing to settle. Re-adding tolerance for the legacy fields
/// would let exactly that regression ship unnoticed.
/// </para>
/// <para>
/// The dual-shape <c>account</c> reader and the string-or-number <c>id</c>
/// reader are <b>ratified permanent properties of the strict wire</b> (§13
/// deposit/withdraw key sets; byte-parity register item B2) — not leftovers of
/// the removed tolerance. Removing either breaks real traffic.
/// </para>
/// <para>
/// Declared with init-only properties rather than positionally: the strict
/// shape carries ~15 mostly-optional fields, so a positional record would make
/// every future wire field a breaking positional change and make construction in
/// tests unreadable.
/// </para>
/// <para>
/// Fields other than <c>clientId</c>/<c>status</c>/<c>amount</c>/
/// <c>confirmedAmount</c>/<c>id</c> are <b>bind-but-ignore</b> —
/// nothing in <c>WalletService</c> reads them. They are deliberate, not dead
/// weight: they document the received shape and keep the DTO a faithful mirror
/// of the wire for future assertions/logging.
/// </para>
/// </summary>
public sealed record AdminkaCallbackTransaction
{
    /// <summary>
    /// adminka's public transaction id. Bound as <c>string?</c>, never
    /// <c>Guid</c>: the byte-parity register (§3 B2) records adminka shipping a
    /// UUID string where FASTPAY ships a NUMERIC id, and a numeric token throws
    /// into <c>Guid</c>. <see cref="TolerantIdConverter"/> accepts both tokens.
    /// </summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(TolerantIdConverter))]
    public string? Id { get; init; }

    /// <summary>
    /// <b>THE</b> settlement key (§13: <c>clientId</c> carries the merchant's own
    /// <c>transactionId</c> echoed back). <c>WalletService.ProcessCallbackAsync</c>
    /// keys the ledger lookup on this and nothing else; a body without it is a
    /// wire regression and is refused rather than settled.
    /// </summary>
    [JsonPropertyName("clientId")]
    public string? ClientId { get; init; }

    /// <summary>
    /// adminka status code. Deliberately NOT widened to a string/nullable:
    /// adminka emits <c>status</c> as a JSON number in both the current and the
    /// target shape (plan §4 A1 changes only <c>statusText</c> casing), so
    /// widening would be speculative.
    /// </summary>
    [JsonPropertyName("status")]
    public short Status { get; init; }

    /// <summary>Human status label — lowercase today, Title case after plan §4 A5. Not read by any logic.</summary>
    [JsonPropertyName("statusText")]
    public string? StatusText { get; init; }

    /// <summary>Requested amount. <c>decimal</c> binds both <c>250</c> and <c>250.0000</c> (plan §4 A7) natively.</summary>
    [JsonPropertyName("amount")]
    public decimal Amount { get; init; }

    /// <summary>Operator-approved amount (§3.2 partial approval). <c>null</c> on terminal-unconfirmed after plan §4 A6.</summary>
    [JsonPropertyName("confirmedAmount")]
    public decimal? ConfirmedAmount { get; init; }

    /// <summary>Wire currency literal — <c>TRY</c>/<c>TL</c> are §14 synonyms; echoed, never branched on.</summary>
    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>
    /// Creation timestamp. Bound as <b>text</b>, not <c>DateTimeOffset</c>: the
    /// strict wire formats it <c>yyyy-MM-dd HH:mm:ss</c> UTC (plan §4 A1), which
    /// is not ISO-8601 and throws <c>JsonException</c> into a date type.
    /// </summary>
    [JsonPropertyName("dateTime")]
    public string? DateTimeText { get; init; }

    /// <summary>Status-transition timestamp; same non-ISO <c>yyyy-MM-dd HH:mm:ss</c> format as <see cref="DateTimeText"/>.</summary>
    [JsonPropertyName("statusDateTime")]
    public string? StatusDateTimeText { get; init; }

    /// <summary>End-user code. The strict wire spells it all-lowercase (plan §4 A2).</summary>
    [JsonPropertyName("usercode")]
    public string? Usercode { get; init; }

    /// <summary>End-user display name.</summary>
    [JsonPropertyName("username")]
    public string? Username { get; init; }

    /// <summary>Payment method — lowercase (deposit) / UPPERCASE (withdraw) on the strict wire.</summary>
    [JsonPropertyName("method")]
    public string? Method { get; init; }

    /// <summary>Free text; carries the §13 human-readable reject reason on a Denied callback.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; init; }

    /// <summary>Account holder name on the withdraw shape (flat, alongside a flat <c>account</c> string).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// The deposit-only nested account block. <b>Dual-shape</b>: withdraw ships
    /// <c>account</c> as a flat IBAN/account STRING, deposit as an object — so
    /// the converter (team memory <c>add2f689</c>) is attached to this PROPERTY
    /// and yields <c>null</c> for the flat form.
    /// </summary>
    [JsonPropertyName("account")]
    [JsonConverter(typeof(TolerantAccountConverter))]
    public AdminkaCallbackAccount? Account { get; init; }

    /// <summary>adminka status codes: 1=confirmed, 2=denied, 3=cancelled (0=pending never on wire).</summary>
    public const short StatusConfirmed = 1;
    public const short StatusDenied = 2;
    public const short StatusCancelled = 3;
}

/// <summary>
/// The deposit-shape nested <c>account</c> block of the strict FASTPAY callback
/// (plan §4 A3). Every member carries an EXPLICIT <see cref="JsonPropertyNameAttribute"/>:
/// <c>JsonSerializerDefaults.Web</c> bridges camelCase↔PascalCase but does NOT
/// bridge snake_case (team memory <c>beae1f4d</c>) — without these shims every
/// field silently binds to its CLR default with no exception. Kept minimal and
/// exact, no speculative aliases (team memory <c>38c76916</c>).
/// Bind-but-ignore: no wallet logic reads it.
/// </summary>
public sealed record AdminkaCallbackAccount
{
    /// <summary>Pay-to account holder name.</summary>
    [JsonPropertyName("account_holder")]
    public string? AccountHolder { get; init; }

    /// <summary>Bank id. Tolerates a quoted or unquoted number.</summary>
    [JsonPropertyName("account_bank_id")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int? AccountBankId { get; init; }

    /// <summary>Bank display name.</summary>
    [JsonPropertyName("account_bank_name")]
    public string? AccountBankName { get; init; }

    /// <summary>Account number.</summary>
    [JsonPropertyName("account_no")]
    public string? AccountNo { get; init; }

    /// <summary>IBAN.</summary>
    [JsonPropertyName("account_iban")]
    public string? AccountIban { get; init; }

    /// <summary>Branch name/code.</summary>
    [JsonPropertyName("account_branch")]
    public string? AccountBranch { get; init; }
}

/// <summary>
/// Dual-shape <c>account</c> reader (team memory <c>add2f689</c>): returns
/// <c>null</c> for the withdraw shape's flat string and deserializes the deposit
/// shape's object. MUST stay attached to the PROPERTY — attaching it to
/// <see cref="AdminkaCallbackAccount"/> itself would make the inner
/// <c>Deserialize</c> call recurse into this converter forever.
/// </summary>
internal sealed class TolerantAccountConverter : JsonConverter<AdminkaCallbackAccount?>
{
    public override AdminkaCallbackAccount? Read(
        ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            reader.Skip();
            return null;
        }

        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        return JsonSerializer.Deserialize<AdminkaCallbackAccount>(ref reader, options);
    }

    // The sim only RECEIVES callbacks; it never serializes this type.
    public override void Write(Utf8JsonWriter writer, AdminkaCallbackAccount? value, JsonSerializerOptions options)
        => throw new NotSupportedException("adminka-sim never serializes a callback account block.");
}

/// <summary>
/// Reads <c>transaction.id</c> from either token type: adminka ships a UUID
/// string, FASTPAY a bare number (plan §3 B2, the one permanent byte-level type
/// difference). Numbers are normalised to their invariant decimal text.
/// </summary>
internal sealed class TolerantIdConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.GetInt64().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.Null:
                return null;
            default:
                // Consume whatever unexpected shape arrived so the reader stays in sync.
                reader.Skip();
                return null;
        }
    }

    // The sim only RECEIVES callbacks; writing is never exercised, but a plain
    // string write keeps the converter total rather than throwing.
    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}
