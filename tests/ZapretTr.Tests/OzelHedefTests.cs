using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// "Açılmayan site" kutusuna yazılan adresin sessizce düşmemesi.
/// </summary>
/// <remarks>
/// ÖLÇÜLDÜ (issue #1, KeremKuyucu, 2026-09-15): kullanıcı Roblox'u eklemeye çalıştı,
/// olmayınca virgülle iki adres yazdı, yine olmadı ve "ya çalışmıyor ya da bir şeyi
/// yanlış yapıyorum" dedi. İkisi de değildi: girdisi reddediliyordu ve bunu ona
/// söyleyen HİÇBİR ŞEY yoktu. Başarı yolunda "Kendi hedefiniz eklendi: ..." satırı
/// vardı, başarısızlık yolunda hiçbir şey.
///
/// Sessiz reddetme, çalışmayan bir özellikten daha kötü: kullanıcı yanlış bir şey
/// yaptığını bilmiyor, dolayısıyla düzeltemiyor. Bu testler mesajın varlığını ve
/// iki ayrıştırıcının AYNI kararı verdiğini sabitliyor.
/// </remarks>
public sealed class OzelHedefTests
{
    // --- Ne zaman şikâyet edilmiyor ---------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Bos_Birakmak_Gecerli_Bir_Secim(string? girdi)
        // Boş bırakmak "varsayılan hedefleri kullan" demek; uyarı çıkarsa kullanıcı
        // yapmadığı bir hatayı aramaya başlar.
        => Assert.Null(HostlistStore.DescribeUnusableTarget(girdi));

    [Theory]
    [InlineData("roblox.com")]
    [InlineData("www.roblox.com")]
    [InlineData("  Roblox.COM  ")]
    [InlineData("https://www.roblox.com/home")]
    [InlineData("roblox.com/games")]
    public void Anlasilan_Adres_Icin_Sikayet_Yok(string girdi)
        => Assert.Null(HostlistStore.DescribeUnusableTarget(girdi));

    // --- Ne zaman şikâyet ediliyor ----------------------------------------------

    [Theory]
    [InlineData("roblox.com, discord.com")]
    [InlineData("roblox.com,discord.com")]
    [InlineData("roblox.com discord.com")]
    public void Birden_Fazla_Adres_Sebebiyle_Birlikte_Reddediliyor(string girdi)
    {
        var mesaj = HostlistStore.DescribeUnusableTarget(girdi);

        Assert.NotNull(mesaj);

        // Kullanıcının ne yapacağını bilmesi ŞART: "anlaşılamadı" tek başına yetmiyor.
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
        // Günlükte hangi girdinin reddedildiği görünmezse kullanıcı hangi
        // denemesinin tutmadığını bilemez.
        var mesaj = HostlistStore.DescribeUnusableTarget("a.com,b.com");

        Assert.Contains("a.com,b.com", mesaj, StringComparison.Ordinal);
    }

    // --- İki ayrıştırıcı aynı kararı vermeli -------------------------------------

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
        // Ayrılırlarsa en kötü hata sınıfı doğar: hedef TESTTE deneniyor ve
        // doğrulanıyor ama ÇALIŞMA ZAMANINDA listeye girmiyor; kullanıcı
        // korunduğunu sanarak korumasız kalıyor. İkisi ayrı ayrıştırıcı olduğu
        // için bu ancak testle sabitlenebilir.
        var olcumKabulEtti = ProbeTargetStore.TryParseUserTarget(girdi) is not null;
        var calismaKabulEtti = HostlistStore.TryParseDomain(girdi) is not null;

        Assert.Equal(olcumKabulEtti, calismaKabulEtti);
    }

    [Theory]
    [InlineData("roblox.com")]
    [InlineData("https://www.roblox.com/home")]
    public void Kabul_Edilen_Adres_Calisma_Zamani_Listesine_Giriyor(string girdi)
    {
        // Zincirin tamamı: kullanıcı girdisi -> alan adı -> --hostlist-domains.
        // Bu kopmazsa kullanıcının eklediği site GERÇEKTEN korunuyor.
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
        // Reddedilen girdi yüzünden Discord koruması kaybolmamalı: şikâyet
        // ediliyor ama geri kalan daraltma aynen sürüyor.
        var alanlar = HostlistStore
            .Load(XmlCommentTests.RepoRoot + "/profiles")
            .DomainsFor(["discord"], "a.com,b.com");

        Assert.Contains("discord.com", alanlar);
        Assert.DoesNotContain(alanlar, a => a.Contains(',', StringComparison.Ordinal));
    }
}
