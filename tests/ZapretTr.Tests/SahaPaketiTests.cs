using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Saha testi paketinin "rapor olustu" demesi yalnizca rapor olustuysa.
/// </summary>
/// <remarks>
/// OLCULDU (issue #1, KeremKuyucu, 2026-09-16): kullanici onay sorusunu bos gecti.
/// zapret-tr-test.exe bunu (bilerek) "hayir" saydi ve rapor yazmadan cikti, ama
/// TESTI-BASLAT.bat her durumda "Test bitti. Sonuc dosyasi: zapret-tr-rapor.json /
/// Bu klasorde olusan bu dosyayi geri gonderin" yaziyordu. Dosya yoktu. Soru da
/// "(E/h)" diye soruluyordu: buyuk harf varsayilan demek, yani Enter "evet" gibi
/// okunuyordu.
///
/// Betik davranisi burada GERCEKTEN kosuluyor: betik build-field-package.ps1'den
/// cikariliyor, yalnizca iki satiri degistiriliyor (yetki denetimi ve exe cagrisi)
/// ve cmd.exe ile calistiriliyor. Exe yerine istenen kodla cikan bir taklit var.
/// Karar mantigi -- hangi kodda ne yazildigi -- dagitilan betigin birebir aynisi.
/// </remarks>
public sealed class SahaPaketiTests
{
    private const string ExeSatiri =
        "zapret-tr-test.exe --isp auto --doh --max-candidates 25 --out \"zapret-tr-rapor.json\"";

    private static string PaketBetigi => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "tools", "build-field-package.ps1"));

    private static string BaslatmaBetigi()
    {
        var m = Regex.Match(PaketBetigi, @"\$startBat = @'\r?\n(?<govde>.*?)\r?\n'@", RegexOptions.Singleline);
        Assert.True(m.Success, "build-field-package.ps1 icinde $startBat bulunamadi.");
        return m.Groups["govde"].Value;
    }

    private static (int Kod, string Cikti) Kos(int exeKodu, bool raporYaz, bool eskiRaporVar = false)
    {
        var betik = BaslatmaBetigi();

        // Degistirilen satirlar ONCE var olmali; yoksa test sessizce baska bir
        // seyi sinar.
        Assert.Contains(ExeSatiri, betik, StringComparison.Ordinal);
        Assert.Contains("net session >nul 2>&1", betik, StringComparison.Ordinal);

        betik = betik
            .Replace("net session >nul 2>&1", "ver >nul", StringComparison.Ordinal)
            .Replace(ExeSatiri, "call \"%~dp0taklit.cmd\"", StringComparison.Ordinal);

        // build-field-package.ps1 betigi CRLF'ye cevirerek yaziyor; ayni donusum.
        betik = Regex.Replace(betik, "\r?\n", "\r\n");

        var dizin = IoPath.Combine(IoPath.GetTempPath(), "zapret-tr-saha-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dizin);
        try
        {
            File.WriteAllText(IoPath.Combine(dizin, "TESTI-BASLAT.bat"), betik, Encoding.ASCII);
            File.WriteAllText(
                IoPath.Combine(dizin, "taklit.cmd"),
                "@if \"%TAKLIT_YAZ%\"==\"1\" echo {\"yeni\":true}> zapret-tr-rapor.json\r\n@exit /b %TAKLIT_KOD%\r\n",
                Encoding.ASCII);

            if (eskiRaporVar)
            {
                File.WriteAllText(IoPath.Combine(dizin, "zapret-tr-rapor.json"), "{\"eski\":true}");
            }

            // Tam yol: bazi ortamlarda cmd mevcut dizinde aramiyor
            // (NoDefaultCurrentDirectoryInExePath).
            var psi = new ProcessStartInfo("cmd.exe", "/d /c \"" + IoPath.Combine(dizin, "TESTI-BASLAT.bat") + "\"")
            {
                WorkingDirectory = dizin,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.Environment["TAKLIT_KOD"] = exeKodu.ToString(System.Globalization.CultureInfo.InvariantCulture);
            psi.Environment["TAKLIT_YAZ"] = raporYaz ? "1" : "0";

            using var p = Process.Start(psi)!;
            p.StandardInput.Close(); // "pause" beklemesin
            var cikti = p.StandardOutput.ReadToEnd();
            Assert.True(p.WaitForExit(30000), "Betik 30 saniyede bitmedi.");
            return (p.ExitCode, cikti);
        }
        finally
        {
            try { Directory.Delete(dizin, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Onay_Bos_Gecilince_Rapor_Olustu_DEMIYOR()
    {
        // Issue #1'in birebir senaryosu: iptal (130), dosya yok.
        var (_, cikti) = Kos(exeKodu: 130, raporYaz: false);

        Assert.DoesNotContain("Test bitti", cikti, StringComparison.Ordinal);
        Assert.DoesNotContain("geri gonderin", cikti, StringComparison.Ordinal);
        Assert.Contains("rapor OLUSMADI", cikti, StringComparison.Ordinal);
        Assert.Contains("E yazin", cikti, StringComparison.Ordinal);
    }

    [Fact]
    public void Iptalde_Eski_Rapor_Bu_Teste_Aitmis_Gibi_Gosterilmiyor()
    {
        // Klasorde onceki bir testten kalan dosya varsa "dosya var" demek yetmez;
        // kullanici eski raporu yeni sanip gonderirdi.
        var (_, cikti) = Kos(exeKodu: 130, raporYaz: false, eskiRaporVar: true);

        Assert.DoesNotContain("Test bitti", cikti, StringComparison.Ordinal);
        Assert.Contains("ONCEKI", cikti, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)] // strateji bulunamadi -- rapor yine yazildi ve degerli
    public void Rapor_Yazildiysa_Gonderin_Diyor(int kod)
    {
        var (_, cikti) = Kos(exeKodu: kod, raporYaz: true);

        Assert.Contains("Test bitti", cikti, StringComparison.Ordinal);
        Assert.Contains("geri gonderin", cikti, StringComparison.Ordinal);
        Assert.DoesNotContain("OLUSMADI", cikti, StringComparison.Ordinal);
    }

    [Fact]
    public void Basari_Kodu_Gelse_Bile_Dosya_Yoksa_Olustu_Demiyor()
    {
        var (_, cikti) = Kos(exeKodu: 0, raporYaz: false);

        Assert.DoesNotContain("Test bitti", cikti, StringComparison.Ordinal);
        Assert.Contains("rapor OLUSMADI", cikti, StringComparison.Ordinal);
    }

    [Fact]
    public void Cokmede_Hata_Raporu_Gonderilmesi_Isteniyor()
    {
        var (_, cikti) = Kos(exeKodu: 5, raporYaz: true);

        Assert.Contains("hata raporu yazildi", cikti, StringComparison.Ordinal);
        Assert.Contains("geri gonderin", cikti, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Raporsuz_Kodlarda_Olusmadi_Diyor(int kod)
    {
        var (_, cikti) = Kos(exeKodu: kod, raporYaz: false);

        Assert.DoesNotContain("Test bitti", cikti, StringComparison.Ordinal);
        Assert.Contains("rapor OLUSMADI", cikti, StringComparison.Ordinal);
    }

    // --- Cift tiklama (argumansiz calistirma) -------------------------------------
    //
    // OLCULDU (2026-09-16): kullanici dogrudan zapret-tr-test.exe'ye cift tikladi;
    // hat tespiti ve sifreli DNS yoktu, rapor yazilmadi, pencere kapandi.

    [Fact]
    public void Argumansiz_Calistirma_Saha_Moduna_Gidiyor()
    {
        var kaynak = CliKaynagi;
        var yonlendirme = kaynak.IndexOf("return SahaModu.CiftTiklamaIleCalistir();", StringComparison.Ordinal);
        var ayristirma = kaynak.IndexOf("var options = CliOptions.Parse(args);", StringComparison.Ordinal);

        Assert.True(yonlendirme >= 0, "Argumansiz calistirma saha moduna yonlendirilmiyor.");
        Assert.True(yonlendirme < ayristirma, "Yonlendirme arguman ayristirmadan ONCE olmali.");
        Assert.Contains("if (args.Length == 0)", kaynak, StringComparison.Ordinal);
    }

    [Fact]
    public void Cift_Tiklama_Betikle_AYNI_Ayarlarla_Calisiyor()
    {
        // Iki giris noktasi farkli olcerse iki rapor karsilastirilamaz.
        Assert.Equal(
            ExeSatiri,
            "zapret-tr-test.exe " + string.Join(' ', SahaModu.Argumanlar.Select(a => a.Contains('.') ? $"\"{a}\"" : a)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Cift_Tiklama_Yeni_Rapor_Varsa_Gonderin_Diyor(int kod)
    {
        var mesaj = string.Join('\n', SahaModu.SonucMesaji(kod, raporYeni: true, eskiRaporVar: false));

        Assert.Contains("Test bitti", mesaj, StringComparison.Ordinal);
        Assert.Contains("geri gönderin", mesaj, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]   // basari kodu ama dosya yok
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(130)]
    public void Cift_Tiklama_Rapor_Yoksa_Gonderin_DEMIYOR(int kod)
    {
        var mesaj = string.Join('\n', SahaModu.SonucMesaji(kod, raporYeni: false, eskiRaporVar: false));

        Assert.DoesNotContain("Test bitti", mesaj, StringComparison.Ordinal);
        Assert.DoesNotContain("gönderin", mesaj, StringComparison.Ordinal);
        Assert.Contains("rapor OLUŞMADI", mesaj, StringComparison.Ordinal);
    }

    [Fact]
    public void Cift_Tiklama_Iptalde_Ne_Yapilacagini_Soyluyor()
    {
        var mesaj = string.Join('\n', SahaModu.SonucMesaji(130, raporYeni: false, eskiRaporVar: true));

        Assert.Contains("E yazın", mesaj, StringComparison.Ordinal);
        Assert.Contains("ÖNCEKİ", mesaj, StringComparison.Ordinal);
    }

    [Fact]
    public void Cift_Tiklama_Cokmede_Hata_Raporu_Isteniyor()
    {
        var mesaj = string.Join('\n', SahaModu.SonucMesaji(5, raporYeni: true, eskiRaporVar: false));

        Assert.Contains("hata raporu yazıldı", mesaj, StringComparison.Ordinal);
    }

    // --- Test araci ---------------------------------------------------------------

    private static string CliKaynagi => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.Prober.Cli", "Program.cs"));

    [Fact]
    public void Onay_Sorusu_Varsayilani_Dogru_Gosteriyor()
    {
        // Buyuk harf varsayilani gosterir; bos cevap "hayir" sayiliyor. Yorumlarda
        // eski "(E/h)" gecebilir, o yuzden kullaniciya yazilan satira bakiliyor.
        Assert.Contains("Console.Write(\"Devam edilsin mi? (e/H): \");", CliKaynagi, StringComparison.Ordinal);
        Assert.DoesNotContain("Console.Write(\"Devam edilsin mi? (E/h): \");", CliKaynagi, StringComparison.Ordinal);
    }

    [Fact]
    public void Onay_Reddedilince_Sifir_Donmuyor()
    {
        // 0, betige "rapor yazildi" der. Reddetme iptal kodunu (130) donmeli.
        var m = Regex.Match(
            CliKaynagi,
            @"if \(!accepted\)\s*\{(?<govde>.*?)\n    \}",
            RegexOptions.Singleline);

        Assert.True(m.Success, "Onay reddi dali bulunamadi.");
        Assert.Contains("return 130;", m.Groups["govde"].Value, StringComparison.Ordinal);
        Assert.DoesNotContain("return 0;", m.Groups["govde"].Value, StringComparison.Ordinal);
    }
}
