using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// "Kurulu" ile "calisiyor" ayrimi.
/// </summary>
/// <remarks>
/// Ikisini ayni sey saymak kullaniciyi KILITLIYORDU: servis kurulu ama durmussa
/// arayuz "servis modu aktif" deyip Baslat dugmesini kapatiyor, koruma olmuyor
/// ve kullanicinin yapabilecegi bir sey de kalmiyordu. Gercek bir kullanicida
/// 0.1.9'dan 0.1.15'e yukseltmeden sonra yasandi: yukseltme servisi geri
/// kuruyor, "sc start" bir sebeple basarisiz oluyor, servis VAR ama DURMUS
/// kaliyor.
///
/// Bu yuzden ucuncu bir durum var: kurulu-ama-durmus. Uygulama o durumda
/// Baslat'i ACIK birakiyor ki kullanici korumasini elle baslatabilsin.
/// </remarks>
public sealed class ServiceStatusTests
{
    [Fact]
    public void Kurulu_ve_calisiyor()
    {
        var durum = new ServiceStatus(WinwsInstalled: true, DnsInstalled: true, WinwsRunning: true);

        Assert.True(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }

    [Fact]
    public void Kurulu_ama_durmus_ayirt_ediliyor()
    {
        // TEHLIKELI DURUM. Eskiden bu, "kurulu" sayilip Baslat kapatiliyordu.
        var durum = new ServiceStatus(WinwsInstalled: true, DnsInstalled: true, WinwsRunning: false);

        Assert.True(durum.AnyInstalled);
        Assert.True(durum.InstalledButStopped);
    }

    [Fact]
    public void Hic_kurulu_degilse_durmus_sayilmaz()
    {
        // Servis yoksa "durmus" demek yanlis olur: kullanici otomatik baslatmayi
        // hic kurmamis olabilir ve ona bir sorun varmis gibi gostermemeliyiz.
        var durum = new ServiceStatus(WinwsInstalled: false, DnsInstalled: false, WinwsRunning: false);

        Assert.False(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }

    [Fact]
    public void Yalnizca_DNS_servisi_kuruluysa_winws_durmus_sayilmaz()
    {
        // Sifreli DNS kapaliyken yalnizca winws servisi kuruluyor; tersi de
        // mumkun. "winws durmus" uyarisi yalnizca winws servisi VARSA anlamli.
        var durum = new ServiceStatus(WinwsInstalled: false, DnsInstalled: true, WinwsRunning: false);

        Assert.True(durum.AnyInstalled);
        Assert.False(durum.InstalledButStopped);
    }
}
