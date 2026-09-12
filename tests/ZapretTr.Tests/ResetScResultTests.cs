using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// "Tüm Ayarları Sıfırla" adimlarinda <c>sc</c> sonucunun yorumlanmasi.
/// </summary>
/// <remarks>
/// Sifirlamanin amaci bir DURUMA varmak; o durum zaten saglaniyorsa adim
/// basarilidir. Gercek bir kullanici raporunda su satir vardi:
///
///     ! [!] ZapretTR servisi durduruldu -- [SC] ControlService FAILED 1062
///
/// Servisin sureci bir onceki adimda oldurulmustu; durdurulacak bir sey yoktu ama
/// kullanici kirmizi bir hata goruyordu.
/// </remarks>
public sealed class ResetScResultTests
{
    [Fact]
    public void Basarili_komut_basarilidir()
    {
        Assert.True(WinDivertCleanup.InterpretScResult("x", 0, "[SC] DeleteService SUCCESS").Succeeded);
    }

    [Theory]
    [InlineData("[SC] OpenService FAILED 1060:\n\nThe specified service does not exist as an installed service.")]
    [InlineData("[SC] ControlService FAILED 1062:\n\nThe service has not been started.")]
    [InlineData("[SC] DeleteService FAILED 1072:\n\nThe specified service has been marked for deletion.")]
    public void Zaten_saglanmis_durum_hata_sayilmaz(string output)
    {
        var adim = WinDivertCleanup.InterpretScResult("x", 1, output);

        Assert.True(adim.Succeeded);
        Assert.False(string.IsNullOrWhiteSpace(adim.Detail));
    }

    [Fact]
    public void Gercek_hata_hata_olarak_kalir()
    {
        // 5 = erisim engellendi. Bunu yutmak, kullaniciyi "sifirladim" sanip
        // servisi yerinde birakilmis bir makineyle birakirdi.
        var adim = WinDivertCleanup.InterpretScResult("x", 5, "[SC] OpenService FAILED 5:\n\nAccess is denied.");

        Assert.False(adim.Succeeded);
    }

    [Fact]
    public void Sifreli_DNS_servisi_de_sifirlamada_siliniyor()
    {
        // Eskiden yalnizca ZapretTR siliniyordu; ZapretTR-DNS geride kaliyor ve
        // kurtarma tanimi yuzunden sureci kendiliginden geri geliyordu.
        Assert.Contains(ServiceManager.WinwsServiceName, WinDivertCleanup.ServiceNames);
        Assert.Contains(ServiceManager.DnsServiceName, WinDivertCleanup.ServiceNames);
    }
}
