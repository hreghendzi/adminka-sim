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
    // a FLAT STRING (not an object), name a plain string. Keys are in §13
    // withdraw order: id, method, username, usercode, name, account, amount,
    // confirmedAmount, currency, note, dateTime, status, statusText,
    // statusDateTime, clientId (clientId LAST). ---
    private const string NewShapeWithdrawTemplate = """
        {"hash":"__HASH__","transaction":{"id":"c41d0a55-9e77-4b12-9d31-2f6b7c8d9e10","method":"HAVALE","username":"Sim Player","usercode":"SIMU001","name":"Sim Player","account":"TR330006100519786457841326","amount":40,"confirmedAmount":40,"currency":"TRY","note":"","dateTime":"2026-07-27 11:00:00","status":1,"statusText":"Confirmed","statusDateTime":"2026-07-27 11:02:00","clientId":"tx-new-w1"}}
        """;

    // --- Fixture (d): NEW strict DENIED shape (plan §4 A6) — confirmedAmount
    // null on terminal-unconfirmed. ---
    private const string NewShapeDeniedTemplate = """
        {"hash":"__HASH__","transaction":{"id":"b3f21a77-5c4e-4d3b-8a19-77c0de4f1a02","method":"havale","username":"Sim Player","usercode":"SIMU001","amount":250,"confirmedAmount":null,"currency":"TRY","note":"Alici adi eslesmiyor","dateTime":"2026-07-27 10:15:00","status":2,"statusText":"Denied","statusDateTime":"2026-07-27 10:16:12","clientId":"tx-new-d1","account":{"account_holder":"FASTPAY A.S.","account_bank_id":12,"account_bank_name":"Ziraat Bankasi","account_no":"1234567","account_iban":"TR330006100519786457841326","account_branch":"Kadikoy"}}}
        """;

    // --- Fixture (e): TRANSITION-WINDOW body — the strict deposit shape PLUS the
    // now-unmapped legacy trio (merchantTxId/direction/occurredAt) riding alongside
    // clientId. merchantTxId is DELIBERATELY a different value ("tx-legacy-ignored")
    // than clientId ("tx-new-1") so a passing settlement proves the clientId leg
    // did the work and no merchantTxId fallback survived (team memory 068f6f15). ---
    private const string TransitionWindowDepositTemplate = """
        {"hash":"__HASH__","transaction":{"id":"b3f21a77-5c4e-4d3b-8a19-77c0de4f1a02","merchantTxId":"tx-legacy-ignored","direction":0,"occurredAt":"2026-07-27T10:15:00+00:00","method":"havale","username":"Sim Player","usercode":"SIMU001","amount":250,"confirmedAmount":250,"currency":"TRY","note":"","dateTime":"2026-07-27 10:15:00","status":1,"statusText":"Confirmed","statusDateTime":"2026-07-27 10:16:12","clientId":"tx-new-1","account":{"account_holder":"FASTPAY A.S.","account_bank_id":12,"account_bank_name":"Ziraat Bankasi","account_no":"1234567","account_iban":"TR330006100519786457841326","account_branch":"Kadikoy"}}}
        """;

    private static string WithHash(string template, AdminkaMerchantOptions o) =>
        template.Replace("__HASH__", MerchantHash.Md5Hex(o.Mid, o.CallbackUrl, o.SecretKey));

    // --- THE STRICTNESS-CANARY PROPERTY (business-logic.md §13 gate G2; byte-parity
    // plan WS-B.2 / board TASK-278). Before Phase 5 the sim tolerated the legacy
    // body and settled via merchantTxId; after the flip that tolerance is GONE, so
    // a legacy-shaped callback — even with a perfectly valid hash — no longer
    // carries a clientId and can no longer resolve to a ledger entry. This test is
    // the deliberate INVERSION of the old assertion: it proves the sim REFUSES to
    // settle a legacy-shaped callback. That refusal is the point, not a defect —
    // CallbackEndpoint turns NotFound into HTTP 404, which drives adminka's §13
    // retry ladder into the DLQ, so any regression of adminka's real wire back
    // toward the legacy shape fails LOUDLY and IMMEDIATELY instead of silently
    // continuing to settle. If this test ever goes back to asserting Accepted,
    // the canary property has been lost. ---
    [Fact]
    public async Task OldShape_MerchantTxIdKey_NoLongerSettles_NotFound()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-old-1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(OldShapeDepositTemplate, o));
        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.NotFound, outcome);
        Assert.Equal(0m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Pending, (await db.WalletLedger.FirstAsync()).Status);
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

    // --- TRANSITION-WINDOW / UNKNOWN-KEY TOLERANCE (business-logic.md §13 gate G2;
    // byte-parity plan WS-B.2 / board TASK-278). During the deploy window an
    // in-flight adminka retry could still carry the legacy trio alongside the new
    // strict fields. AdminkaCallbackTransaction no longer declares MerchantTxId /
    // Direction / OccurredAt, so those become plain unmapped JSON keys — and
    // System.Text.Json's default JsonUnmappedMemberHandling.Skip means binding
    // must NOT throw on them. This test proves that concretely rather than
    // assuming it: deserialize must succeed, and settlement must go through the
    // clientId leg alone.
    // <para>
    // What is explicitly NOT preserved: key-fallback tolerance for an
    // adminka-side rollback (settling off merchantTxId when clientId is absent)
    // — plan §6 accepts that the rollback window closed once Phase 4 went green.
    // What IS preserved is only that unknown keys are ignored rather than fatal.
    // merchantTxId is deliberately a NON-matching value here so a passing
    // settlement proves the clientId leg did the work, not a merchantTxId
    // fallback that no longer exists (team memory 068f6f15).
    // </para>
    [Fact]
    public async Task TransitionWindow_UnknownLegacyKeys_DoNotThrow_SettlesViaClientId()
    {
        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(TransitionWindowDepositTemplate, o));

        // Deserialization must not throw on the unmapped legacy trio, and the
        // strict fields must still bind correctly alongside them.
        Assert.NotNull(body.Transaction);
        Assert.Equal("tx-new-1", body.Transaction!.ClientId);
        Assert.NotNull(body.Transaction!.Account);
        Assert.Equal("TR330006100519786457841326", body.Transaction!.Account!.AccountIban);
        Assert.Equal(12, body.Transaction!.Account!.AccountBankId);
        Assert.Equal("FASTPAY A.S.", body.Transaction!.Account!.AccountHolder);

        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(250m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Confirmed, (await db.WalletLedger.FirstAsync()).Status);
    }

    // Cheap variant proving the tolerance is GENERIC (any unknown key), not
    // specific to the legacy trio: a single arbitrary nonsense key alongside an
    // otherwise-strict body must not throw either.
    [Fact]
    public async Task TransitionWindow_ArbitraryUnknownKey_DoesNotThrow_SettlesViaClientId()
    {
        const string template = """
            {"hash":"__HASH__","transaction":{"id":"b3f21a77-5c4e-4d3b-8a19-77c0de4f1a02","somethingUnexpected":"nonsense","method":"havale","username":"Sim Player","usercode":"SIMU001","amount":250,"confirmedAmount":250,"currency":"TRY","note":"","dateTime":"2026-07-27 10:15:00","status":1,"statusText":"Confirmed","statusDateTime":"2026-07-27 10:16:12","clientId":"tx-new-1","account":{"account_holder":"FASTPAY A.S.","account_bank_id":12,"account_bank_name":"Ziraat Bankasi","account_no":"1234567","account_iban":"TR330006100519786457841326","account_branch":"Kadikoy"}}}
            """;

        var (svc, db, o) = WalletCallbackTests.NewService();
        var wid = WalletCallbackTests.SeedWallet(db, 0m);
        WalletCallbackTests.SeedPending(db, wid, "tx-new-1", LedgerDirection.Deposit, 250m);

        var body = Deserialize(WithHash(template, o));

        var outcome = await svc.ProcessCallbackAsync(body);

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(250m, (await db.Wallets.FirstAsync()).Balance);
    }

    // Still green after the flip, and deliberately NOT inverted like the test
    // above: WalletService verifies the hash BEFORE any settlement-key lookup
    // (§13 "merchant verifies hash before acting"), so a bad hash short-circuits
    // to HashInvalid regardless of body shape — the legacy-vs-strict distinction
    // never gets a chance to matter here.
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
