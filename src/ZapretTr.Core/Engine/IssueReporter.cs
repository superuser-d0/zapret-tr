using System.Text;

namespace ZapretTr.Core.Engine;

/// <summary>Kullanıcının gözden geçirip göndereceği hata bildirimini hazırlar.</summary>
/// <remarks>
/// Bu sınıf HİÇBİR ŞEY GÖNDERMEZ. Yalnızca GitHub'ın "yeni konu" formunu
/// dolduran bir adres üretir; formu açan tarayıcıdır, gönder düğmesine basan
/// kullanıcıdır.
///
/// Neden böyle:
///
/// 1. Uygulamanın içinden doğrudan issue açmak için bir GitHub belirteci
///    gerekir. Belirteci exe'ye gömersek herkes çıkarıp bizim adımıza konu
///    açabilir; kendi sunucumuza göndermek ise ayrı bir sistem ve ayrı bir
///    gizlilik sorunu demek.
/// 2. Anonim gelen rapora GERİ SORU SORULAMIYOR. Elimizdeki en öğretici saha
///    bildirimi ("çıkış kodu 34") tek başına işe yaramadı; ikinci bir koşum
///    gerekti. Bildirimin bir kullanıcıya bağlı olması, raporun kendisi kadar
///    değerli.
/// 3. Gönderilecek metni kullanıcının GÖRMESİ gerekiyor: içinde hattının
///    servis sağlayıcısı ve denenen parametreler var.
///
/// Adres uzunluğu sınırlı olduğu için günlüğün TAMAMI buraya konmuyor; son
/// satırlar konuyor ve gerisi "Raporu Kaydet" dosyasına bırakılıyor.
/// </remarks>
public static class IssueReporter
{
    /// <summary>Boş konu formu.</summary>
    public const string NewIssuePage =
        "https://github.com/superuser-d0/zapret-tr/issues/new";

    /// <summary>Mevcut konuların listesi.</summary>
    public const string IssuesPage =
        "https://github.com/superuser-d0/zapret-tr/issues";

    /// <summary>
    /// Üretilen adres için üst sınır.
    /// </summary>
    /// <remarks>
    /// Çok uzun adresler sunucudan 414 dönüyor ve bazı tarayıcılar sessizce
    /// kesiyor. 6000, hem GitHub'ın hem tarayıcıların rahatça taşıdığı bir
    /// değer. Sınıra takılırsak günlüğün EN ESKİ satırlarından atıyoruz:
    /// hatanın izi son satırlarda.
    /// </remarks>
    public const int MaxUrlLength = 6000;

    /// <summary>Günlükten alınacak en fazla satır sayısı.</summary>
    public const int MaxLogLines = 25;

    /// <summary>Tek bir günlük satırının gövdeye girecek en fazla uzunluğu.</summary>
    /// <remarks>
    /// Satır SAYISINI sınırlamak tek başına yetmiyor: tek bir satır da çok uzun
    /// olabiliyor (tam komut satırı, uzun bir istisna metni). Satır başına sınır
    /// olmadan "adres sınırın altında kalır" diye bir güvence veremiyoruz.
    /// </remarks>
    public const int MaxLogLineLength = 200;

    /// <summary>
    /// Bir günlük satırının kullanıcının kendi girdiği veriyi taşıyıp
    /// taşımadığını söyler.
    /// </summary>
    /// <remarks>
    /// "Kendi hedefiniz" satırı kullanıcının arayüze YAZDIĞI adresi içeriyor.
    /// Denenen parametreler ve varsayılan hedefler bizim listemiz, onların
    /// paylaşılmasında sakınca yok; ama kullanıcının kendi yazdığı adres
    /// bize ait değil ve bir forma kendiliğinden düşmemeli.
    /// </remarks>
    public static bool IsUserSupplied(string logLine)
        => logLine.Contains("Kendi hedefiniz", StringComparison.OrdinalIgnoreCase);

    /// <summary>Konu başlığını kurar.</summary>
    /// <remarks>
    /// Ayraç "·", uzun tire değil: durum metinlerinin KENDİSİ uzun tire
    /// içeriyor ("ÇALIŞIYOR — AMA AÇMIYOR") ve ayraç da uzun tire olunca başlık
    /// üç parçalı mı dört parçalı mı belli olmuyordu. Gerçek çıktıda görüldü.
    ///
    /// Sürüm başta: konu listesi tarandığında ilk sorulan şey "bu hangi sürüm".
    ///
    /// Sürümün "+sha" kuyruğu ATILIYOR. Gerçek koşumda başlık şöyle çıktı:
    /// "[hata] v0.1.18+a8a66ca13a4b2675ea2f8b6fe217a3ba9fd3bad8 · ..."; 93
    /// karakterin yarısı tek bir yapının karmasıydı ve konu listesinde okunacak
    /// hiçbir şey bırakmıyordu. Karma yine de kaybolmuyor: ortam
    /// tablosunda tam hâliyle duruyor, yani "hangi yapı" sorusu cevapsız kalmıyor.
    /// </remarks>
    public static string BuildTitle(IssueDetails details)
        => $"[hata] v{Bos(details.AppVersion, "?").Split('+')[0]} · {Bos(details.Isp, "ISS seçilmemiş")} · {Bos(details.Status, "durum yok")}";

    /// <summary>Konu gövdesini kurar.</summary>
    /// <param name="logLineBudget">
    /// Gövdeye girecek günlük satırı sayısı. <see cref="BuildUrl"/> adres
    /// sınırına sığana kadar bunu küçültüyor.
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

    /// <summary>Doldurulmuş konu formunun adresini kurar.</summary>
    /// <remarks>
    /// Adres <see cref="MaxUrlLength"/>'i aşarsa günlük satırı sayısı
    /// azaltılarak yeniden deneniyor. Sıfır satırla bile sığmıyorsa (ki
    /// ortam tablosu sabit boyutta olduğu için olmamalı) yine de o adres
    /// dönüyor: eksik bir bildirim, hiç bildirim olmamasından iyi.
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

        // Döngü bütçe == 0'da mutlaka dönüyor; derleyiciyi memnun etmek için.
        return NewIssuePage;
    }

    private static string Kisalt(string satir)
        => satir.Length <= MaxLogLineLength
            ? satir
            : satir[..MaxLogLineLength] + " …(kesildi)";

    private static string Bos(string? deger, string yedek)
        => string.IsNullOrWhiteSpace(deger) ? yedek : deger.Trim();

    // Tablo hücresinde dikey çizgi sütunu kırıyor; parametrelerde "|" geçmiyor
    // ama geçerse tabloyu bozmasın.
    private static string Hucre(string? deger)
        => Bos(deger, "-").Replace("|", @"\|", StringComparison.Ordinal);
}

/// <summary>Hata bildirimine girecek ortam özeti.</summary>
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
