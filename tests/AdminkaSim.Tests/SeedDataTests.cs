using AdminkaSim.Web.Data;

namespace AdminkaSim.Tests;

/// <summary>
/// P0 smoke checks that need no database. The boot + migrate + login
/// integration test (WebApplicationFactory against a Postgres service
/// container) lands with P1, once the test-DB strategy is set.
/// </summary>
public class SeedDataTests
{
    [Fact]
    public void DemoEmails_AreThreeAndUnique()
    {
        Assert.Equal(3, SeedData.DemoEmails.Length);
        Assert.Equal(SeedData.DemoEmails.Length, SeedData.DemoEmails.Distinct().Count());
    }

    [Fact]
    public void DemoEmails_AllLookLikeEmails()
    {
        Assert.All(SeedData.DemoEmails, e => Assert.Contains("@", e));
    }
}
