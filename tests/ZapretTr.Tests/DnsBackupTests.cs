using System.Text.Json;
using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// DNS yedeginin diske yazilip geri okunmasi.
/// </summary>
/// <remarks>
/// Bu, uygulamanin en tehlikeli yolu. Yedek bozulursa ya da eksik okunursa
/// kullanicinin DNS'i 127.0.0.1'de kalir ve makine HICBIR adi cozemez -- ona gore
/// internetin tamamen gitmesi demek. Yedek bu yuzden bellekte degil DISKTE
/// tutuluyor (cokme/yeniden baslatma sonrasi da geri donulebilsin diye) ve bu
/// testler o dosyanin sadakatini dogruluyor.
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

        // Coklu adres sirasi korunmali: netsh geri yuklerken index veriyoruz ve
        // sira degisirse kullanicinin birincil/ikincil DNS'i yer degistirir.
        var wifi = restored.Entries[1];
        Assert.True(wifi.WasStatic);
        Assert.Equal(["8.8.8.8", "8.8.4.4"], wifi.Addresses);
    }

    [Fact]
    public void DhcpKaydi_StaticOlarak_GeriYuklenmemeli()
    {
        // Bu ayrimin kaybolmasi sinsi bir hasar verir: DHCP'den gelen adresi
        // static yazarsak, kullanici baska bir aga baglandiginda (baska wifi,
        // mobil paylasim) eski ag gecidinin DNS'ine sabitlenmis kalir. Biz
        // "geri aldik" deriz ama makine bozuk kalmis olur.
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
    public void BosYedek_Cozumlenebiliyor()
    {
        // Bozuk/bos bir dosya yuzunden geri alma yolunun tamamen patlamamasi
        // gerekiyor; patlarsa kullanici DNS'i elle duzeltmek zorunda kalir.
        var restored = JsonSerializer.Deserialize<DnsBackup>("""{"createdAt":"","entries":[]}""");

        Assert.NotNull(restored);
        Assert.Empty(restored.Entries);
    }

    [Fact]
    public void YerelCozumleyici_Adresi_Sabit()
    {
        // Bu deger yapilandirma dosyasina da yaziliyor; ikisi ayrisirsa
        // dnscrypt-proxy bir adreste dinler, sistem baska adrese sorar.
        Assert.Equal("127.0.0.1", SystemDnsManager.LocalResolver);
    }
}
