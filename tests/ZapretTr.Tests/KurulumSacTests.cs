using System.IO;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Kurulumun, imzasız kendi exe'mize bağımlı kalmaması.
/// </summary>
/// <remarks>
/// ÖLÇÜLDÜ (gerçek makine, 2026-09-16, 0.2.3 kurulumu): Akıllı Uygulama Denetimi
/// açıkken Windows ZapretTR.exe'yi çalıştırmıyor; hata 4551, "An Application
/// Control policy has blocked this file". İki sonucu oldu:
///
///   * <c>PrepareToInstall</c> servisleri sökmek için önceki sürümün exe'sini
///     çağırıyordu ve Exec'in sonucunu YOK SAYIYORDU. Çağrı başarısız oluyor,
///     servisler sökülmüyor, kimse fark etmiyor. Kullanıcının bildirimi:
///     "güncelleme sırasında servisleri kapamıyor".
///   * [Run] girdileri ham CreateProcess hatasını kullanıcının yüzüne veriyordu:
///     "CreateProcess tamamlanamadı; kod 4551". Ne olduğunu da ne yapması
///     gerektiğini de söylemeyen bir kutu, hem de kurulumun ortasında.
///
/// Kural: kurulum, İMZALI araçlarla (sc.exe) ayakta kalabilmeli. İmzasız exe'yi
/// çağırmak "en iyi ihtimal" olabilir ama tek yol OLAMAZ.
///
/// Bu testler metin üzerinde çalışıyor. ISCC her makinede kurulu değil,
/// dolayısıyla derleme hatasını CI yakalar; buradaki testler paketi üretmeden,
/// her yerde koşuyor. AppIconTests'teki gerekçenin aynısı.
/// </remarks>
public sealed class KurulumSacTests
{
    private static string Iss => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "installer", "setup.iss"));

    /// <summary>[Code] bölümü, yalnızca yorumlar atılmış. İçerik kontrolleri için.</summary>
    private static string Kod
    {
        get
        {
            var src = Iss;
            // Satır başındaki bölüm başlığı; yorumlarda geçen "[Code]" değil.
            var govde = src[src.IndexOf("\n[Code]", StringComparison.Ordinal)..];
            return string.Join('\n', govde.Split('\n')
                .Select(l => Regex.Replace(l, @"//.*$", string.Empty)));
        }
    }

    /// <summary>
    /// <see cref="Kod"/>, string sabitleri de boşaltılmış. YAPISAL kontroller için.
    /// </summary>
    /// <remarks>
    /// Ayrı olması şart: <c>'--register-dns-guard'</c> bir string sabiti, dolayısıyla
    /// içerik kontrolü bu sürümde yapılırsa her zaman bulunamaz. (Bu testi yazarken
    /// tam olarak o oldu.)
    /// </remarks>
    private static string KodYapisal => Regex.Replace(Kod, @"'(?:[^']|'')*'", "''");

    // --- Servis sökme imzasız exe'ye bağlı olmamalı -------------------------------

    [Fact]
    public void Onceki_Exe_Cagrisinin_Sonucu_Denetleniyor()
    {
        // Sonuç yok sayılırsa SAC engeli sessiz kalır ve servisler ayakta kalır.
        Assert.Matches(
            new Regex(@"Exec\(OncekiExe,\s*'--uninstall-services'[^)]*\)\s*\r?\n?\s*and\s*\(ResultCode\s*=\s*0\)",
                      RegexOptions.IgnoreCase),
            Kod);
    }

    [Fact]
    public void Exe_Calismazsa_Servisler_Sc_Ile_Sokuluyor()
    {
        // sc.exe Microsoft imzalı; SAC onu engellemiyor. Yedek yol BU olmalı.
        var kod = Kod;

        Assert.Contains("not TemizlikYapildi", kod, StringComparison.Ordinal);

        foreach (var komut in new[] { "stop ZapretTR", "delete ZapretTR",
                                      "stop ZapretTR-DNS", "delete ZapretTR-DNS" })
        {
            Assert.Contains(komut, Iss, StringComparison.Ordinal);
        }
    }

    // --- Ham CreateProcess hatası kullanıcıya gösterilmemeli ----------------------

    [Fact]
    public void Run_Bolumu_Exeyi_Sessizce_Cagirmiyor()
    {
        // [Run] başarısız olursa Inno ham hatayı gösteriyor ve biz araya giremiyoruz.
        // Bu iki çağrı [Code]'a taşındı; geri gelirlerse kullanıcı yine 4551 kutusunu
        // kurulumun ortasında görür.
        var src = Iss;
        var run = src[src.IndexOf("[Run]", StringComparison.Ordinal)..
                      src.IndexOf("[UninstallRun]", StringComparison.Ordinal)];

        Assert.DoesNotContain("--register-dns-guard", run, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameters: \"--dns-guard\"", run, StringComparison.Ordinal);
    }

    [Fact]
    public void Bekci_Kaydi_KAYBOLMADI()
    {
        // [Run]'dan çıkarıldı diye özellik de gitmemeli: görev her kurulumda
        // yeniden yazılmalı ki YENİ exe'yi göstersin.
        var kod = Kod;

        Assert.Contains("--register-dns-guard", kod, StringComparison.Ordinal);
        Assert.Contains("--dns-guard", kod, StringComparison.Ordinal);
    }

    [Fact]
    public void Bekci_Servis_Kurulu_Olmasa_Da_Yaziliyor()
    {
        // Eskiden CurStepChanged "not ServisGeriKurulacak" ile de çıkıyordu; bekçi
        // ayrı ([Run]) koştuğu için sorun değildi. İkisi birleştiğine göre o koşul
        // geri gelirse bekçi, servis kullanmayan kullanıcılarda SESSİZCE kurulmaz.
        Assert.DoesNotContain("(CurStep <> ssPostInstall) or (not ServisGeriKurulacak)",
                              Kod, StringComparison.Ordinal);
    }

    [Fact]
    public void Engellenince_Ne_Yapilacagi_SIRASIYLA_Yaziyor()
    {
        // İki adım da gerekli ve ikincisini gerçek kullanıcı bulup bildirdi:
        // yalnızca SAC'yi kapatmak YETMEDİ, indirilen dosyanın "Engellemeyi Kaldır"
        // işaretinin de temizlenmesi gerekti. Biri eksik kalırsa kullanıcı
        // "yaptım, yine olmadı" noktasına geri döner.
        var src = Iss;

        Assert.Contains("Engellemeyi Kaldir", src, StringComparison.Ordinal);
        Assert.Contains("Akilli Uygulama Denetimi", src, StringComparison.Ordinal);
        Assert.Contains("4551", src, StringComparison.Ordinal);
    }

    // --- SAC açıksa kurmadan ÖNCE söyleniyor --------------------------------------

    [Fact]
    public void Sac_Acikken_Kurulum_Basinda_Uyariyor()
    {
        // ÖLÇÜLDÜ (2026-09-16): SAC açık makinede 0.2.5 taslağı kuruldu ve uyarı
        // kurulumun SONUNDA geldi.
        var kod = Kod;
        var init = kod[kod.IndexOf("function InitializeSetup", StringComparison.Ordinal)..];
        init = init[..init.IndexOf("Exec(", StringComparison.Ordinal)];

        Assert.Contains("SacAcik()", init, StringComparison.Ordinal);
        Assert.Contains("VerifiedAndReputablePolicyState", kod, StringComparison.Ordinal);
    }

    [Fact]
    public void Sac_Uyarisi_Sessiz_Kurulumu_Durdurmuyor()
    {
        // CI /VERYSILENT /SUPPRESSMSGBOXES ile kuruyor. Düz MsgBox sessiz kurulumda
        // da soru sorar; SuppressibleMsgBox varsayılan cevabı (IDYES) kullanır.
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
        // "ZapretTR kurulu" yanıltıcı, "ZapretTR acilmaz" ise ölçülerek YANLIŞ çıktı
        // (SAC açık makinede pencere sonradan açıldı). Ölçülen: düzgün çalışmıyor.
        Assert.DoesNotContain("'ZapretTR kurulu, ama", Iss, StringComparison.Ordinal);
        Assert.DoesNotContain("ZapretTR acilmaz", Iss, StringComparison.Ordinal);
        Assert.Contains("ZapretTR duzgun calismaz", Iss, StringComparison.Ordinal);
    }

    [Fact]
    public void Exe_Engellendiyse_Simdi_Baslat_Denenmiyor()
    {
        // ÖLÇÜLDÜ (2026-09-16): açıklama kutusundan sonra "şimdi başlat" girdisi ham
        // "ShellExecuteEx failed; code 4551" kutusunu yine gösterdi.
        var src = Iss;
        var run = src[src.IndexOf("[Run]", StringComparison.Ordinal)..
                      src.IndexOf("[UninstallRun]", StringComparison.Ordinal)];
        var girdi = run.Split('\n').Single(l => l.StartsWith("Filename:", StringComparison.Ordinal) && l.Contains("postinstall", StringComparison.Ordinal));

        Assert.Contains("Check: ExeCalisabildi", girdi, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"function ExeCalisabildi\(\): Boolean;\s*begin\s*Result := not Engellendi;"), Kod);
    }

    // --- Yapısal sağlık ----------------------------------------------------------

    [Fact]
    public void Pascal_Bloklari_Dengeli()
    {
        // ISCC her yerde yok; en olası Pascal hatasını (eksik/fazla end) burada
        // yakalıyoruz. Derlemenin yerine geçmez, ucuz bir ön kontrol.
        var kod = KodYapisal;
        var begin = Regex.Matches(kod, @"\bbegin\b", RegexOptions.IgnoreCase).Count;
        var end = Regex.Matches(kod, @"\bend\b", RegexOptions.IgnoreCase).Count;

        Assert.Equal(begin, end);
    }

    [Fact]
    public void Kod_Icinde_Satir_Basi_Diyez_YOK()
    {
        // ÖLÇÜLDÜ: ISCC'nin önişlemcisi satır başındaki '#' karakterini yönerge
        // sayıyor. Çok satırlı bir MsgBox'ta devam satırı '#13#10' ile başlayınca
        // derleme "Unknown preprocessor directive" ile DURDU (setup.iss:399).
        //
        // Bu tam da metin testlerinin göremediği sınıftı: begin/end dengeliydi,
        // değişkenler bildirilmişti, içerik doğruydu; ama dosya derlenmiyordu.
        // Testi, gerçek derleme hatayı bulduktan SONRA yazdım; derlemenin yerine
        // geçmez, yalnızca aynı hatanın sessizce geri gelmesini engeller.
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
        // Pascal'da bildirilmemiş değişken derleme hatası; ISCC olmadan bunu
        // yakalayan tek şey bu.
        var kod = KodYapisal;

        foreach (var ad in new[] { "TemizlikYapildi", "Engellendi", "Exe", "Sc", "OncekiExe", "Mesaj", "Durum" })
        {
            Assert.Matches(new Regex($@"^\s*{ad}\s*:\s*\w+\s*;", RegexOptions.Multiline), kod);
        }
    }
}
