using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// "Duraklat"tan sonra arkada kalanların tarifi.
/// </summary>
/// <remarks>
/// Duraklat eskiden yalnızca uygulamanın kendi winws'i ile şifreli DNS'i kapatıyordu;
/// WinDivert sürücüsü çekirdekte RUNNING kalıyordu ve arayüz yine "Duraklatıldı"
/// diyordu. Artık duraklatmadan sonra ölçülüyor ve kalan HER ŞEY yazılıyor; bir
/// tanesi bile atlanırsa kullanıcı arkada hiçbir şey kalmadığını sanar.
/// </remarks>
public sealed class PauseLeftoverTests
{
    [Fact]
    public void Hicbir_sey_kalmadiysa_liste_bos()
    {
        Assert.Empty(WinDivertCleanup.DescribeLeftovers(0, 0, [], dnsRedirected: false, serviceRunning: false));
    }

    [Fact]
    public void Cekirdekte_kalan_surucu_tek_basina_kalinti_sayilir()
    {
        // ÖLÇÜLEN HATA: süreçler kapanmış, DNS geri alınmış, ama sürücü duruyor.
        var kalan = WinDivertCleanup.DescribeLeftovers(0, 0, ["windivert"], dnsRedirected: false, serviceRunning: false);

        var satir = Assert.Single(kalan);
        Assert.Contains("windivert", satir, StringComparison.Ordinal);
    }

    [Fact]
    public void Her_kalinti_ayri_satir()
    {
        var kalan = WinDivertCleanup.DescribeLeftovers(
            winwsProcesses: 1, dnsCryptProcesses: 1, loadedDrivers: ["windivert", "WinDivert14"],
            dnsRedirected: true, serviceRunning: true);

        Assert.Equal(5, kalan.Count);
        Assert.Contains(kalan, s => s.Contains("winws", StringComparison.Ordinal));
        Assert.Contains(kalan, s => s.Contains("dnscrypt-proxy", StringComparison.Ordinal));
        Assert.Contains(kalan, s => s.Contains("WinDivert14", StringComparison.Ordinal));
        Assert.Contains(kalan, s => s.Contains("DNS", StringComparison.Ordinal));
        Assert.Contains(kalan, s => s.Contains("servis", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, 0, false, false)]
    [InlineData(0, 1, false, false)]
    [InlineData(0, 0, true, false)]
    [InlineData(0, 0, false, true)]
    public void Tek_bir_kalinti_bile_bos_liste_vermez(int winws, int dnscrypt, bool dns, bool servis)
    {
        Assert.NotEmpty(WinDivertCleanup.DescribeLeftovers(winws, dnscrypt, [], dns, servis));
    }
}
