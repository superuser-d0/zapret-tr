using System.Net.NetworkInformation;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// DNS yonlendirmesinin hangi arayuzlere uygulanacagi.
/// </summary>
/// <remarks>
/// Bu secim bir kez yanlis yapildi ve belirtisi olmayan bir hataya donustu:
/// yalnizca "calisan + ag gecidi olan" arayuzler aliniyordu, dolayisiyla kablo
/// takiliyken kurulum yapan kullanicida WiFi atlaniyordu. Kullanici sonra WiFi'ye
/// gectiginde o arayuzun DNS'i ISS'in sunucusunda kaliyor, DNS engellemesi geri
/// geliyor, ama arayuz hala "KORUMA AKTIF" gosteriyordu -- cunku winws gercekten
/// calisiyordu, devrede olmayan sey sifreli DNS'ti.
///
/// Gercek arayuz listesi isletim sisteminden geldigi ve testte uretilemedigi icin
/// KARAR saf bir fonksiyona ayrildi; test edilen o.
/// </remarks>
public class DnsInterfaceSelectionTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211)]
    [InlineData(NetworkInterfaceType.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet)]
    public void BagliOlmayanFizikselKart_hedefe_dahil(NetworkInterfaceType type)
    {
        // Tam da atlanip hataya yol acan durum: kart var, su an bagli degil.
        Assert.True(SystemDnsManager.IsOfflinePhysical(OperationalStatus.Down, type));
    }

    [Fact]
    public void CalisanKart_bu_yoldan_gelmez()
    {
        // Calisan kartlar ag gecidi sartiyla ayrica secildigi icin buradan
        // gelmemeli; gelseydi ag gecidi olmayan sanal adaptorler de girerdi.
        Assert.False(SystemDnsManager.IsOfflinePhysical(
            OperationalStatus.Up, NetworkInterfaceType.Wireless80211));
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Tunnel)]
    [InlineData(NetworkInterfaceType.Loopback)]
    [InlineData(NetworkInterfaceType.Ppp)]
    public void TunelVeLoopback_hedef_degil(NetworkInterfaceType type)
    {
        // VPN tuneli ve loopback kullanicinin internete ciktigi kart degil;
        // bunlarin DNS'ini degistirmek kazanc saglamaz, bozma ihtimali vardir.
        Assert.False(SystemDnsManager.IsOfflinePhysical(OperationalStatus.Down, type));
    }
}
