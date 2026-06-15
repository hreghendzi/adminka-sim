namespace AdminkaSim.Web;

/// <summary>
/// Static list of Turkish banks for the withdraw bank dropdown (m-11). This is
/// the PLAYER's own bank (where they receive the Havale), a fixed client-side
/// list exactly like the reference merchant — deliberately NOT adminka's
/// <c>action=banks</c> (those are the casino's pool-account banks, a different
/// set). The selected name goes on the wire as <c>bankName</c>.
/// </summary>
public static class TurkishBanks
{
    public static readonly IReadOnlyList<string> Names =
    [
        "Akbank",
        "Albaraka Türk",
        "Anadolubank",
        "Alternatif Bank",
        "Burgan Bank",
        "DenizBank",
        "Enpara",
        "Fibabanka",
        "Garanti BBVA",
        "Halkbank",
        "HSBC",
        "ING",
        "İş Bankası",
        "Kuveyt Türk",
        "Odeabank",
        "PTT Bank",
        "QNB Finansbank",
        "Şekerbank",
        "TEB",
        "Türkiye Finans",
        "VakıfBank",
        "Vakıf Katılım",
        "Yapı Kredi",
        "Ziraat Bankası",
        "Ziraat Katılım",
    ];
}
