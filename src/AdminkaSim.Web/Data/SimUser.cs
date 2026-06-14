using Microsoft.AspNetCore.Identity;

namespace AdminkaSim.Web.Data;

/// <summary>
/// A demo player. Backed by ASP.NET Core Identity (cookie login). These are
/// casino end-users — NOT adminka operators/admins — so there is no 2FA, no
/// OIDC, no role/RBAC surface here. The platform's real operator approval
/// workflow lives in adminka, not in the sim.
/// </summary>
public sealed class SimUser : IdentityUser
{
    /// <summary>Friendly name shown in the wallet UI (falls back to the email local part).</summary>
    public string? DisplayName { get; set; }
}
