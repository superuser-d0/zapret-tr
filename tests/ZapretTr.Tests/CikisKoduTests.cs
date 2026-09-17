using System.IO;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Her komut satırı yolunun sonucunu çıkış koduyla BİLDİRMESİ.
/// </summary>
/// <remarks>
/// Kurulum paketi ve CI bu yolları çıkış koduyla denetliyor. Bir yol kod
/// döndürmezse çağıran taraf her zaman 0 görür, yani başarısızlık görünmez olur.
/// En pahalısı <c>--uninstall-services</c>: başarısız olursa kullanıcının sistem
/// DNS'i 127.0.0.1'de, çözümleyicisiz kalabilir; projenin en kötü senaryosu.
/// O yol <c>void</c> olduğu için tam da bunu yapıyordu.
///
/// NE ÖLÇÜLDÜ, NE ÖLÇÜLMEDİ. Çıkış kodunun bu yapıda (StartupUri yok, pencere yok,
/// karar OnStartup'ta, üretilen Main void) doğru döndüğü tek kullanımlık bir WPF
/// uygulamasıyla ölçüldü: <c>Shutdown(42)</c> 42, <c>Environment.ExitCode = 43;
/// Shutdown();</c> 43. İKİSİ DE ÇALIŞIYOR; <c>Shutdown(kod)</c> yalnızca biçim
/// tercihi ve bu testler o tercihi DAYATMIYOR. Dayatılan şey, kod döndüren bir
/// yolun sonucunun yutulmaması.
/// </remarks>
public sealed class CikisKoduTests
{
    private static string AppXamlCs => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "App.xaml.cs"));

    // Çıkış kodu döndüren komut satırı yolları.
    [Theory]
    [InlineData("RunServiceCleanup")]
    [InlineData("RunServiceInstall")]
    [InlineData("RunDnsGuard")]
    [InlineData("RunRegisterDnsGuard")]
    [InlineData("RunUnregisterDnsGuard")]
    public void Yol_Kod_Donduruyor(string yol)
    {
        // int dönmeyen bir yol, çağırana hiçbir şey söyleyemez.
        Assert.Matches(
            new Regex($@"private\s+static\s+int\s+{yol}\s*\("),
            AppXamlCs);
    }

    [Theory]
    [InlineData("RunServiceCleanup")]
    [InlineData("RunServiceInstall")]
    [InlineData("RunDnsGuard")]
    [InlineData("RunRegisterDnsGuard")]
    [InlineData("RunUnregisterDnsGuard")]
    public void Yolun_Sonucu_Yutulmuyor(string yol)
    {
        // Çağrı ya Shutdown'a parametre olarak ya da Environment.ExitCode'a
        // gitmeli. "RunX();" tek başına çağrılırsa dönen kod sessizce kaybolur;
        // RunServiceCleanup void iken aynen böyleydi.
        var kaynak = AppXamlCs;

        var yutuldu = Regex.IsMatch(kaynak, $@"^\s*{yol}\(\)\s*;", RegexOptions.Multiline);
        Assert.False(yutuldu, $"{yol}() sonucu kullanilmadan cagriliyor; cikis kodu kayboluyor.");

        var kullanildi = kaynak.Contains($"Shutdown({yol}())", StringComparison.Ordinal)
                         || Regex.IsMatch(kaynak, $@"Environment\.ExitCode\s*=\s*{yol}\(\)");
        Assert.True(kullanildi, $"{yol}() sonucu bir cikis koduna baglanmamis.");
    }

    [Fact]
    public void Parametresiz_Shutdown_Yalnizca_Ikinci_Ornek_Yolunda()
    {
        // Geriye İKİ meşru parametresiz Shutdown() kalıyor ve ikisi de "ikinci
        // örnek" yolunda: pencere öne getirildi ya da "zaten çalışıyor" mesajı
        // gösterildi. Orada doğru cevap zaten 0.
        //
        // Sayı artarsa yeni bir komut satırı yolu çıkış kodunu kaybediyor olabilir.
        // Bu testi ilk yazdığımda sınırı 2 sanmıştım ve 3 çıktı; üçüncüsü
        // --uninstall-services yoluydu ve gerçekten kod döndürmüyordu.
        //
        // YORUM SATIRLARI ELENİYOR: bir XML belge yorumunda geçen örnek kod
        // ("Environment.ExitCode = 43; Shutdown();") sayıma giriyor ve testi
        // haksız yere düşürüyordu. Önce bu regex'e bir lookbehind koymuştum;
        // yetmedi, çünkü o yalnızca önceki iki karaktere bakıyor.
        var kodSatirlari = AppXamlCs
            .Split('\n')
            .Where(satir => !satir.TrimStart().StartsWith("//", StringComparison.Ordinal));

        var parametresiz = Regex.Matches(
            string.Join('\n', kodSatirlari), @"\bShutdown\(\)\s*;").Count;

        Assert.Equal(2, parametresiz);
    }
}
