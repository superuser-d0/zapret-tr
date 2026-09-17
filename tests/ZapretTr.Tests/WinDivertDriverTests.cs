using ZapretTr.App.ViewModels;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// winws'in sürücü hatasını tanımak ve takılı sürücüyü boşaltmaya karar vermek.
/// </summary>
/// <remarks>
/// Sahadan gelen bildirim: "yeni sürümü indirip parametre testi yaptım, motor
/// çalışmıyor; yeniden başlatınca açılıyor." Sürücü hatası TANINMAZSA motor
/// kurtarma denemeden "başlar başlamaz N koduyla kapandı" deyip düşer ve tek çıkış
/// yeniden başlatma olur. Yanlış tanınırsa (her erken ölüm sürücü hatası sayılırsa)
/// "aynı filtreyle zaten çalışıyor" gibi bambaşka bir durumda gereksiz yere 20 sn
/// boşaltma beklenir.
///
/// Örnek satırlar winws.exe ikilisinden çıkarılan biçim dizgilerinden kuruldu
/// ("windivert: error opening filter: %s", "win_dark_init failed. win32 error %u
/// (0x%08X)"). DEVAM'ın kuralı: seçeneği ve çıktıyı sürüm numarasına değil
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
        // En sık erken ölüm bu ve sürücüyle ilgisi yok: boşaltmak hem işe
        // yaramaz hem de çalışan servisin sürücüsünü durdurmaya kalkar.
        string[] satirlar =
        [
            "github version v72.12 (5cc46a9815b00e97401b1459984dff44abfec411)",
            "A copy of winws is already running with the same filter",
        ];

        Assert.False(WinDivertDriver.IsOpenFailure(satirlar));
    }

    [Theory]
    [InlineData(577)]   // imza reddi: boşaltmak geçirmez
    [InlineData(1275)]  // güvenlik yazılımı engeli
    [InlineData(1753)]  // BFE servisi kapalı
    [InlineData(5)]     // yetki
    [InlineData(2)]     // dosya yok
    public void BosaltmanınDuzeltemeyecegiHatalar_Denenmez(int kod)
    {
        Assert.False(WinDivertDriver.IsRecoverable(kod));
    }

    [Theory]
    [InlineData(654)]
    [InlineData(1072)]
    [InlineData(null)]  // kod yazılmadıysa en olası sebep takılı sürücü
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
        // STOP_PENDING'i "durdu" saymak tam da takılı kalmış sürücüyü gözden
        // kaçırmak olurdu. (Ayrı bir dışlama gerekmiyor: "STOP_PENDING"
        // "STOPPED" dizgisini içermiyor; ilk yazımda gereksiz bir koşul vardı
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
