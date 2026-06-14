using AdminkaSim.Web.Data;
using AdminkaSim.Web.Merchant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WalletService = AdminkaSim.Web.Wallet.WalletService;
using CallbackOutcome = AdminkaSim.Web.Wallet.CallbackOutcome;

namespace AdminkaSim.Tests;

/// <summary>
/// The P1 contract: adminka-sim's wallet moves ONLY on a hash-verified
/// <c>confirmed</c> callback, and re-delivery is idempotent.
/// </summary>
public class WalletCallbackTests
{
    private static (WalletService Svc, SimDbContext Db, AdminkaMerchantOptions O) NewService()
    {
        var db = new SimDbContext(new DbContextOptionsBuilder<SimDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        var o = new AdminkaMerchantOptions
        {
            Mid = "ADMINKA_SIM_001",
            CallbackUrl = "https://sim.test/callback",
            SecretKey = "s3cr3t",
            Currency = "TRY",
        };
        var merchant = new AdminkaMerchantClient(new HttpClient(), Options.Create(o));
        var svc = new WalletService(db, merchant, Options.Create(o), NullLogger<WalletService>.Instance);
        return (svc, db, o);
    }

    private static Guid SeedWallet(SimDbContext db, decimal balance)
    {
        var w = new Wallet { Id = Guid.NewGuid(), UserId = "u1", Currency = "TRY", Balance = balance, CreatedAt = DateTimeOffset.UtcNow };
        db.Wallets.Add(w);
        db.SaveChanges();
        return w.Id;
    }

    private static void SeedPending(SimDbContext db, Guid walletId, string txid, LedgerDirection dir, decimal amount)
    {
        db.WalletLedger.Add(new WalletLedgerEntry
        {
            Id = Guid.NewGuid(),
            WalletId = walletId,
            Direction = dir,
            Status = LedgerStatus.Pending,
            Amount = amount,
            Currency = "TRY",
            MerchantTxId = txid,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        });
        db.SaveChanges();
    }

    private static AdminkaCallbackBody Callback(
        AdminkaMerchantOptions o, string txid, short status, decimal amount,
        decimal? confirmed = null, short direction = 0, string? hash = null)
        => new(
            new AdminkaCallbackTransaction(Guid.NewGuid(), txid, status, "x", direction, amount, confirmed, "TRY", DateTimeOffset.UtcNow),
            hash ?? MerchantHash.Md5Hex(o.Mid, o.CallbackUrl, o.SecretKey));

    [Fact]
    public async Task ConfirmedDeposit_CreditsWallet()
    {
        var (svc, db, o) = NewService();
        var wid = SeedWallet(db, 0m);
        SeedPending(db, wid, "tx1", LedgerDirection.Deposit, 100m);

        var outcome = await svc.ProcessCallbackAsync(Callback(o, "tx1", 1, 100m));

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(100m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Confirmed, (await db.WalletLedger.FirstAsync()).Status);
    }

    [Fact]
    public async Task Redelivery_IsIdempotent()
    {
        var (svc, db, o) = NewService();
        var wid = SeedWallet(db, 0m);
        SeedPending(db, wid, "tx1", LedgerDirection.Deposit, 100m);

        await svc.ProcessCallbackAsync(Callback(o, "tx1", 1, 100m));
        var second = await svc.ProcessCallbackAsync(Callback(o, "tx1", 1, 100m));

        Assert.Equal(CallbackOutcome.AlreadyProcessed, second);
        Assert.Equal(100m, (await db.Wallets.FirstAsync()).Balance); // not double-credited
    }

    [Fact]
    public async Task BadHash_RejectedAndWalletUntouched()
    {
        var (svc, db, o) = NewService();
        var wid = SeedWallet(db, 0m);
        SeedPending(db, wid, "tx1", LedgerDirection.Deposit, 100m);

        var outcome = await svc.ProcessCallbackAsync(Callback(o, "tx1", 1, 100m, hash: "deadbeef"));

        Assert.Equal(CallbackOutcome.HashInvalid, outcome);
        Assert.Equal(0m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Pending, (await db.WalletLedger.FirstAsync()).Status);
    }

    [Fact]
    public async Task UnknownTransaction_NotFound()
    {
        var (svc, db, o) = NewService();
        SeedWallet(db, 0m);

        var outcome = await svc.ProcessCallbackAsync(Callback(o, "nope", 1, 100m));

        Assert.Equal(CallbackOutcome.NotFound, outcome);
    }

    [Fact]
    public async Task Denied_NoBalanceMove()
    {
        var (svc, db, o) = NewService();
        var wid = SeedWallet(db, 0m);
        SeedPending(db, wid, "tx1", LedgerDirection.Deposit, 100m);

        var outcome = await svc.ProcessCallbackAsync(Callback(o, "tx1", 2, 100m));

        Assert.Equal(CallbackOutcome.Accepted, outcome);
        Assert.Equal(0m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(LedgerStatus.Denied, (await db.WalletLedger.FirstAsync()).Status);
    }

    [Fact]
    public async Task PartialApprovalDeposit_UsesConfirmedAmount()
    {
        var (svc, db, o) = NewService();
        var wid = SeedWallet(db, 0m);
        SeedPending(db, wid, "tx1", LedgerDirection.Deposit, 100m);

        await svc.ProcessCallbackAsync(Callback(o, "tx1", 1, 100m, confirmed: 60m));

        Assert.Equal(60m, (await db.Wallets.FirstAsync()).Balance);
        Assert.Equal(60m, (await db.WalletLedger.FirstAsync()).Amount);
    }

    [Fact]
    public async Task ConfirmedWithdraw_DebitsWallet()
    {
        var (svc, db, o) = NewService();
        var wid = SeedWallet(db, 100m);
        SeedPending(db, wid, "tx1", LedgerDirection.Withdraw, 40m);

        await svc.ProcessCallbackAsync(Callback(o, "tx1", 1, 40m, direction: 1));

        Assert.Equal(60m, (await db.Wallets.FirstAsync()).Balance);
    }
}
