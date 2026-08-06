namespace AdminkaSim.Web.Merchant;

/// <summary>
/// Keeps a player on the domain they are already browsing when the sim hands
/// them off to the AdminkaPay hosted payment page (adminka board TASK-321).
/// </summary>
/// <remarks>
/// <para>
/// adminka mints the absolute <c>paymentUrl</c> from a single configured host
/// (<c>PaymentPortal:BaseUrl</c>) because the deposit call is server-to-server
/// and carries no browser context — and that value is part of what merchants
/// receive on the wire, so it is deliberately NOT per-request. When the sim is
/// served at several domains at once (sim.masisyepremyan.com and sim.hdpay.org
/// resolve to the same release), the returned URL would send a player who came
/// in on one brand domain to the portal on the other.
/// </para>
/// <para>
/// This mirror rewrites ONLY the origin, and only to an origin on the
/// configured <c>AdminkaPay:PortalOrigins</c> allowlist whose parent domain
/// matches the player's current host — <c>sim.hdpay.org</c> selects
/// <c>https://pay.hdpay.org</c>. The path and query (the signed <c>ref</c>)
/// are carried over untouched, and anything unmatched falls through to the URL
/// exactly as adminka minted it. It is a presentation choice on the merchant
/// side, not a change to the wire contract.
/// </para>
/// </remarks>
public static class PortalOriginMirror
{
    /// <summary>
    /// Returns <paramref name="paymentUrl"/> re-hosted onto the allowlisted
    /// portal origin that shares <paramref name="requestHost"/>'s parent
    /// domain, or unchanged when there is no such origin.
    /// </summary>
    /// <param name="paymentUrl">The absolute paymentUrl adminka returned.</param>
    /// <param name="requestHost">The Host the player is browsing (no scheme).</param>
    /// <param name="portalOriginsCsv">
    /// <c>AdminkaPay:PortalOrigins</c> — CSV of every origin the AdminkaPay
    /// portal is reachable at, e.g.
    /// <c>https://pay.masisyepremyan.com,https://pay.hdpay.org</c>.
    /// </param>
    public static string Mirror(string paymentUrl, string? requestHost, string? portalOriginsCsv)
    {
        if (string.IsNullOrWhiteSpace(paymentUrl)
            || string.IsNullOrWhiteSpace(requestHost)
            || string.IsNullOrWhiteSpace(portalOriginsCsv)
            || !Uri.TryCreate(paymentUrl, UriKind.Absolute, out var minted))
        {
            return paymentUrl;
        }

        var requestDomain = ParentDomain(HostOnly(requestHost));
        if (requestDomain is null)
        {
            return paymentUrl;
        }

        foreach (var entry in portalOriginsCsv.Split(
                     ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(entry, UriKind.Absolute, out var candidate)
                || (candidate.Scheme != Uri.UriSchemeHttp && candidate.Scheme != Uri.UriSchemeHttps))
            {
                continue;
            }

            if (!string.Equals(ParentDomain(candidate.Host), requestDomain, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var origin = candidate.IsDefaultPort
                ? $"{candidate.Scheme}://{candidate.Host}"
                : $"{candidate.Scheme}://{candidate.Host}:{candidate.Port}";

            return origin + minted.PathAndQuery + minted.Fragment;
        }

        return paymentUrl;
    }

    /// <summary>Strips a <c>host:port</c> pair down to the host.</summary>
    private static string HostOnly(string host)
    {
        var colon = host.LastIndexOf(':');
        return colon > 0 && !host.Contains(']', StringComparison.Ordinal) ? host[..colon] : host;
    }

    /// <summary>
    /// Everything after the first label — the domain the sibling subdomains of
    /// one deployment share (<c>sim.hdpay.org</c> → <c>hdpay.org</c>). Returns
    /// <see langword="null"/> for a single-label host or a bare IP, so LAN and
    /// localhost deployments never mirror.
    /// </summary>
    private static string? ParentDomain(string host)
    {
        if (System.Net.IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var dot = host.IndexOf('.');
        return dot > 0 && dot < host.Length - 1 ? host[(dot + 1)..] : null;
    }
}
