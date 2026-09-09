using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>Hata bildirimi formunun icerigini sinar.</summary>
/// <remarks>
/// Bu testlerin hepsi TEK bir soruya bakiyor: kullaniciya gosterilmeden once
/// forma NE kondu. Uretilen metin bir kere gonderildikten sonra geri alinamaz;
/// bu yuzden icerigin kurallari kodda degil, burada sabitleniyor.
/// </remarks>
public class IssueReporterTests
{
    private static IssueDetails Ornek(IEnumerable<string>? gunluk = null) => new(
        AppVersion: "0.1.18",
        EngineVersion: "winws v72.13",
        Windows: "10.0.26200",
        Isp: "Turkcell Superonline",
        Strategy: "multisplit + md5sig",
        StrategyArgs: "--dpi-desync=multisplit --dpi-desync-fooling=md5sig",
        SecureDns: true,
        ServiceState: "kurulu ama durmuş",
        Status: "ÇALIŞIYOR — AMA AÇMIYOR",
        LogLines: [.. gunluk ?? ["motor basladi", "cikis kodu 34"]]);

    [Fact]
    public void Govde_teshis_icin_gereken_alanlari_tasiyor()
    {
        var govde = IssueReporter.BuildBody(Ornek());

        // Bunlarin biri eksikse bildirim gelse bile ise yaramiyor: hangi surumde,
        // hangi hatta, hangi parametreyle oldugunu bilmeden hicbir sey yapamiyoruz.
        Assert.Contains("0.1.18", govde, StringComparison.Ordinal);
        Assert.Contains("winws v72.13", govde, StringComparison.Ordinal);
        Assert.Contains("Turkcell Superonline", govde, StringComparison.Ordinal);
        Assert.Contains("--dpi-desync=multisplit", govde, StringComparison.Ordinal);
        Assert.Contains("kurulu ama durmuş", govde, StringComparison.Ordinal);
        Assert.Contains("cikis kodu 34", govde, StringComparison.Ordinal);
    }

    [Fact]
    public void Kullanicinin_kendi_yazdigi_adres_forma_girmiyor()
    {
        // ASIL KONTROL. Gunlukteki oteki satirlar bizim listemiz -- denenen
        // parametreler, varsayilan hedefler. Ama "kendi hedefiniz" satirindaki
        // adresi kullanici arayuze KENDI yazdi; bir genel forma kendiliginden
        // dusmemeli. Kullanici isterse elle ekler, bu onun karari.
        var govde = IssueReporter.BuildBody(Ornek([
            "motor basladi",
            "Kendi hedefiniz eklendi: sirket-ic-portal.ornek.tr",
            "cikis kodu 34",
        ]));

        Assert.DoesNotContain("sirket-ic-portal", govde, StringComparison.Ordinal);
        Assert.Contains("cikis kodu 34", govde, StringComparison.Ordinal);
    }

    [Fact]
    public void Uzun_gunluk_adresi_sinirin_ustune_cikarmiyor()
    {
        // Uzun adres sunucudan 414 donuyor ve kullanici bos bir sayfa goruyor --
        // yani "hata bildir" dugmesi tam da hata cok konustugunda calismiyor.
        //
        // Satirlar KASITLI olarak hem cok hem uzun. Ilk yazdigimda 400 KISA satir
        // vermistim: son 25 satir zaten sinirin altinda kaldigi icin kisaltma
        // dongusu hic calismiyordu ve testi bozdugumda hala geciyordu. Yani test
        // sinamak istedigi seyi sinamiyordu.
        var uzun = Enumerable.Range(0, 400)
            .Select(i => $"[{i:D3}] aday denendi: " + new string('x', 600));

        var adres = IssueReporter.BuildUrl(Ornek(uzun));

        Assert.True(
            adres.Length <= IssueReporter.MaxUrlLength,
            $"adres {adres.Length} karakter, sinir {IssueReporter.MaxUrlLength}");

        // Kisaltma teshisin CEKIRDEGINI yemis olmamali: ortam tablosu her
        // durumda kalmali, atilacak sey gunluk satiri.
        var govde = IssueReporter.BuildBody(Ornek(uzun), logLineBudget: 0);
        Assert.Contains("Turkcell Superonline", govde, StringComparison.Ordinal);
        Assert.Contains("0.1.18", govde, StringComparison.Ordinal);
    }

    [Fact]
    public void Tek_satir_da_kisaltiliyor()
    {
        // Satir SAYISI sinirlamak yetmiyor: tek bir satir tam bir komut satiri
        // ya da uzun bir istisna metni olabiliyor.
        var govde = IssueReporter.BuildBody(Ornek([new string('y', 5000)]));

        Assert.Contains("…(kesildi)", govde, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('y', IssueReporter.MaxLogLineLength + 1), govde, StringComparison.Ordinal);
    }

    [Fact]
    public void Adres_gecerli_ve_dogru_depoyu_gosteriyor()
    {
        var adres = IssueReporter.BuildUrl(Ornek());

        Assert.True(Uri.TryCreate(adres, UriKind.Absolute, out var uri));
        Assert.Equal("github.com", uri!.Host);
        Assert.StartsWith("/superuser-d0/zapret-tr/issues/new", uri.AbsolutePath, StringComparison.Ordinal);

        // Baslik ve govde kacirilmis olarak gitmezse "&" ya da "#" gecen bir
        // parametre adresi ortadan bolerdi.
        Assert.DoesNotContain(" ", adres, StringComparison.Ordinal);
        Assert.Contains("title=", adres, StringComparison.Ordinal);
        Assert.Contains("body=", adres, StringComparison.Ordinal);
    }

    [Fact]
    public void Baslik_surumu_ve_durumu_tasiyor()
    {
        // Konu listesinde basliga bakip "bu hangi surum" diyebilmek gerekiyor:
        // ayni belirti farkli surumlerde farkli sebeplerden cikabiliyor.
        var baslik = IssueReporter.BuildTitle(Ornek());

        Assert.Contains("v0.1.18", baslik, StringComparison.Ordinal);
        Assert.Contains("ÇALIŞIYOR", baslik, StringComparison.Ordinal);
        Assert.Contains("Turkcell", baslik, StringComparison.Ordinal);
    }

    [Fact]
    public void Secim_yapilmamisken_de_form_kuruluyor()
    {
        // Hatanin en sik goruldugu an ilk acilis: henuz ISS de strateji de
        // secilmemis. Bildirim tam o anda kurulamiyorsa dugmenin anlami kalmaz.
        var bos = new IssueDetails(
            "0.1.18", "bilinmiyor", "10.0.26200",
            Isp: string.Empty, Strategy: string.Empty, StrategyArgs: string.Empty,
            SecureDns: false, ServiceState: "kurulu değil", Status: "HAZIR",
            LogLines: []);

        var adres = IssueReporter.BuildUrl(bos);

        Assert.True(Uri.TryCreate(adres, UriKind.Absolute, out _));
        Assert.Contains("ISS secilmemis", IssueReporter.BuildTitle(bos), StringComparison.Ordinal);
    }
}
