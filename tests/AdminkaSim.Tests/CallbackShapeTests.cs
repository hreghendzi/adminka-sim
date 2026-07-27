using System.Text.Json;
using AdminkaSim.Web.Data;
using AdminkaSim.Web.Merchant;
using Microsoft.EntityFrameworkCore;
using CallbackOutcome = AdminkaSim.Web.Wallet.CallbackOutcome;

namespace AdminkaSim.Tests;

/// <summary>
/// Byte-level wire-shape tests for the fastpay-byte-parity plan WS-B.1
/// (<c>docs/fastpay-byte-parity-plan.md</c> §5). Unlike
/// <see cref="WalletCallbackTests"/>, which constructs
/// <see cref="AdminkaCallbackBody"/> objects directly and therefore proves
/// nothing about deserialization, every test here starts from a RAW JSON
/// string and deserializes it exactly the way <c>CallbackEndpoint</c>'s
/// minimal-API model binding does:
/// <c>JsonSerializer.Deserialize&lt;AdminkaCallbackBody&gt;(json, new
/// JsonSerializerOptions(JsonSerializerDefaults.Web))</c> — <c>Program.cs</c>
/// never calls <c>ConfigureHttpJsonOptions</c>, so ASP.NET Core's default Web
/// JSON options are the faithful mirror of what the endpoint actually binds.
/// <para>
/// Fixtures (b)/(c)/(d) below are the GOLDEN strict-FASTPAY shapes from plan
/// §4 A1. If adminka's real wire ever disagrees with them, the FIXTURE is the
/// spec and the wire is the bug (team memory 55ba362e, 50838c70: prior
/// callback body-shape mismatches in this codebase came from paraphrasing the
/// wire instead of transcribing it byte-for-byte).
/// </para>
/// <para>
/// Binding contract: business-logic.md §13 (callbacks — hash verified before
/// acting; <c>status: 0</c> never on the wire; idempotent at the merchant)
/// and §3.2 (partial approval → <c>confirmedAmount</c>).
/// </para>
/// </summary>
public class CallbackShapeTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    private static AdminkaCallbackBody Deserialize(string json)
    {
        var body = JsonSerializer.Deserialize<AdminkaCallbackBody>(json, WebJson);
        Assert.NotNull(body);
        return body!;
    }

    // --- Fixture (a): OLD live shape — 9 keys, camelCase, transaction BEFORE
    // hash, statusText lowercase, scaled decimal amounts, direction +
    // occurredAt present, settlement key is merchantTxId. ---
    private const string OldShapeDepositTemplate = """
        {"transaction":{"id":"3f1c9d6e-8b2a-4d51-9f0c-1a2b3c4d5e6f","merchantTxId":"tx-old-1","status":1,"statusText":"confirmed","direction":0,"amount":250.0000,"confirmedAmount":250.0000,"currency":"TRY","occurredAt":"2026-07-27T10:15:00+00:00"},"hash":"__HASH__"}
        """;

    // --- Fixture (b): NEW strict FASTPAY DEPOSIT shape (plan §4 A1) — hash
    // FIRST (C5), Title-case statusText (A5), integer amount tokens (A7),
    // clientId settlement key, all-lowercase usercode (A2), nested
    // snake_case account block (A3), no merchantTxId/direction/occurredAt. ---
    private const string NewShapeDepositTemplate = """
        {"hash":"__HASH__","transaction":{"id":"b3f21a77-5c4e-4d3b-8a19-77c0de4f1a02","method":"havale","username":"Sim Player","usercode":"SIMU001","amount":250,"confirmedAmount":250,"currency":"TRY","note":"","dateTime":"2026-07-27 10:15:00","status":1,"statusText":"Confirmed","statusDateTime":"2026-07-27 10:16:12","clientId":"tx-new-1","account":{"account_holder":"FASTPAY A.S.","account_bank_id":12,"account_bank_name":"Ziraat Bankasi","account_no":"1234567","account_iban":"TR330006100519786457841326","account_branch":"Kadikoy"}}}
        """;

    // --- Fixture (c): NEW strict WITHDRAW shape — method UPPERCASE, account
    // a FLAT STRING (not an object), name a plain string. ---
    private const string NewShapeWithdrawTemplate = """
        {"hash":"__HASH__","transaction":{"id":"c41d0a55-9e77-4b12-9d31-2f6b7c8d9e10","method":"HAVALE","username":"Sim Player","usercode":"SIMU001","amount":40,"confirmedAmount":40,"currency":"TRY","note":"","dateTime":"2026-07-27 11:00:00","status":1,"statusText":"Confirmed","statusDateTime":"2026-07-27 11:02:00","clientId":"tx-new-w1","account":"TR330006100519786457841326","name":"Sim Player"}}
        """;

    // --- Fixture (d): NEW strict DENIED shape (plan §4 A6) — confirmedAmount
    // null on terminal-unconfirmed. ---
    private const string NewShapeDeniedTemplate = """
        {"hash":"__HASH__","transaction":{"id":"b3f21a77-5c4e-4d3b-8a19-77c0de4f1a02","method":"havale","username":"Sim Player","usercode":"SIMU001","amount":250,"confirmedAmount":null,"currency":"TRY","note":"Alici adi eslesmiyor","dateTime":"2026-07-27 10:15:00","status":2,"statusText":"Denied","statusDateTime":"2026-07-27 10:16:12","clientId":"tx-new-d1","account":{"account_holder":"FASTPAY A.S.","account_bank_id":12,"account_bank_name":"Ziraat Bankasi","account_no":"1234567","account_iban":"TR330006100519786457841326","account_branch":"Kadikoy"}}}
        """;

    private static string WithHash(string template, AdminkaMerchantOptions o) =>
        template.Replace("__HASH__", MerchantHash.Md5Hex(o.Mid, o.CallbackUrl, o.SecretKey));

    [Fact]
    public async Task OldShape_MerchantTxIdKey_SettlesWallet()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-old-1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(OldShapeDepositTemplate, o));
        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(250m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Confirmed, (await db.WalletLedger.FirstAsync()).Status);
    }

    [Fact]
    public async Task NewShape_Deposit_ClientIdKey_SettlesWallet()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(NewShapeDepositTemplate, o));

        // beae1f4d: a snake_case miss binds to CLR defaults with no exception —
        // assert the nested account_* values explicitly, not just the settlement outcome.
        Assert.NotNull(body.Transaction);
        Assert.NotNull(body.Transaction!.Account);
        Assert.Equal("TR330006100519786457841326", body.Transaction!.Account!.AccountIban);
        Assert.Equal(12, body.Transaction!.Account!.AccountBankId);
        Assert.Equal("FASTPAY A.S.", body.Transaction!.Account!.AccountHolder);

        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(250m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Confirmed, (await db.WalletLedger.FirstAsync()).Status);
    }

    [Fact]
    public async Task NewShape_Withdraw_FlatAccountString_DebitsWallet()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 100m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-w1", LedgerDirection.Withdraw, 40m);

        var body = Deserialize(WithHash(NewShapeWithdrawTemplate, o));

        // Dual-shape converter's contract: a flat string `account` deserializes to
        // a null Account without throwing (add2f689).
        Assert.NotNull(body.Transaction);
        Assert.Null(body.Transaction!.Account);

        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(60m, (await db.Wallets.FirstAsync()).Balance);
    }

    [Fact]
    public async Task NewShape_Denied_NullConfirmedAmount_DoesNotCredit()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-d1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(NewShapeDeniedTemplate, o));
        Assert.Null(body.Transaction!.ConfirmedAmount);

        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(0m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Denied, (await db.WalletLedger.FirstAsync()).Status);
    }

    [Fact]
    public async Task NewShape_Redelivery_IsIdempotent()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-1", LedgerDirection.Deposit, 250m);

        var json = WithHash(NewShapeDepositTemplate, o);
        var first = await svc.ProcessCallbackAsync(Deserialize(json));
        var second = await svc.ProcessCallbackAsync(Deserialize(json));

        Assert.Equal(CallbackOutcome.Accepted, first);
        Assert.Equal(CallbackOutcome.AlreadyProcessed, second);
        Assert.Equal(250m, (await db.Wallets.FirstAsync()).Balance); // not double-credited
    }

    [Fact]
    public async Task NewShape_NumericId_Tolerated()
    {
        // Plan §3 register item B2: FASTPAY ships a numeric id where adminka
        // ships a UUID string. TolerantIdConverter (CallbackContract.cs) accepts
        // both tokens — this fixture swaps the quoted UUID for a bare number.
        const string template = """
            {"hash":"__HASH__","transaction":{"id":9876543,"method":"havale","username":"Sim Player","usercode":"SIMU001","amount":250,"confirmedAmount":250,"currency":"TRY","note":"","dateTime":"2026-07-27 10:15:00","status":1,"statusText":"Confirmed","statusDateTime":"2026-07-27 10:16:12","clientId":"tx-new-1","account":{"account_holder":"FASTPAY A.S.","account_bank_id":12,"account_bank_name":"Ziraat Bankasi","account_no":"1234567","account_iban":"TR330006100519786457841326","account_branch":"Kadikoy"}}}
            """;

        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(template, o));
        Assert.Equal("9876543", body.Transaction!.Id);

        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(250m, (await db.Wallets.FirstAsync()).Balance);
    }

    [Fact]
    public async Task OldShape_BadHash_WalletUntouched()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-old-1", LedgerDirection.Deposit, 250m);

        var json = OldShapeDepositTemplate.Replace("__HASH__", "deadbeef");
        var outcome = await svc.ProcessCallbackAsync(Deserialize(json));

        Assert.Equal(CallbackOutcome.HashInvalid, outcome);
        Assert.Equal(0m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Pending, (await db.WalletLedger.FirstAsync()).Status);
    }
}
