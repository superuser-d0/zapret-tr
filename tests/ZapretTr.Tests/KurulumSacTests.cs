using System.IO;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Kurulumun, imzasiz kendi exe'mize bagimli kalmamasi.
/// </summary>
/// <remarks>
/// OLCULDU (gercek makine, 2026-09-16, 0.2.3 kurulumu): Akilli Uygulama Denetimi
/// acikken Windows ZapretTR.exe'yi calistirmiyor -- hata 4551, "An Application
/// Control policy has blocked this file". Iki sonucu oldu:
///
///   * <c>PrepareToInstall</c> servisleri sokmek icin onceki surumun exe'sini
///     cagiriyordu ve Exec'in sonucunu YOK SAYIYORDU. Cagri basarisiz oluyor,
///     servisler sokulmuyor, kimse fark etmiyor. Kullanicinin bildirimi:
///     "guncelleme sirasinda servisleri kapamiyor".
///   * [Run] girdileri ham CreateProcess hatasini kullanicinin yuzune veriyordu:
///     "CreateProcess tamamlanamadi; kod 4551" -- ne oldugunu da ne yapmasi
///     gerektigini de soylemeyen bir kutu, hem de kurulumun ortasinda.
///
/// Kural: kurulum, IMZALI araclarla (sc.exe) ayakta kalabilmeli. Imzasiz exe'yi
/// cagirmak "en iyi ihtimal" olabilir ama tek yol OLAMAZ.
///
/// Bu testler metin uzerinde calisiyor. ISCC yalnizca CI'da var (yerelde Inno
/// Setup kurulu degil), dolayisiyla derleme hatasini CI yakalar; buradaki
/// testler paketi uretmeden, yerelde de kosuyor -- AppIconTests'teki gerekcenin
/// aynisi.
/// </remarks>
public sealed class KurulumSacTests
{
    private static string Iss => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "installer", "setup.iss"));

    /// <summary>[Code] bolumu, yalnizca yorumlar atilmis. Icerik kontrolleri icin.</summary>
    private static string Kod
    {
        get
        {
            var src = Iss;
            var govde = src[src.IndexOf("[Code]", StringComparison.Ordinal)..];
            return string.Join('\n', govde.Split('\n')
                .Select(l => Regex.Replace(l, @"//.*$", string.Empty)));
        }
    }

    /// <summary>
    /// <see cref="Kod"/>, string sabitleri de bosaltilmis. YAPISAL kontroller icin.
    /// </summary>
    /// <remarks>
    /// Ayri olmasi sart: <c>'--register-dns-guard'</c> bir string sabiti, dolayisiyla
    /// icerik kontrolu bu surumde yapilirsa her zaman bulunamaz. (Bu testi yazarken
    /// tam olarak o oldu.)
    /// </remarks>
    private static string KodYapisal => Regex.Replace(Kod, @"'(?:[^']|'')*'", "''");

    // --- Servis sokme imzasiz exe'ye bagli olmamali -------------------------------

    [Fact]
    public void Onceki_Exe_Cagrisinin_Sonucu_Denetleniyor()
    {
        // Sonuc yok sayilirsa SAC engeli sessiz kalir ve servisler ayakta kalir.
        Assert.Matches(
            new Regex(@"Exec\(OncekiExe,\s*'--uninstall-services'[^)]*\)\s*\r?\n?\s*and\s*\(ResultCode\s*=\s*0\)",
                      RegexOptions.IgnoreCase),
            Kod);
    }

    [Fact]
    public void Exe_Calismazsa_Servisler_Sc_Ile_Sokuluyor()
    {
        // sc.exe Microsoft imzali; SAC onu engellemiyor. Yedek yol BU olmali.
        var kod = Kod;

        Assert.Contains("not TemizlikYapildi", kod, StringComparison.Ordinal);

        foreach (var komut in new[] { "stop ZapretTR", "delete ZapretTR",
                                      "stop ZapretTR-DNS", "delete ZapretTR-DNS" })
        {
            Assert.Contains(komut, Iss, StringComparison.Ordinal);
        }
    }

    // --- Ham CreateProcess hatasi kullaniciya gosterilmemeli ----------------------

    [Fact]
    public void Run_Bolumu_Exeyi_Sessizce_Cagirmiyor()
    {
        // [Run] basarisiz olursa Inno ham hatayi gosteriyor ve biz araya giremiyoruz.
        // Bu iki cagri [Code]'a tasindi; geri gelirlerse kullanici yine 4551 kutusunu
        // kurulumun ortasinda gorur.
        var src = Iss;
        var run = src[src.IndexOf("[Run]", StringComparison.Ordinal)..
                      src.IndexOf("[UninstallRun]", StringComparison.Ordinal)];

        Assert.DoesNotContain("--register-dns-guard", run, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameters: \"--dns-guard\"", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Bekci_Kaydi_KAYBOLMADI()
    {
        // [Run]'dan cikarildi diye ozellik de gitmemeli: gorev her kurulumda
        // yeniden yazilmali ki YENI exe'yi gostersin.
        var kod = Kod;

        Assert.Contains("--register-dns-guard", kod, StringComparison.Ordinal);
        Assert.Contains("--dns-guard", kod, StringComparison.Ordinal);
    }

    [Fact]
    public void Bekci_Servis_Kurulu_Olmasa_Da_Yaziliyor()
    {
        // Eskiden CurStepChanged "not ServisGeriKurulacak" ile de cikiyordu; bekci
        // ayri ([Run]) kostugu icin sorun degildi. Ikisi birlestigine gore o kosul
        // geri gelirse bekci, servis kullanmayan kullanicilarda SESSIZCE kurulmaz.
        Assert.DoesNotContain("(CurStep <> ssPostInstall) or (not ServisGeriKurulacak)",
                              Kod, StringComparison.Ordinal);
    }

    [Fact]
    public void Engellenince_Ne_Yapilacagi_SIRASIYLA_Yaziyor()
    {
        // Iki adim da gerekli ve ikincisini gercek kullanici bulup bildirdi:
        // yalnizca SAC'i kapatmak YETMEDI, indirilen dosyanin "Engellemeyi Kaldir"
        // isaretinin de temizlenmesi gerekti. Biri eksik kalirsa kullanici
        // "yaptim, yine olmadi" noktasina geri doner.
        var src = Iss;

        Assert.Contains("Engellemeyi Kaldir", src, StringComparison.Ordinal);
        Assert.Contains("Akilli Uygulama Denetimi", src, StringComparison.Ordinal);
        Assert.Contains("4551", src, StringComparison.Ordinal);
    }

    // --- SAC aciksa kurmadan ONCE soyleniyor --------------------------------------

    [Fact]
    public void Sac_Acikken_Kurulum_Basinda_Uyariyor()
    {
        // OLCULDU (2026-09-16): SAC acik makinede 0.2.5 taslagi kuruldu, uyari
        // kurulum SONUNDA geldi ve uygulama hic acilmadi.
        var kod = Kod;
        var init = kod[kod.IndexOf("function InitializeSetup", StringComparison.Ordinal)..];
        init = init[..init.IndexOf("Exec(", StringComparison.Ordinal)];

        Assert.Contains("SacAcik()", init, StringComparison.Ordinal);
        Assert.Contains("VerifiedAndReputablePolicyState", kod, StringComparison.Ordinal);
    }

    [Fact]
    public void Sac_Uyarisi_Sessiz_Kurulumu_Durdurmuyor()
    {
        // CI /VERYSILENT /SUPPRESSMSGBOXES ile kuruyor. Duz MsgBox sessiz kurulumda
        // da soru sorar; SuppressibleMsgBox varsayilan cevabi (IDYES) kullanir.
        var kod = Kod;
        var init = kod[kod.IndexOf("function InitializeSetup", StringComparison.Ordinal)..];
        init = init[..init.IndexOf("Exec(", StringComparison.Ordinal)];

        Assert.Contains("SuppressibleMsgBox(", init, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"SuppressibleMsgBox\([^;]*,\s*IDYES\)"), init);
        Assert.DoesNotMatch(new Regex(@"(?<!Suppressible)MsgBox\("), init);
    }

    [Fact]
    public void Engellenince_Uygulamanin_Acilmayacagi_Soyleniyor()
    {
        // "ZapretTR kurulu" yazmak dogruydu ama kullanici uygulamayi acabilecegini sandi.
        Assert.DoesNotContain("'ZapretTR kurulu, ama", Iss, StringComparison.Ordinal);
        Assert.Contains("ZapretTR acilmaz", Iss, StringComparison.Ordinal);
    }

    // --- Yapisal saglik ----------------------------------------------------------

    [Fact]
    public void Pascal_Bloklari_Dengeli()
    {
        // ISCC yerelde yok; en olasi Pascal hatasini (eksik/fazla end) burada
        // yakaliyoruz. Derlemenin yerine gecmez, ucuz bir on kontrol.
        var kod = KodYapisal;
        var begin = Regex.Matches(kod, @"\bbegin\b", RegexOptions.IgnoreCase).Count;
        var end = Regex.Matches(kod, @"\bend\b", RegexOptions.IgnoreCase).Count;

        Assert.Equal(begin, end);
    }

    [Fact]
    public void Kod_Icinde_Satir_Basi_Diyez_YOK()
    {
        // OLCULDU: ISCC'nin onislemcisi satir basindaki '#' karakterini yonerge
        // sayiyor. Cok satirli bir MsgBox'ta devam satiri '#13#10' ile baslayinca
        // derleme "Unknown preprocessor directive" ile DURDU (setup.iss:399).
        //
        // Bu tam da metin testlerinin goremedigi sinifti: begin/end dengeliydi,
        // degiskenler bildirilmisti, icerik dogruydu -- ama dosya derlenmiyordu.
        // Testi, gercek derleme hatayi bulduktan SONRA yazdim; derlemenin yerine
        // gecmez, yalnizca ayni hatanin sessizce geri gelmesini engeller.
        var src = Iss;
        var kodBasi = src.IndexOf("[Code]", StringComparison.Ordinal);

        var suclular = src[kodBasi..]
            .Split('\n')
            .Select((satir, i) => (satir: satir.TrimEnd('\r'), no: i))
            .Where(x => x.satir.TrimStart().StartsWith('#'))
            .Select(x => $"[Code]+{x.no}: {x.satir.Trim()}")
            .ToList();

        Assert.Empty(suclular);
    }

    [Fact]
    public void Kullanilan_Degiskenler_Bildirilmis()
    {
        // Pascal'da bildirilmemis degisken derleme hatasi; ISCC olmadan bunu
        // yakalayan tek sey bu.
        var kod = KodYapisal;

        foreach (var ad in new[] { "TemizlikYapildi", "Engellendi", "Exe", "Sc", "OncekiExe", "Mesaj", "Durum" })
        {
            Assert.Matches(new Regex($@"^\s*{ad}\s*:\s*\w+\s*;", RegexOptions.Multiline), kod);
        }
    }
}
