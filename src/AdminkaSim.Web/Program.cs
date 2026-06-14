using AdminkaSim.Web.Data;
using AdminkaSim.Web.Endpoints;
using AdminkaSim.Web.Merchant;
using AdminkaSim.Web.Wallet;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Behind HAProxy + Cloudflare (TLS terminated at the edge): honour
// X-Forwarded-For/Proto so HTTPS detection, redirects, HSTS, and secure
// cookies work. The proxy is the cluster edge, not a known static IP, so the
// known-proxy/network lists are cleared (the NodePort is not internet-facing).
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

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

// adminka merchant integration (plan §3.1). Bound but not validate-on-start, so
// the app still boots locally before adminka is configured; merchant calls just
// fail until BaseUrl/Mid/SecretKey are set.
builder.Services.AddOptions<AdminkaMerchantOptions>()
    .Bind(builder.Configuration.GetSection(AdminkaMerchantOptions.SectionName));

builder.Services.AddHttpClient<AdminkaMerchantClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var o = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<AdminkaMerchantOptions>>().Value;
        if (!string.IsNullOrWhiteSpace(o.BaseUrl))
        {
            client.BaseAddress = new Uri(o.BaseUrl);
        }
        client.Timeout = TimeSpan.FromSeconds(15);
    });

builder.Services.AddScoped<WalletService>();

// Everything requires login except the auth pages and the error page.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Account/Login");
    options.Conventions.AllowAnonymousToPage("/Account/Logout");
    options.Conventions.AllowAnonymousToPage("/Error");
});

var app = builder.Build();

// Must run before HTTPS redirect / auth so the scheme is corrected first.
app.UseForwardedHeaders();

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

// adminka -> sim webhook callback receiver (anonymous; authenticated by v1 hash).
app.MapAdminkaCallback();

// Apply migrations + seed the 3 demo players (idempotent).
await SeedData.InitializeAsync(app.Services, app.Configuration);

app.Run();

/// <summary>Exposed so the test project can drive the app via WebApplicationFactory.</summary>
public partial class Program;
