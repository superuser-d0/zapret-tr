using System.Text;

namespace ZapretTr.Core.Engine;

/// <summary>Kullanicinin gozden gecirip gonderecegi hata bildirimini hazirlar.</summary>
/// <remarks>
/// Bu sinif HICBIR SEY GONDERMEZ. Yalnizca GitHub'in "yeni konu" formunu
/// dolduran bir adres uretir; formu acan tarayicidir, gonder dugmesine basan
/// kullanicidir.
///
/// Neden boyle:
///
/// 1. Uygulamanin icinden dogrudan issue acmak icin bir GitHub belirteci
///    gerekir. Belirteci exe'ye gomersek herkes cikartip bizim adimiza konu
///    acabilir; kendi sunucumuza gondermek ise ayri bir sistem ve ayri bir
///    gizlilik sorunu demek.
/// 2. Anonim gelen rapora GERI SORU SORULAMIYOR. Elimizdeki en ogretici saha
///    bildirimi ("cikis kodu 34") tek basina ise yaramadi; ikinci bir kosum
///    gerekti. Bildirimin bir kullaniciya bagli olmasi, raporun kendisi kadar
///    degerli.
/// 3. Gonderilecek metni kullanicinin GORMESI gerekiyor: icinde hattinin
///    servis saglayicisi ve denenen parametreler var.
///
/// Adres uzunlugu sinirli oldugu icin gunlugun TAMAMI buraya konmuyor; son
/// satirlar konuyor ve gerisi "Raporu Kaydet" dosyasina birakiliyor.
/// </remarks>
public static class IssueReporter
{
    /// <summary>Bos konu formu.</summary>
    public const string NewIssuePage =
        "https://github.com/superuser-d0/zapret-tr/issues/new";

    /// <summary>Mevcut konularin listesi.</summary>
    public const string IssuesPage =
        "https://github.com/superuser-d0/zapret-tr/issues";

    /// <summary>
    /// Uretilen adres icin ust sinir.
    /// </summary>
    /// <remarks>
    /// Cok uzun adresler sunucudan 414 donuyor ve bazi tarayicilar sessizce
    /// kesiyor. 6000, hem GitHub'in hem tarayicilarin rahatca tasidigi bir
    /// deger. Sinira takilirsak gunlugun EN ESKI satirlarindan atiyoruz:
    /// hatanin izi son satirlarda.
    /// </remarks>
    public const int MaxUrlLength = 6000;

    /// <summary>Gunlukten alinacak en fazla satir sayisi.</summary>
    public const int MaxLogLines = 25;

    /// <summary>Tek bir gunluk satirinin govdeye girecek en fazla uzunlugu.</summary>
    /// <remarks>
    /// Satir SAYISINI sinirlamak tek basina yetmiyor: tek bir satir da cok uzun
    /// olabiliyor (tam komut satiri, uzun bir istisna metni). Satir basina sinir
    /// olmadan "adres sinirin altinda kalir" diye bir guvence veremiyoruz.
    /// </remarks>
    public const int MaxLogLineLength = 200;

    /// <summary>
    /// Bir gunluk satirinin kullanicinin kendi girdigi veriyi tasiyip
    /// tasimadigini soyler.
    /// </summary>
    /// <remarks>
    /// "Kendi hedefiniz" satiri kullanicinin arayuze YAZDIGI adresi iceriyor.
    /// Denenen parametreler ve varsayilan hedefler bizim listemiz -- onlarin
    /// paylasilmasinda sakinca yok -- ama kullanicinin kendi yazdigi adres
    /// bize ait degil ve bir forma kendiliginden dusmemeli.
    /// </remarks>
    public static bool IsUserSupplied(string logLine)
        => logLine.Contains("Kendi hedefiniz", StringComparison.OrdinalIgnoreCase);

    /// <summary>Konu basligini kurar.</summary>
    public static string BuildTitle(IssueDetails details)
        => $"[hata] {Bos(details.Status, "durum yok")} — {Bos(details.Isp, "ISS secilmemis")} (v{Bos(details.AppVersion, "?")})";

    /// <summary>Konu govdesini kurar.</summary>
    /// <param name="logLineBudget">
    /// Govdeye girecek gunluk satiri sayisi. <see cref="BuildUrl"/> adres
    /// sinirina sigana kadar bunu kucultuyor.
    /// </param>
    public static string BuildBody(IssueDetails details, int logLineBudget = MaxLogLines)
    {
        var sb = new StringBuilder();

        sb.AppendLine("> Bu bildirimi ZapretTR'nin **Hata Bildir** düğmesi hazırladı.");
        sb.AppendLine("> Hiçbir şey gönderilmedi — göndermeden önce okuyun,");
        sb.AppendLine("> paylaşmak istemediğiniz satırı silin.");
        sb.AppendLine();

        sb.AppendLine("## Ne oldu?");
        sb.AppendLine();
        sb.AppendLine("<!-- Ne yapmaya çalıştınız, ne bekliyordunuz, ne oldu?");
        sb.AppendLine("     Birkaç cümle yeterli. Ekran görüntüsü varsa sürükleyip bırakın. -->");
        sb.AppendLine();
        sb.AppendLine();

        sb.AppendLine("## Ortam");
        sb.AppendLine();
        sb.AppendLine("| Alan | Değer |");
        sb.AppendLine("| --- | --- |");
        sb.AppendLine("| ZapretTR | " + Hucre(details.AppVersion) + " |");
        sb.AppendLine("| Motor | " + Hucre(details.EngineVersion) + " |");
        sb.AppendLine("| Windows | " + Hucre(details.Windows) + " |");
        sb.AppendLine("| Servis sağlayıcı | " + Hucre(details.Isp) + " |");
        sb.AppendLine("| Strateji | " + Hucre(details.Strategy) + " |");
        sb.AppendLine("| Parametre | " + Hucre(details.StrategyArgs) + " |");
        sb.AppendLine("| Şifreli DNS | " + (details.SecureDns ? "açık" : "kapalı") + " |");
        sb.AppendLine("| Otomatik başlatma | " + Hucre(details.ServiceState) + " |");
        sb.AppendLine("| Durum | " + Hucre(details.Status) + " |");
        sb.AppendLine();

        var satirlar = details.LogLines
            .Where(s => !IsUserSupplied(s))
            .TakeLast(Math.Max(0, logLineBudget))
            .Select(Kisalt)
            .ToList();

        if (satirlar.Count > 0)
        {
            sb.AppendLine("## Günlük (son satırlar)");
            sb.AppendLine();
            sb.AppendLine("```");
            foreach (var satir in satirlar)
            {
                sb.AppendLine(satir);
            }

            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("Günlüğün tamamı gerekiyorsa uygulamadaki **Raporu Kaydet**");
        sb.AppendLine("düğmesiyle dosyayı kaydedip bu konuya sürükleyebilirsiniz.");

        return sb.ToString();
    }

    /// <summary>Doldurulmus konu formunun adresini kurar.</summary>
    /// <remarks>
    /// Adres <see cref="MaxUrlLength"/>'i asarsa gunluk satiri sayisi
    /// azaltilarak yeniden deneniyor. Sifir satirla bile sigmiyorsa -- ki
    /// ortam tablosu sabit boyutta oldugu icin olmamali -- yine de o adres
    /// donuyor: eksik bir bildirim, hic bildirim olmamasindan iyi.
    /// </remarks>
    public static string BuildUrl(IssueDetails details)
    {
        var baslik = Uri.EscapeDataString(BuildTitle(details));

        for (var butce = MaxLogLines; butce >= 0; butce -= 5)
        {
            var adres = NewIssuePage
                + "?title=" + baslik
                + "&body=" + Uri.EscapeDataString(BuildBody(details, butce));

            if (adres.Length <= MaxUrlLength || butce == 0)
            {
                return adres;
            }
        }

        // Dongu butce == 0'da mutlaka donuyor; derleyiciyi memnun etmek icin.
        return NewIssuePage;
    }

    private static string Kisalt(string satir)
        => satir.Length <= MaxLogLineLength
            ? satir
            : satir[..MaxLogLineLength] + " …(kesildi)";

    private static string Bos(string? deger, string yedek)
        => string.IsNullOrWhiteSpace(deger) ? yedek : deger.Trim();

    // Tablo hucresinde bolme cizgisi sutunu kiriyor; parametrelerde "|" gecmiyor
    // ama gecerse tabloyu bozmasin.
    private static string Hucre(string? deger)
        => Bos(deger, "-").Replace("|", @"\|", StringComparison.Ordinal);
}

/// <summary>Hata bildirimine girecek ortam ozeti.</summary>
public sealed record IssueDetails(
    string AppVersion,
    string EngineVersion,
    string Windows,
    string Isp,
    string Strategy,
    string StrategyArgs,
    bool SecureDns,
    string ServiceState,
    string Status,
    IReadOnlyList<string> LogLines);
