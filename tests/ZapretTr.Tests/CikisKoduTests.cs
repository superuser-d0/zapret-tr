using System.IO;
using System.Text.RegularExpressions;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Her komut satiri yolunun sonucunu cikis koduyla BILDIRMESI.
/// </summary>
/// <remarks>
/// Kurulum paketi ve CI bu yollari cikis koduyla denetliyor. Bir yol kod
/// dondurmezse cagiran taraf her zaman 0 gorur, yani basarisizlik gorunmez olur.
/// En pahalisi <c>--uninstall-services</c>: basarisiz olursa kullanicinin sistem
/// DNS'i 127.0.0.1'de, cozumleyicisiz kalabilir -- projenin en kotu senaryosu.
/// O yol <c>void</c> oldugu icin tam da bunu yapiyordu.
///
/// NE OLCULDU, NE OLCULMEDI. Cikis kodunun bu yapida (StartupUri yok, pencere yok,
/// karar OnStartup'ta, uretilen Main void) dogru donduğu tek kullanimlik bir WPF
/// uygulamasiyla olculdu: <c>Shutdown(42)</c> 42, <c>Environment.ExitCode = 43;
/// Shutdown();</c> 43. IKISI DE CALISIYOR; <c>Shutdown(kod)</c> yalnizca bicim
/// tercihi ve bu testler o tercihi DAYATMIYOR -- dayatilan sey, kod donduren bir
/// yolun sonucunun yutulmamasi.
/// </remarks>
public sealed class CikisKoduTests
{
    private static string AppXamlCs => File.ReadAllText(
        IoPath.Combine(XmlCommentTests.RepoRoot, "src", "ZapretTr.App", "App.xaml.cs"));

    // Cikis kodu donduren komut satiri yollari.
    [Theory]
    [InlineData("RunServiceCleanup")]
    [InlineData("RunServiceInstall")]
    [InlineData("RunDnsGuard")]
    [InlineData("RunRegisterDnsGuard")]
    [InlineData("RunUnregisterDnsGuard")]
    public void Yol_Kod_Donduruyor(string yol)
    {
        // int donmeyen bir yol, cagirana hicbir sey soyleyemez.
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
        // Cagri ya Shutdown'a parametre olarak ya da Environment.ExitCode'a
        // gitmeli. "RunX();" tek basina cagrilirsa donen kod sessizce kaybolur --
        // RunServiceCleanup void iken aynen boyleydi.
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
        // Geriye IKI mesru parametresiz Shutdown() kaliyor ve ikisi de "ikinci
        // ornek" yolunda: pencere one getirildi ya da "zaten calisiyor" mesaji
        // gosterildi. Orada dogru cevap zaten 0.
        //
        // Sayi artarsa yeni bir komut satiri yolu cikis kodunu kaybediyor olabilir.
        // Bu testi ilk yazdigimda siniri 2 sanmistim ve 3 cikti; ucuncusu
        // --uninstall-services yoluydu ve gercekten kod dondurmuyordu.
        //
        // YORUM SATIRLARI ELENIYOR: bir XML belge yorumunda gecen ornek kod
        // ("Environment.ExitCode = 43; Shutdown();") sayima giriyor ve testi
        // haksiz yere dusuruyordu. Once bu regex'e bir lookbehind koymustum;
        // yetmedi, cunku o yalnizca onceki iki karaktere bakiyor.
        var kodSatirlari = AppXamlCs
            .Split('\n')
            .Where(satir => !satir.TrimStart().StartsWith("//", StringComparison.Ordinal));

        var parametresiz = Regex.Matches(
            string.Join('\n', kodSatirlari), @"\bShutdown\(\)\s*;").Count;

        Assert.Equal(2, parametresiz);
    }
}
