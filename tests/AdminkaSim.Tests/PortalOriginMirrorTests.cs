using AdminkaSim.Web.Merchant;
using Xunit;

namespace AdminkaSim.Tests;

/// <summary>
/// Unit tests for <see cref="PortalOriginMirror"/> — the AdminkaPay hand-off
/// origin swap that keeps a player on the domain they are browsing when the
/// sim answers at more than one (adminka board TASK-321).
/// </summary>
public class PortalOriginMirrorTests
{
    private const string Origins = "https://pay.masisyepremyan.com,https://pay.hdpay.org";

    private const string MintedUrl =
        "https://pay.masisyepremyan.com/p/account?ref=abc123&sig=deadbeef";

    [Fact]
    public void Player_on_the_secondary_domain_is_sent_to_that_domains_portal()
    {
        var url = PortalOriginMirror.Mirror(MintedUrl, "sim.hdpay.org", Origins);

        Assert.Equal("https://pay.hdpay.org/p/account?ref=abc123&sig=deadbeef", url);
    }

    [Fact]
    public void Signed_ref_and_query_survive_the_swap_verbatim()
    {
        var url = PortalOriginMirror.Mirror(MintedUrl, "sim.hdpay.org", Origins);

        Assert.EndsWith("/p/account?ref=abc123&sig=deadbeef", url, StringComparison.Ordinal);
    }

    [Fact]
    public void Player_on_the_minting_domain_gets_the_url_unchanged()
    {
        Assert.Equal(MintedUrl, PortalOriginMirror.Mirror(MintedUrl, "sim.masisyepremyan.com", Origins));
    }

    [Theory]
    // No portal on that domain — fall through to what adminka minted.
    [InlineData("sim.example.org")]
    // LAN / IP host: no parent domain, never mirrored.
    [InlineData("192.168.0.5:30480")]
    // Single-label host.
    [InlineData("localhost:5080")]
    public void Unmatched_host_leaves_the_minted_url_alone(string host)
    {
        Assert.Equal(MintedUrl, PortalOriginMirror.Mirror(MintedUrl, host, Origins));
    }

    [Fact]
    public void Host_port_is_ignored_when_matching_the_parent_domain()
    {
        var url = PortalOriginMirror.Mirror(MintedUrl, "sim.hdpay.org:8443", Origins);

        Assert.Equal("https://pay.hdpay.org/p/account?ref=abc123&sig=deadbeef", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_configured_origins_is_a_no_op(string? origins)
    {
        Assert.Equal(MintedUrl, PortalOriginMirror.Mirror(MintedUrl, "sim.hdpay.org", origins));
    }

    [Fact]
    public void Relative_or_malformed_payment_url_is_returned_untouched()
    {
        Assert.Equal("/p/account?ref=abc", PortalOriginMirror.Mirror("/p/account?ref=abc", "sim.hdpay.org", Origins));
    }

    [Fact]
    public void Non_http_origin_entries_are_ignored()
    {
        var url = PortalOriginMirror.Mirror(MintedUrl, "sim.hdpay.org", "javascript:alert(1),https://pay.hdpay.org");

        Assert.Equal("https://pay.hdpay.org/p/account?ref=abc123&sig=deadbeef", url);
    }
}
