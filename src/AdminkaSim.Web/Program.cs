using AdminkaSim.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContextPool<SimDbContext>(o => o.UseNpgsql(connectionString));

// DB-backed ASP.NET Core Identity, cookie login. AddIdentity (not
// AddDefaultIdentity) so there is NO scaffolded self-registration / 2FA UI —
// just the login + logout pages we author. No Duende, no OIDC (plan D3).
builder.Services
    .AddIdentity<SimUser, IdentityRole>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        // Relaxed password policy — these are throwaway demo players.
        options.Password.RequiredLength = 6;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireDigit = false;
        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<SimDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/Account/Login";
    o.LogoutPath = "/Account/Logout";
    o.AccessDeniedPath = "/Account/Login";
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
    o.SlidingExpiration = true;
});

// Everything requires login except the auth pages and the error page.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Apply migrations + seed the 3 demo players (idempotent).
await SeedData.InitializeAsync(app.Services, app.Configuration);

app.Run();

/// <summary>Exposed so the test project can drive the app via WebApplicationFactory.</summary>
public partial class Program;
