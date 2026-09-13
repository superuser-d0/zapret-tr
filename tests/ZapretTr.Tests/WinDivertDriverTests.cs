using ZapretTr.App.ViewModels;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// winws'in surucu hatasini tanimak ve takili surucuyu bosaltmaya karar vermek.
/// </summary>
/// <remarks>
/// Sahadan gelen bildirim: "yeni surumu indirip parametre testi yaptim, motor
/// calismiyor; yeniden baslatinca aciliyor." Surucu hatasi TANINMAZSA motor
/// kurtarma denemeden "baslar baslamaz N koduyla kapandi" deyip duser ve tek cikis
/// yeniden baslatma olur. Yanlis taninirsa (her erken olum surucu hatasi sayilirsa)
/// "ayni filtreyle zaten calisiyor" gibi bambaska bir durumda gereksiz yere 20 sn
/// bosaltma beklenir.
///
/// Ornek satirlar winws.exe ikilisinden cikarilan bicim dizgilerinden kuruldu
/// ("windivert: error opening filter: %s", "win_dark_init failed. win32 error %u
/// (0x%08X)") -- DEVAM'in kurali: secenegi ve ciktiyi surum numarasina degil
/// ikiliye sor.
/// </remarks>
public sealed class WinDivertDriverTests
{
    [Fact]
    public void SurucuAcmaHatasi_Taniniyor_ve_KoduOkunuyor()
    {
        string[] satirlar =
        [
            "github version v72.12 (5cc46a9815b00e97401b1459984dff44abfec411)",
            "windivert: error opening filter: The driver could not be loaded because a previous version of the driver is still in memory.",
            "win_dark_init failed. win32 error 654 (0x0000028E)",
        ];

        Assert.True(WinDivertDriver.IsOpenFailure(satirlar));
        Assert.Equal(654, WinDivertDriver.ParseWin32Error(satirlar));
        Assert.True(WinDivertDriver.IsRecoverable(654));
    }

    [Fact]
    public void AyniFiltreyleZatenCalisiyor_SurucuHatasiSayilmaz()
    {
        // En sik erken olum bu ve surucuyle ilgisi yok: bosaltmak hem ise
        // yaramaz hem de calisan servisin surucusunu durdurmaya kalkar.
        string[] satirlar =
        [
            "github version v72.12 (5cc46a9815b00e97401b1459984dff44abfec411)",
            "A copy of winws is already running with the same filter",
        ];

        Assert.False(WinDivertDriver.IsOpenFailure(satirlar));
    }

    [Theory]
    [InlineData(577)]   // imza reddi: bosaltmak gecirmez
    [InlineData(1275)]  // guvenlik yazilimi engeli
    [InlineData(1753)]  // BFE servisi kapali
    [InlineData(5)]     // yetki
    [InlineData(2)]     // dosya yok
    public void BosaltmanınDuzeltemeyecegiHatalar_Denenmez(int kod)
    {
        Assert.False(WinDivertDriver.IsRecoverable(kod));
    }

    [Theory]
    [InlineData(654)]
    [InlineData(1072)]
    [InlineData(null)]  // kod yazilmadiysa en olasi sebep takili surucu
    public void TakiliSurucuHatalari_Denenir(int? kod)
    {
        Assert.True(WinDivertDriver.IsRecoverable(kod));
    }

    [Fact]
    public void KoduOlmayanSatir_KodUydurmaz()
    {
        Assert.Null(WinDivertDriver.ParseWin32Error(["windivert: error opening filter: Access is denied."]));
    }

    [Theory]
    [InlineData("[SC] EnumQueryServicesStatus:OpenService FAILED 1060:\r\n\r\nThe specified service does not exist as an installed service.", true)]
    [InlineData("SERVICE_NAME: windivert\r\n        TYPE               : 1  KERNEL_DRIVER\r\n        STATE              : 1  STOPPED", true)]
    [InlineData("SERVICE_NAME: windivert\r\n        TYPE               : 1  KERNEL_DRIVER\r\n        STATE              : 4  RUNNING", false)]
    [InlineData("SERVICE_NAME: windivert\r\n        TYPE               : 1  KERNEL_DRIVER\r\n        STATE              : 3  STOP_PENDING", false)]
    public void SurucuDurumu_TakiliKalmayiAyiriyor(string scQuery, bool dustu)
    {
        // STOP_PENDING'i "durdu" saymak tam da takili kalmis surucuyu gozden
        // kacirmak olurdu. (Ayri bir dislama gerekmiyor: "STOP_PENDING"
        // "STOPPED" dizgisini icermiyor -- ilk yazimda gereksiz bir kosul vardi
        // ve mutasyon onu ele verdi.)
        Assert.Equal(dustu, WinDivertDriver.IsGoneOrStopped(scQuery));
    }

    [Theory]
    [InlineData("0.1.20+5cc46a9815b00e97401b1459984dff44abfec411", "0.1.20")]
    [InlineData("0.1.20", "0.1.20")]
    public void BaslikSurumu_CommitOzetiniGostermez(string bilgi, string beklenen)
    {
        Assert.Equal(beklenen, MainViewModel.ShortVersion(bilgi));
    }
}
