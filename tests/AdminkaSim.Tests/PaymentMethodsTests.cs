using AdminkaSim.Web;

namespace AdminkaSim.Tests;

/// <summary>
/// Guards the m-11 owner decisions encoded in the cashier catalogue:
/// Havale-only active, demo-friendly floors, and a non-trivial bank list.
/// </summary>
public class PaymentMethodsTests
{
    [Fact]
    public void OnlyTheHavaleMethodIsActive()
    {
        var active = PaymentMethods.All.Where(m => m.Active).ToList();
        Assert.Single(active);
        Assert.Contains("Havale", active[0].Name);
        Assert.Equal("transfer", PaymentMethods.ActiveMethodCode);
    }

    [Fact]
    public void DemoFloorsAreHundredToHundredThousand()
    {
        Assert.Equal(100m, PaymentMethods.MinAmount);
        Assert.Equal(100_000m, PaymentMethods.MaxAmount);

        var havale = PaymentMethods.All.Single(m => m.Active);
        Assert.Equal(PaymentMethods.MinAmount, havale.Min);
        Assert.Equal(PaymentMethods.MaxAmount, havale.Max);
    }

    [Fact]
    public void TurkishBankList_IsNonTrivial_AndDistinct()
    {
        Assert.True(TurkishBanks.Names.Count >= 10);
        Assert.Equal(TurkishBanks.Names.Count, TurkishBanks.Names.Distinct().Count());
        Assert.Contains("Ziraat Bankası", TurkishBanks.Names);
    }
}
