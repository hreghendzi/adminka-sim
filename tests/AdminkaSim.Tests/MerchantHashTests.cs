using AdminkaSim.Web.Merchant;

namespace AdminkaSim.Tests;

public class MerchantHashTests
{
    [Fact]
    public void Md5Hex_ConcatsThenLowercaseHexEncodes_KnownVector()
    {
        // RFC 1321 / canonical MD5 vector: md5("abc") = 900150983cd24fb0d6963f7d28e17f72.
        // Proves we concat the parts and hex-encode lowercase exactly like adminka's
        // MerchantHasher.Md5Hex / WebhookDeliveryService.ComputeV1Hash.
        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", MerchantHash.Md5Hex("a", "b", "c"));
    }

    [Fact]
    public void Md5Hex_IsDeterministicAndLowercase32Hex()
    {
        var h = MerchantHash.Md5Hex("ADMINKA_SIM_001", "100", "transfer", "secret");
        Assert.Equal(32, h.Length);
        Assert.Equal(h.ToLowerInvariant(), h);
        Assert.Equal(h, MerchantHash.Md5Hex("ADMINKA_SIM_001", "100", "transfer", "secret"));
    }

    [Fact]
    public void Md5Hex_OrderMatters()
    {
        Assert.NotEqual(MerchantHash.Md5Hex("a", "b"), MerchantHash.Md5Hex("b", "a"));
    }

    [Fact]
    public void ConstantTimeEquals_TrueForEqual_FalseOtherwise()
    {
        var h = MerchantHash.Md5Hex("mid", "url", "secret");
        Assert.True(MerchantHash.ConstantTimeEquals(h, h));
        Assert.False(MerchantHash.ConstantTimeEquals(h, MerchantHash.Md5Hex("mid", "url", "other")));
        Assert.False(MerchantHash.ConstantTimeEquals(h, h + "extra")); // length mismatch is safe
    }
}
