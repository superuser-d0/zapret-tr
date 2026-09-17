using System.Text.Json;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// DNS yedeğinin diske yazılıp geri okunması.
/// </summary>
/// <remarks>
/// Bu, uygulamanın en tehlikeli yolu. Yedek bozulursa ya da eksik okunursa
/// kullanıcının DNS'i 127.0.0.1'de kalır ve makine HİÇBİR adı çözemez; ona göre
/// internetin tamamen gitmesi demek. Yedek bu yüzden bellekte değil DİSKTE
/// tutuluyor (çökme/yeniden başlatma sonrası da geri dönülebilsin diye) ve bu
/// testler o dosyanın sadakatini doğruluyor.
/// </remarks>
public sealed class DnsBackupTests
{
    [Fact]
    public void Yedek_JsonTuruna_Sadik_Gidip_Geliyor()
    {
        var original = new DnsBackup
        {
            CreatedAt = "2026-09-06T18:00:00.0000000Z",
            Entries =
            [
                new DnsBackupEntry
                {
                    Alias = "Ethernet",
                    Guid = "{11111111-2222-3333-4444-555555555555}",
                    WasStatic = false,
                    Addresses = ["192.168.8.1"],
                },
                new DnsBackupEntry
                {
                    Alias = "Wi-Fi",
                    Guid = "{66666666-7777-8888-9999-000000000000}",
                    WasStatic = true,
                    Addresses = ["8.8.8.8", "8.8.4.4"],
                },
            ],
        };

        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<DnsBackup>(json);

        Assert.NotNull(restored);
        Assert.Equal(2, restored.Entries.Count);

        var ethernet = restored.Entries[0];
        Assert.Equal("Ethernet", ethernet.Alias);
        Assert.False(ethernet.WasStatic);
        Assert.Equal(["192.168.8.1"], ethernet.Addresses);

        // Çoklu adres sırası korunmalı: netsh geri yüklerken index veriyoruz ve
        // sıra değişirse kullanıcının birincil/ikincil DNS'i yer değiştirir.
        var wifi = restored.Entries[1];
        Assert.True(wifi.WasStatic);
        Assert.Equal(["8.8.8.8", "8.8.4.4"], wifi.Addresses);
    }

    [Fact]
    public void DhcpKaydi_StaticOlarak_GeriYuklenmemeli()
    {
        // Bu ayrımın kaybolması sinsi bir hasar verir: DHCP'den gelen adresi
        // statik yazarsak, kullanıcı başka bir ağa bağlandığında (başka WiFi,
        // mobil paylaşım) eski ağ geçidinin DNS'ine sabitlenmiş kalır. Biz
        // "geri aldık" deriz ama makine bozuk kalmış olur.
        var entry = new DnsBackupEntry
        {
            Alias = "Ethernet",
            Guid = "{x}",
            WasStatic = false,
            Addresses = ["192.168.8.1"],
        };

        var roundTripped = JsonSerializer.Deserialize<DnsBackupEntry>(JsonSerializer.Serialize(entry));

        Assert.NotNull(roundTripped);
        Assert.False(roundTripped.WasStatic);
    }

    [Fact]
    public void Ipv6Alanlari_GidipGeliyor()
    {
        // Yönlendirme artık arayüzün IPv6 DNS sunucularını da BOŞALTIYOR: yalnızca
        // IPv4'ü 127.0.0.1'e çevirmek yetmiyor, çünkü arayüzde duran bir IPv6
        // çözümleyicisi (yönlendirici duyurusu ya da DHCPv6) DNS kaçırma katmanını
        // ayakta tutuyor. Boşaltılan şey yedekte taşınmazsa geri alma yarım kalır.
        var entry = new DnsBackupEntry
        {
            Alias = "Wi-Fi",
            Guid = "{66666666-7777-8888-9999-000000000000}",
            WasStatic = false,
            Addresses = ["192.168.1.1"],
            Ipv6Captured = true,
            Ipv6WasStatic = true,
            Ipv6Addresses = ["2606:4700:4700::1111", "2606:4700:4700::1001"],
        };

        var roundTripped = JsonSerializer.Deserialize<DnsBackupEntry>(JsonSerializer.Serialize(entry));

        Assert.NotNull(roundTripped);
        Assert.True(roundTripped.Ipv6Captured);
        Assert.True(roundTripped.Ipv6WasStatic);
        Assert.Equal(["2606:4700:4700::1111", "2606:4700:4700::1001"], roundTripped.Ipv6Addresses);
    }

    [Fact]
    public void EskiYedek_Ipv6ya_Dokunulmamis_Sayilir()
    {
        // 0.1.18 ve öncesinde yazılmış yedeklerde IPv6 alanları HİÇ YOK. Böyle bir
        // kaydı "IPv6 DHCP'ydi" diye okumak, geri alma sırasında kullanıcının ELLE
        // girdiği bir IPv6 DNS'ini silmek olurdu; hiç dokunmadığımız bir şeyi
        // bozmak. Bayrak bu yüzden ayrı ve varsayılanı false.
        var eski = """
            {"alias":"Ethernet","guid":"{1}","wasStatic":true,"addresses":["8.8.8.8"]}
            """;

        var okunan = JsonSerializer.Deserialize<DnsBackupEntry>(eski);

        Assert.NotNull(okunan);
        Assert.True(okunan.WasStatic);
        Assert.False(okunan.Ipv6Captured);
        Assert.Empty(okunan.Ipv6Addresses);
    }

    [Fact]
    public void BosYedek_Cozumlenebiliyor()
    {
        // Bozuk/boş bir dosya yüzünden geri alma yolunun tamamen patlamaması
        // gerekiyor; patlarsa kullanıcı DNS'i elle düzeltmek zorunda kalır.
        var restored = JsonSerializer.Deserialize<DnsBackup>("""{"createdAt":"","entries":[]}""");

        Assert.NotNull(restored);
        Assert.Empty(restored.Entries);
    }

    // --- Bizim 127.0.0.1'imiz asla "orijinal" sayılmaz --------------------------

    [Fact]
    public void YedegeGiren_Yalnizca127_DhcpSayilir()
    {
        // Yedek kaybolmuşken yeniden yönlendirme yapılırsa kartın DNS'i zaten
        // 127.0.0.1. Onu "elle girilmiş" diye kaydedip geri yazmak, kaldırmadan
        // sonra o kartta hiçbir adın çözülmemesi demekti.
        var (wasStatic, addresses) = SystemDnsManager.SanitizeCaptured(true, ["127.0.0.1"]);

        Assert.False(wasStatic);
        Assert.Empty(addresses);
    }

    [Fact]
    public void YedegeGiren_127_ve_GercekAdres_GercekAdresKalir()
    {
        var (wasStatic, addresses) = SystemDnsManager.SanitizeCaptured(true, ["127.0.0.1", "8.8.8.8"]);

        Assert.True(wasStatic);
        Assert.Equal(["8.8.8.8"], addresses);
    }

    [Fact]
    public void YedegeGiren_KullanicininElleAyari_Degismez()
    {
        var (wasStatic, addresses) = SystemDnsManager.SanitizeCaptured(true, ["1.1.1.1", "9.9.9.9"]);

        Assert.True(wasStatic);
        Assert.Equal(["1.1.1.1", "9.9.9.9"], addresses);
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.1,8.8.8.8", true)]
    [InlineData(null, true)]        // okunamadı: bizimki duruyor olabilir, geri al
    [InlineData("", false)]         // otomatiğe alınmış: geri alınacak bir şey yok
    [InlineData("1.1.1.1", false)]  // kullanıcı sonradan elle değiştirmiş: EZME
    public void GeriAlma_YalnizcaHalaBizdeyse(string? mevcut, bool beklenen)
    {
        // Servis modunda yönlendirme ile geri alma arasında aylar geçebiliyor.
        // Eskiden kullanıcının o arada yaptığı DNS ayarı yedekle eziliyordu.
        Assert.Equal(beklenen, SystemDnsManager.ShouldRestore(mevcut));
    }

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("127.0.0.1,127.0.0.1", true)]
    [InlineData("127.0.0.1,8.8.8.8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ZatenYonlendirilmis_YalnizcaTek127(string? mevcut, bool beklenen)
    {
        // Bekçi her ağ olayında yönlendirmeyi yeniden yapıyor; zaten bizde olan
        // karta tekrar netsh koşmak her seferinde gereksiz bir kesinti olurdu.
        // Ama 127.0.0.1'in yanında başka bir adres varsa Windows sorguyu oraya da
        // yollayabilir: o kart "bizde" sayılmamalı.
        Assert.Equal(beklenen, SystemDnsManager.IsOnlyLocalResolver(mevcut));
    }

    [Theory]
    [InlineData(DnsBackupOwner.App, DnsBackupOwner.Service, DnsBackupOwner.Service)]
    [InlineData(DnsBackupOwner.Service, DnsBackupOwner.App, DnsBackupOwner.Service)]
    [InlineData(DnsBackupOwner.App, DnsBackupOwner.App, DnsBackupOwner.App)]
    [InlineData(DnsBackupOwner.Service, DnsBackupOwner.Service, DnsBackupOwner.Service)]
    public void YedekSahibi_ServisHepKazanir(string mevcut, string istenen, string beklenen)
    {
        // Uygulama korumayı çalıştırırken servis kurulunca sahip "app" kalıyordu;
        // uygulama kapanınca servisin şifreli DNS'i sessizce geri alınıyordu.
        Assert.Equal(beklenen, SystemDnsManager.DecideOwner(mevcut, istenen));
    }

    [Fact]
    public void YerelCozumleyici_Adresi_Sabit()
    {
        // Bu değer yapılandırma dosyasına da yazılıyor; ikisi ayrışırsa
        // dnscrypt-proxy bir adreste dinler, sistem başka adrese sorar.
        Assert.Equal("127.0.0.1", SystemDnsManager.LocalResolver);
    }
}
