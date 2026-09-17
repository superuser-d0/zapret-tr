using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>Hata bildirimi formunun içeriğini sınar.</summary>
/// <remarks>
/// Bu testlerin hepsi TEK bir soruya bakıyor: kullanıcıya gösterilmeden önce
/// forma NE kondu. Üretilen metin bir kere gönderildikten sonra geri alınamaz;
/// bu yüzden içeriğin kuralları kodda değil, burada sabitleniyor.
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

        // Bunların biri eksikse bildirim gelse bile işe yaramıyor: hangi sürümde,
        // hangi hatta, hangi parametreyle olduğunu bilmeden hiçbir şey yapamıyoruz.
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
        // ASIL KONTROL. Günlükteki öteki satırlar bizim listemiz: denenen
        // parametreler, varsayılan hedefler. Ama "kendi hedefiniz" satırındaki
        // adresi kullanıcı arayüze KENDİ yazdı; bir genel forma kendiliğinden
        // düşmemeli. Kullanıcı isterse elle ekler, bu onun kararı.
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
        // Uzun adres sunucudan 414 dönüyor ve kullanıcı boş bir sayfa görüyor;
        // yani "hata bildir" düğmesi tam da hata çok konuştuğunda çalışmıyor.
        //
        // Satırlar KASITLI olarak hem çok hem uzun. İlk yazdığımda 400 KISA satır
        // vermiştim: son 25 satır zaten sınırın altında kaldığı için kısaltma
        // döngüsü hiç çalışmıyordu ve testi bozduğumda hâlâ geçiyordu. Yani test
        // sınamak istediği şeyi sınamıyordu.
        var uzun = Enumerable.Range(0, 400)
            .Select(i => $"[{i:D3}] aday denendi: " + new string('x', 600));

        var adres = IssueReporter.BuildUrl(Ornek(uzun));

        Assert.True(
            adres.Length <= IssueReporter.MaxUrlLength,
            $"adres {adres.Length} karakter, sinir {IssueReporter.MaxUrlLength}");

        // Kısaltma teşhisin ÇEKİRDEĞİNİ yemiş olmamalı: ortam tablosu her
        // durumda kalmalı, atılacak şey günlük satırı.
        var govde = IssueReporter.BuildBody(Ornek(uzun), logLineBudget: 0);
        Assert.Contains("Turkcell Superonline", govde, StringComparison.Ordinal);
        Assert.Contains("0.1.18", govde, StringComparison.Ordinal);
    }

    [Fact]
    public void Tek_satir_da_kisaltiliyor()
    {
        // Satır SAYISINI sınırlamak yetmiyor: tek bir satır tam bir komut satırı
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

        // Başlık ve gövde kaçışlanmış olarak gitmezse "&" ya da "#" geçen bir
        // parametre adresi ortadan bölerdi.
        Assert.DoesNotContain(" ", adres, StringComparison.Ordinal);
        Assert.Contains("title=", adres, StringComparison.Ordinal);
        Assert.Contains("body=", adres, StringComparison.Ordinal);
    }

    [Fact]
    public void Baslik_surumu_ve_durumu_tasiyor()
    {
        // Konu listesinde başlığa bakıp "bu hangi sürüm" diyebilmek gerekiyor:
        // aynı belirti farklı sürümlerde farklı sebeplerden çıkabiliyor.
        var baslik = IssueReporter.BuildTitle(Ornek());

        Assert.Contains("v0.1.18", baslik, StringComparison.Ordinal);
        Assert.Contains("ÇALIŞIYOR", baslik, StringComparison.Ordinal);
        Assert.Contains("Turkcell", baslik, StringComparison.Ordinal);

        // Ayraç, durum metninin KENDİ uzun tiresiyle karışmamalı: gerçek çıktıda
        // "[hata] ÇALIŞIYOR — AMA AÇMIYOR — Turkcell ..." okunmuyordu.
        Assert.DoesNotContain("] ÇALIŞIYOR", baslik, StringComparison.Ordinal);
        Assert.Equal(2, baslik.Split(" · ").Length - 1);
    }

    [Fact]
    public void Basliktaki_surum_yapinin_karmasini_tasimiyor()
    {
        // Gerçek koşumda görüldü: uygulamanın sürüm metni
        // "0.1.18+a8a66ca13a4b..." şeklinde geliyor ve başlık 93 karakterin
        // yarısı karma olan bir şeye dönüşüyordu. Karma ortam tablosunda tam
        // hâliyle duruyor; başlıkta işi yok.
        var d = Ornek() with { AppVersion = "0.1.18+a8a66ca13a4b2675ea2f8b6fe217a3ba9fd3bad8" };

        Assert.Contains("v0.1.18 ·", IssueReporter.BuildTitle(d), StringComparison.Ordinal);
        Assert.DoesNotContain("a8a66ca", IssueReporter.BuildTitle(d), StringComparison.Ordinal);

        // Gövdede ise KALMALI: hangi yapının bildirimi olduğu kaybolmasın.
        Assert.Contains("a8a66ca", IssueReporter.BuildBody(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Secim_yapilmamisken_de_form_kuruluyor()
    {
        // Hatanın en sık görüldüğü an ilk açılış: henüz İSS de strateji de
        // seçilmemiş. Bildirim tam o anda kurulamıyorsa düğmenin anlamı kalmaz.
        var bos = new IssueDetails(
            "0.1.18", "bilinmiyor", "10.0.26200",
            Isp: string.Empty, Strategy: string.Empty, StrategyArgs: string.Empty,
            SecureDns: false, ServiceState: "kurulu değil", Status: "HAZIR",
            LogLines: []);

        var adres = IssueReporter.BuildUrl(bos);

        Assert.True(Uri.TryCreate(adres, UriKind.Absolute, out _));
        Assert.Contains("ISS seçilmemiş", IssueReporter.BuildTitle(bos), StringComparison.Ordinal);
    }
}
