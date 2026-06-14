using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Data;

/// <summary>
/// The sim's single database context: ASP.NET Core Identity tables + the
/// wallet ledger. One Postgres database (<c>adminka-sim-data</c> in the
/// cluster). Identity and wallet are distinct logical concerns sharing one
/// database (plan §5 / reviewer note).
/// </summary>
public sealed class SimDbContext(DbContextOptions<SimDbContext> options)
    : IdentityDbContext<SimUser>(options)
{
    public DbSet<Wallet> Wallets => Set<Wallet>();
    public DbSet<WalletLedgerEntry> WalletLedger => Set<WalletLedgerEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Wallet>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.UserId).IsRequired();
            e.HasIndex(w => w.UserId).IsUnique();          // one wallet per user
            e.Property(w => w.Currency).HasMaxLength(3).IsRequired();
            e.Property(w => w.Balance).HasColumnType("numeric(19,4)");
            e.HasMany(w => w.Ledger)
                .WithOne(l => l.Wallet)
                .HasForeignKey(l => l.WalletId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<WalletLedgerEntry>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Amount).HasColumnType("numeric(19,4)");
            e.Property(l => l.Currency).HasMaxLength(3).IsRequired();
            e.Property(l => l.MerchantTxId).IsRequired();
            e.HasIndex(l => l.MerchantTxId).IsUnique();    // idempotency anchor for P1
            e.Property(l => l.Direction).HasConversion<string>().HasMaxLength(16);
            e.Property(l => l.Status).HasConversion<string>().HasMaxLength(16);
        });
    }
}
