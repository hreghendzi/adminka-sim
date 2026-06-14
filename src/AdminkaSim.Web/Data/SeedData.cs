using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AdminkaSim.Web.Data;

/// <summary>
/// Applies migrations and seeds the 3 demo players + their (empty) wallets.
/// Idempotent — safe to run on every startup. The password is config-driven
/// (<c>Seed:DemoUserPassword</c>) with a baked default so a fresh clone runs
/// with no setup.
/// </summary>
public static class SeedData
{
    public static readonly string[] DemoEmails =
    [
        "player1@demo.local",
        "player2@demo.local",
        "player3@demo.local",
    ];

    public static async Task InitializeAsync(IServiceProvider services, IConfiguration config)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var db = sp.GetRequiredService<SimDbContext>();
        await db.Database.MigrateAsync();

        var users = sp.GetRequiredService<UserManager<SimUser>>();
        var password = config["Seed:DemoUserPassword"] ?? "Demo123";

        for (var i = 0; i < DemoEmails.Length; i++)
        {
            var email = DemoEmails[i];
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null)
            {
                continue;
            }

            var user = new SimUser
            {
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                DisplayName = $"Player {i + 1}",
            };

            var result = await users.CreateAsync(user, password);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Failed to seed demo user {email}: " +
                    string.Join(", ", result.Errors.Select(e => $"{e.Code}:{e.Description}")));
            }

            db.Wallets.Add(new Wallet
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                Currency = "TRY",
                Balance = 0m,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
    }
}
