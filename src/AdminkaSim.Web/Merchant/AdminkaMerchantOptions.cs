using System.ComponentModel.DataAnnotations;

namespace AdminkaSim.Web.Merchant;

/// <summary>
/// How the sim reaches adminka as a registered merchant. Bound from the
/// <c>AdminkaMerchant</c> config section. The sim knows adminka ONLY through
/// these values (plan §3.1) — a base URL + MID + shared secret + its own
/// callback URL. No shared DB, no internal service DNS in the contract.
/// </summary>
public sealed class AdminkaMerchantOptions
{
    public const string SectionName = "AdminkaMerchant";

    /// <summary>Base URL of adminka's MerchantApi (e.g. https://adminka.../).</summary>
    [Required]
    public string BaseUrl { get; set; } = "";

    /// <summary>The sim's merchant id, as registered in adminka.</summary>
    [Required]
    public string Mid { get; set; } = "";

    /// <summary>The shared SECRETKEY. Never sent on the wire; used only to hash.</summary>
    [Required]
    public string SecretKey { get; set; } = "";

    /// <summary>
    /// The sim's OWN public callback URL (adminka POSTs status changes here).
    /// This exact string is sent as the <c>callbackUrl</c> on start AND used as
    /// the <c>CALLBACK_URL</c> hash input when verifying inbound callbacks — the
    /// two MUST be byte-identical, so it has one home here.
    /// </summary>
    [Required]
    public string CallbackUrl { get; set; } = "";

    /// <summary>Default Havale method (current wave is Havale-only).</summary>
    public string DefaultMethod { get; set; } = "transfer";

    public string Currency { get; set; } = "TRY";
}
