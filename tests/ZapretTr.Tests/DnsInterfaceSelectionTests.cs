using System.Net.NetworkInformation;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// DNS yönlendirmesinin hangi arayüzlere uygulanacağı.
/// </summary>
/// <remarks>
/// Bu seçim bir kez yanlış yapıldı ve belirtisi olmayan bir hataya dönüştü:
/// yalnızca "çalışan + ağ geçidi olan" arayüzler alınıyordu, dolayısıyla kablo
/// takılıyken kurulum yapan kullanıcıda WiFi atlanıyordu. Kullanıcı sonra WiFi'ye
/// geçtiğinde o arayüzün DNS'i İSS'in sunucusunda kalıyor, DNS engellemesi geri
/// geliyor, ama arayüz hâlâ "KORUMA AKTİF" gösteriyordu; çünkü winws gerçekten
/// çalışıyordu, devrede olmayan şey şifreli DNS'ti.
///
/// Gerçek arayüz listesi işletim sisteminden geldiği ve testte üretilemediği için
/// KARAR saf bir fonksiyona ayrıldı; test edilen o.
/// </remarks>
public class DnsInterfaceSelectionTests
{
    [Theory]
    [InlineData(NetworkInterfaceType.Wireless80211)]
    [InlineData(NetworkInterfaceType.Ethernet)]
    [InlineData(NetworkInterfaceType.GigabitEthernet)]
    public void BagliOlmayanFizikselKart_hedefe_dahil(NetworkInterfaceType type)
    {
        // Tam da atlanıp hataya yol açan durum: kart var, şu an bağlı değil.
        Assert.True(SystemDnsManager.IsOfflinePhysical(OperationalStatus.Down, type));
    }

    [Fact]
    public void CalisanKart_bu_yoldan_gelmez()
    {
        // Çalışan kartlar ağ geçidi şartıyla ayrıca seçildiği için buradan
        // gelmemeli; gelseydi ağ geçidi olmayan sanal bağdaştırıcılar da girerdi.
        Assert.False(SystemDnsManager.IsOfflinePhysical(
            OperationalStatus.Up, NetworkInterfaceType.Wireless80211));
    }

    [Theory]
    [InlineData(NetworkInterfaceType.Tunnel)]
    [InlineData(NetworkInterfaceType.Loopback)]
    [InlineData(NetworkInterfaceType.Ppp)]
    public void TunelVeLoopback_hedef_degil(NetworkInterfaceType type)
    {
        // VPN tüneli ve loopback kullanıcının internete çıktığı kart değil;
        // bunların DNS'ini değiştirmek kazanç sağlamaz, bozma ihtimali vardır.
        Assert.False(SystemDnsManager.IsOfflinePhysical(OperationalStatus.Down, type));
    }

    // --- Sanal kartlar -----------------------------------------------------------
    //
    // Tür süzgeci sanal kartları ayıramıyor: Wi-Fi Direct kartları kendini
    // Wireless80211 olarak bildiriyor. Gerçek bir kullanıcı raporunda iki tanesinin
    // DNS'i 127.0.0.1'e çevrilmişti. Açıklamalar o makinedeki Get-NetAdapter
    // çıktısından alındı.

    [Theory]
    [InlineData("Microsoft Wi-Fi Direct Virtual Adapter")]
    [InlineData("Microsoft Wi-Fi Direct Virtual Adapter #2")]
    [InlineData("Hyper-V Virtual Ethernet Adapter")]
    [InlineData("VirtualBox Host-Only Ethernet Adapter")]
    public void SanalKart_hedef_degil(string description)
    {
        Assert.True(SystemDnsManager.IsVirtualAdapterDescription(description));
    }

    [Theory]
    [InlineData("Realtek Gaming 2.5GbE Family Controller")]
    [InlineData("RZ616 Wi-Fi 6E 160MHz")]
    [InlineData("Bluetooth Device (Personal Area Network)")]
    [InlineData(null)]
    public void FizikselKart_ve_BluetoothPAN_sanal_sayilmaz(string? description)
    {
        // Bluetooth PAN kasıtlı: telefondan bağlantı paylaşan kullanıcı o kartla
        // internete çıkıyor ve orada da korunmalı. Windows onu "Virtual" bayrağıyla
        // bildirse de açıklamasında o kelime yok.
        Assert.False(SystemDnsManager.IsVirtualAdapterDescription(description));
    }
}
