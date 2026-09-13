using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// "Duraklat"tan sonra arkada kalanlarin tarifi.
/// </summary>
/// <remarks>
/// Duraklat eskiden yalnizca uygulamanin kendi winws'i ile sifreli DNS'i kapatiyordu;
/// WinDivert surucusu cekirdekte RUNNING kaliyordu ve arayuz yine "Duraklatıldı"
/// diyordu. Artik duraklatmadan sonra olculuyor ve kalan HER SEY yaziliyor -- bir
/// tanesi bile atlanirsa kullanici arkada hicbir sey kalmadigini sanar.
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
        // OLCULEN HATA: surecler kapanmis, DNS geri alinmis, ama surucu duruyor.
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
