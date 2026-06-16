using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace AdminkaSim.Web.Merchant;

/// <summary>
/// Result of a <c>start</c> call. adminka's envelope is always HTTP 200 with a
/// body-level <c>success</c> flag (memory dee137f5) — callers check
/// <see cref="Success"/>, never the HTTP status.
/// </summary>
public sealed record MerchantStartResult(
    bool Success,
    string? StatusCode,
    string? Message,
    string? PublicTxId,
    IReadOnlyDictionary<string, string?> Account,
    string RawJson,
    string? PaymentUrl = null);

/// <summary>Result of a read/command action (<c>status</c>, <c>cancel</c>).</summary>
public sealed record MerchantActionResult(bool Success, string? StatusCode, string? Message, string RawJson);

/// <summary>A deposit bank from <c>action=banks</c>. <c>Id</c> is passed as <c>bankId</c> on a transfer start (IDs are sparse).</summary>
public sealed record MerchantBank(int Id, string Name, bool HasActiveAccount, decimal? DepositMin, decimal? DepositMax);

/// <summary>
/// Real HTTP client against adminka's MerchantApi. This is the port of
/// <c>CasinoSimService</c>'s hash + action logic into a network client —
/// <b>no in-process call, no DB read</b> (plan D2/§4). The wire contract is
/// <c>docs/merchant-api-v1-reference.md</c>; any divergence is a bug here.
/// </summary>
public sealed class AdminkaMerchantClient(HttpClient http, IOptions<AdminkaMerchantOptions> options)
{
    private readonly AdminkaMerchantOptions _o = options.Value;
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    /// <summary>POST-equivalent <c>/deposit/?action=start</c>. Returns the pool-account block to "pay".</summary>
    public Task<MerchantStartResult> StartDepositAsync(
        string merchantTxId, decimal amount, string method, string userCode, string name,
        int? bankId = null, CancellationToken ct = default)
    {
        var amountWire = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var qs = new Dictionary<string, string?>
        {
            ["action"] = "start",
            ["mid"] = _o.Mid,
            ["transactionId"] = merchantTxId,
            ["usercode"] = userCode,
            ["username"] = userCode,
            ["name"] = name,
            ["callbackUrl"] = _o.CallbackUrl,
            ["amount"] = amountWire,
            ["currency"] = _o.Currency,
            ["method"] = method,
            // Hash: md5(MID + AMOUNT + METHOD + SECRETKEY) — the §3 start irregularity.
            ["hash"] = MerchantHash.Md5Hex(_o.Mid, amountWire, method, _o.SecretKey),
        };
        if (bankId is int b)
        {
            qs["bankId"] = b.ToString(CultureInfo.InvariantCulture);
        }

        return StartAsync("/deposit/", qs, ct);
    }

    /// <summary>POST-equivalent <c>/withdraw/?action=start</c>. <c>callbackUrl</c> MUST be non-empty (gotcha 5cb3187e).</summary>
    public Task<MerchantStartResult> StartWithdrawAsync(
        string merchantTxId, decimal amount, string method, string userCode, string name,
        string account, string? iban = null, string? bankName = null, string? bankBranch = null,
        CancellationToken ct = default)
    {
        var amountWire = amount.ToString("0.##", CultureInfo.InvariantCulture);
        var qs = new Dictionary<string, string?>
        {
            ["action"] = "start",
            ["mid"] = _o.Mid,
            ["transactionId"] = merchantTxId,
            ["usercode"] = userCode,
            ["username"] = userCode,
            ["name"] = name,
            ["account"] = account,
            ["amount"] = amountWire,
            ["currency"] = _o.Currency,
            ["method"] = method,
            ["callbackUrl"] = _o.CallbackUrl,
            ["hash"] = MerchantHash.Md5Hex(_o.Mid, amountWire, method, _o.SecretKey),
        };
        if (string.Equals(method, "transfer", StringComparison.OrdinalIgnoreCase))
        {
            qs["iban"] = iban ?? "";
            qs["bankName"] = bankName ?? "";
            qs["bankBranch"] = bankBranch ?? "";
        }

        return StartAsync("/withdraw/", qs, ct);
    }

    /// <summary><c>action=banks</c> (deposit). Hash: md5(MID + "banks" + SECRETKEY). Returns the agency's banks; filter on <see cref="MerchantBank.HasActiveAccount"/>.</summary>
    public async Task<(bool Success, IReadOnlyList<MerchantBank> Banks, string? Message)> GetDepositBanksAsync(CancellationToken ct = default)
    {
        var qs = new Dictionary<string, string?>
        {
            ["action"] = "banks",
            ["mid"] = _o.Mid,
            ["hash"] = MerchantHash.Md5Hex(_o.Mid, "banks", _o.SecretKey),
        };
        var (root, _) = await GetEnvelopeAsync("/deposit/", qs, ct).ConfigureAwait(false);
        var (success, _, message) = ReadEnvelopeHead(root);

        var banks = new List<MerchantBank>();
        if (success && root is { ValueKind: JsonValueKind.Object } r
            && r.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("banks", out var arr)
            && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var b in arr.EnumerateArray())
            {
                if (b.ValueKind != JsonValueKind.Object || !b.TryGetProperty("id", out var idEl)
                    || !idEl.TryGetInt32(out var id))
                {
                    continue;
                }

                banks.Add(new MerchantBank(
                    id,
                    b.TryGetProperty("name", out var n) ? n.GetString() ?? $"Bank {id}" : $"Bank {id}",
                    b.TryGetProperty("hasActiveAccount", out var ha) && ha.ValueKind == JsonValueKind.True,
                    b.TryGetProperty("depositMin", out var mn) && mn.TryGetDecimal(out var mnv) ? mnv : null,
                    b.TryGetProperty("depositMax", out var mx) && mx.TryGetDecimal(out var mxv) ? mxv : null));
            }
        }

        return (success, banks, message);
    }

    /// <summary><c>status</c> read. Hash: md5(MID + "status" + TXID + SECRETKEY); TXID is adminka's public id (gotcha 068f6f15).</summary>
    public Task<MerchantActionResult> GetStatusAsync(string publicTxId, bool withdraw, CancellationToken ct = default)
        => ActionAsync(withdraw ? "/withdraw/" : "/deposit/", "status", publicTxId, ct);

    /// <summary><c>cancel</c> command. Hash: md5(MID + "cancel" + TXID + SECRETKEY).</summary>
    public Task<MerchantActionResult> CancelAsync(string publicTxId, bool withdraw, CancellationToken ct = default)
        => ActionAsync(withdraw ? "/withdraw/" : "/deposit/", "cancel", publicTxId, ct);

    private async Task<MerchantStartResult> StartAsync(string path, Dictionary<string, string?> qs, CancellationToken ct)
    {
        var (root, raw) = await GetEnvelopeAsync(path, qs, ct).ConfigureAwait(false);
        var (success, statusCode, message) = ReadEnvelopeHead(root);

        string? publicTxId = null;
        string? paymentUrl = null;
        var account = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (root is { ValueKind: JsonValueKind.Object } r
            && r.TryGetProperty("data", out var data)
            && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("transaction", out var txn)
            && txn.ValueKind == JsonValueKind.Object)
        {
            if (txn.TryGetProperty("id", out var idEl))
            {
                publicTxId = idEl.ToString();
            }

            // §3.4 hosted mode: when adminka has the hosted flag on, the deposit-start
            // response carries an absolute paymentUrl (AdminkaPay /account?ref=). Absent
            // otherwise (inline model). When present, the sim redirects the player to it.
            if (txn.TryGetProperty("paymentUrl", out var pu) && pu.ValueKind == JsonValueKind.String)
            {
                paymentUrl = pu.GetString();
            }

            // Deposit: account is a nested pool-account OBJECT; withdraw: a STRING (memory add2f689).
            if (txn.TryGetProperty("account", out var acc))
            {
                if (acc.ValueKind == JsonValueKind.Object)
                {
                    foreach (var p in acc.EnumerateObject())
                    {
                        account[p.Name] = p.Value.ToString();
                    }
                }
                else if (acc.ValueKind == JsonValueKind.String)
                {
                    account["account"] = acc.GetString();
                }
            }
        }

        return new MerchantStartResult(success, statusCode, message, publicTxId, account, raw, paymentUrl);
    }

    private async Task<MerchantActionResult> ActionAsync(string path, string action, string publicTxId, CancellationToken ct)
    {
        var qs = new Dictionary<string, string?>
        {
            ["action"] = action,
            ["mid"] = _o.Mid,
            ["txId"] = publicTxId,
            ["hash"] = MerchantHash.Md5Hex(_o.Mid, action, publicTxId, _o.SecretKey),
        };
        var (root, raw) = await GetEnvelopeAsync(path, qs, ct).ConfigureAwait(false);
        var (success, statusCode, message) = ReadEnvelopeHead(root);
        return new MerchantActionResult(success, statusCode, message, raw);
    }

    private async Task<(JsonElement? Root, string Raw)> GetEnvelopeAsync(
        string path, Dictionary<string, string?> qs, CancellationToken ct)
    {
        var url = QueryHelpers.AddQueryString(path, qs);
        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
        var raw = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        try
        {
            using var doc = JsonDocument.Parse(raw);
            return (doc.RootElement.Clone(), raw);
        }
        catch (JsonException)
        {
            return (null, raw);
        }
    }

    private static (bool Success, string? StatusCode, string? Message) ReadEnvelopeHead(JsonElement? root)
    {
        if (root is not { ValueKind: JsonValueKind.Object } r)
        {
            return (false, null, "Non-JSON response from MerchantApi.");
        }

        var success = r.TryGetProperty("success", out var s)
            && s.ValueKind is JsonValueKind.True or JsonValueKind.False && s.GetBoolean();
        var statusCode = r.TryGetProperty("statusCode", out var sc) ? sc.ToString() : null;
        var message = r.TryGetProperty("message", out var m) ? m.GetString() : null;
        return (success, statusCode, message);
    }
}
