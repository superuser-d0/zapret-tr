using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// "Acilmayan site" kutusuna yazilan adresin sessizce dusmemesi.
/// </summary>
/// <remarks>
/// OLCULDU (issue #1, KeremKuyucu, 2026-09-15): kullanici Roblox'u eklemeye calisti,
/// olmayinca virgulle iki adres yazdi, yine olmadi ve "ya calismiyor ya da bir seyi
/// yanlis yapiyorum" dedi. Ikisi de degildi: girdisi reddediliyordu ve bunu ona
/// soyleyen HICBIR SEY yoktu. Basari yolunda "Kendi hedefiniz eklendi: ..." satiri
/// vardi, basarisizlik yolunda hicbir sey.
///
/// Sessiz reddetme, calismayan bir ozellikten daha kotu: kullanici yanlis bir sey
/// yaptigini bilmiyor, dolayisiyla duzeltemiyor. Bu testler mesajin varligini ve
/// iki ayristiricinin AYNI kararI verdigini sabitliyor.
/// </remarks>
public sealed class OzelHedefTests
{
    // --- Ne zaman sikayet edilmiyor ---------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_Birakmak_Gecerli_Bir_Secim(string? girdi)
        // Bos birakmak "varsayilan hedefleri kullan" demek; uyari cikarsa kullanici
        // yapmadigi bir hatayi aramaya baslar.
        => Assert.Null(HostlistStore.DescribeUnusableTarget(girdi));

    [Theory]
    [InlineData("roblox.com")]
    [InlineData("www.roblox.com")]
    [InlineData("  Roblox.COM  ")]
    [InlineData("https://www.roblox.com/home")]
    [InlineData("roblox.com/games")]
    public void Anlasilan_Adres_Icin_Sikayet_Yok(string girdi)
        => Assert.Null(HostlistStore.DescribeUnusableTarget(girdi));

    // --- Ne zaman sikayet ediliyor ----------------------------------------------

    [Theory]
    [InlineData("roblox.com, discord.com")]
    [InlineData("roblox.com,discord.com")]
    [InlineData("roblox.com discord.com")]
    public void Birden_Fazla_Adres_Sebebiyle_Birlikte_Reddediliyor(string girdi)
    {
        var mesaj = HostlistStore.DescribeUnusableTarget(girdi);

        Assert.NotNull(mesaj);

        // Kullanicinin ne yapacagini bilmesi SART: "anlasilamadi" tek basina yetmiyor.
        Assert.Contains("TEK", mesaj, StringComparison.Ordinal);
        Assert.Contains("roblox.com", mesaj, StringComparison.Ordinal);
    }

    [Fact]
    public void Anlasilmayan_Girdi_Ornekle_Reddediliyor()
    {
        var mesaj = HostlistStore.DescribeUnusableTarget("??");

        Assert.NotNull(mesaj);
        Assert.Contains("roblox.com", mesaj, StringComparison.Ordinal);
    }

    [Fact]
    public void Mesaj_Kullanicinin_Yazdigini_Geri_Gosteriyor()
    {
        // Gunlukte hangi girdinin reddedildigi gorunmezse kullanici hangi
        // denemesinin tutmadigini bilemez.
        var mesaj = HostlistStore.DescribeUnusableTarget("a.com,b.com");

        Assert.Contains("a.com,b.com", mesaj, StringComparison.Ordinal);
    }

    // --- Iki ayristirici ayni kararI vermeli -------------------------------------

    [Theory]
    [InlineData("roblox.com")]
    [InlineData("www.roblox.com")]
    [InlineData("https://www.roblox.com/home")]
    [InlineData("roblox.com/games")]
    [InlineData("roblox.com, discord.com")]
    [InlineData("roblox.com,discord.com")]
    [InlineData("??")]
    [InlineData("")]
    public void Olcum_Ve_Calisma_Zamani_Ayni_Karari_Veriyor(string girdi)
    {
        // Ayrilirlarsa en kotu hata sinifi dogar: hedef TESTTE deneniyor ve
        // dogrulaniyor ama CALISMA ZAMANINDA listeye girmiyor -- kullanici
        // korundugunu sanarak korumasiz kaliyor. Ikisi ayri ayristirici oldugu
        // icin bu ancak testle sabitlenebilir.
        var olcumKabulEtti = ProbeTargetStore.TryParseUserTarget(girdi) is not null;
        var calismaKabulEtti = HostlistStore.TryParseDomain(girdi) is not null;

        Assert.Equal(olcumKabulEtti, calismaKabulEtti);
    }

    [Theory]
    [InlineData("roblox.com")]
    [InlineData("https://www.roblox.com/home")]
    public void Kabul_Edilen_Adres_Calisma_Zamani_Listesine_Giriyor(string girdi)
    {
        // Zincirin tamami: kullanici girdisi -> alan adi -> --hostlist-domains.
        // Bu kopmazsa kullanicinin ekledigi site GERCEKTEN korunuyor.
        var alanlar = HostlistStore
            .Load(XmlCommentTests.RepoRoot + "/profiles")
            .DomainsFor(["discord"], girdi);

        var beklenen = HostlistStore.TryParseDomain(girdi);

        Assert.NotNull(beklenen);
        Assert.Contains(beklenen, alanlar);
    }

    [Fact]
    public void Reddedilen_Adres_Listeyi_Bozmuyor()
    {
        // Reddedilen girdi yuzunden discord korumasi kaybolmamali: sikayet
        // ediliyor ama geri kalan daraltma aynen suruyor.
        var alanlar = HostlistStore
            .Load(XmlCommentTests.RepoRoot + "/profiles")
            .DomainsFor(["discord"], "a.com,b.com");

        Assert.Contains("discord.com", alanlar);
        Assert.DoesNotContain(alanlar, a => a.Contains(',', StringComparison.Ordinal));
    }
}
