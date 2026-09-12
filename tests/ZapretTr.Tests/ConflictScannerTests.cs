using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Baska DPI araclarinin kalintilarini bulan ayristiricilar.
/// </summary>
/// <remarks>
/// Uc bicim de DISARIDAN geliyor -- <c>sc qc</c>, <c>netstat -ano</c> ve
/// <c>hosts</c>. Bicimi biz belirlemiyoruz, dolayisiyla ayristiricilarin
/// sinanmasi sart. Ustelik bu yolun sonu SILME: yanlis ayristirma, silinmemesi
/// gereken bir seyi silinebilir gostermek demek.
/// </remarks>
public sealed class ConflictScannerTests
{
    private const string GoodbyeDpiQc = """
        [SC] QueryServiceConfig SUCCESS

        SERVICE_NAME: GoodbyeDPI
                TYPE               : 10  WIN32_OWN_PROCESS
                START_TYPE         : 2   AUTO_START
                ERROR_CONTROL      : 1   NORMAL
                BINARY_PATH_NAME   : C:\Program Files\GoodbyeDPI\goodbyedpi.exe -5
                LOAD_ORDER_GROUP   :
                TAG                : 0
                DISPLAY_NAME       : GoodbyeDPI
                DEPENDENCIES       :
                SERVICE_START_NAME : LocalSystem
        """;

    [Fact]
    public void Sc_ciktisindan_alanlar_okunuyor()
    {
        Assert.Equal(
            @"C:\Program Files\GoodbyeDPI\goodbyedpi.exe -5",
            ConflictScanner.ReadScField(GoodbyeDpiQc, "BINARY_PATH_NAME"));

        // Baslatma turu "acilista geri geliyor mu" sorusunun cevabi; kalintinin
        // tehlikeli olup olmadigini belirleyen sey de bu.
        Assert.Contains("AUTO_START", ConflictScanner.ReadScField(GoodbyeDpiQc, "START_TYPE")!,
            StringComparison.Ordinal);

        // Bos alan null donmeli, bos dizgi degil: "DEPENDENCIES :" satiri
        // "bagimlilik var ama adi bos" diye okunmamali.
        Assert.Null(ConflictScanner.ReadScField(GoodbyeDpiQc, "DEPENDENCIES"));
        Assert.Null(ConflictScanner.ReadScField(GoodbyeDpiQc, "BOYLE_BIR_ALAN_YOK"));
        Assert.Null(ConflictScanner.ReadScField(null, "START_TYPE"));
    }

    [Fact]
    public void Surucu_yolundaki_nt_oneki_kaldiriliyor()
    {
        // Surucu servislerinde yol NT ad alaninda geliyor. On ek kalirsa
        // File.Exists her zaman false doner ve calisan bir surucu "kalinti"
        // sanilirdi -- yani silinmemesi gereken sey silinebilir gorunurdu.
        Assert.Equal(
            @"C:\Windows\System32\drivers\WinDivert64.sys",
            ConflictScanner.ExtractExecutablePath(@"\??\C:\Windows\System32\drivers\WinDivert64.sys"));
    }

    [Fact]
    public void Tirnakli_yol_argumanlardan_ayriliyor()
    {
        Assert.Equal(
            @"C:\Program Files\Tool\tool.exe",
            ConflictScanner.ExtractExecutablePath(@"""C:\Program Files\Tool\tool.exe"" --flag 1"));
    }

    [Fact]
    public void Tirnaksiz_bosluksuz_yol_argumanlardan_ayriliyor()
    {
        // Tirnaksiz ve bosluklu yollar diskteki dosyaya bakilarak ayriliyor;
        // burada dosya yok, dolayisiyla ilk boslukta kesiliyor. Bu geri
        // cekilmenin dogru davranmasi onemli: yanlis kesilen bir yol
        // File.Exists'te false verir ve calisan bir kurulumu "oksuz" gosterir.
        Assert.Equal(
            @"C:\Tools\ciadpi.exe",
            ConflictScanner.ExtractExecutablePath(@"C:\Tools\ciadpi.exe -p 1080"));

        Assert.Null(ConflictScanner.ExtractExecutablePath(null));
        Assert.Null(ConflictScanner.ExtractExecutablePath("   "));
    }

    [Fact]
    public void Netstat_ciktisinda_portu_tutan_pid_bulunuyor()
    {
        // Sifreli DNS 127.0.0.1:53'u dinlemek zorunda. Orayi baskasi tutuyorsa
        // dnscrypt hic acilamaz ve kullanici yalnizca "sifreli DNS calismadi"
        // gorur. Tutanin adini soylemek o duvari tek cumleye indiriyor.
        const string netstat = """
              Proto  Local Address          Foreign Address        State           PID
              UDP    0.0.0.0:5353           *:*                                    1234
              UDP    127.0.0.1:53           *:*                                    4242
              UDP    127.0.0.1:5355         *:*                                    9999
            """;

        Assert.Equal(4242, ConflictScanner.ParseUdpPortOwner(netstat, 53));
        Assert.Null(ConflictScanner.ParseUdpPortOwner(netstat, 853));
        Assert.Null(ConflictScanner.ParseUdpPortOwner(null, 53));
    }

    [Fact]
    public void Netstat_ciktisinda_port_numarasi_kismen_eslesmiyor()
    {
        // 5353 ve 5355 de "53" ile BASLIYOR. Metin karsilastirmasiyla yazilmis
        // bir ayristirici bunlari 53 sanardi ve kullaniciya masum bir servisi
        // "DNS portunu tutuyor" diye bildirirdi.
        const string netstat = """
              UDP    0.0.0.0:5353           *:*                                    1234
              UDP    127.0.0.1:5355         *:*                                    9999
            """;

        Assert.Null(ConflictScanner.ParseUdpPortOwner(netstat, 53));
    }

    [Fact]
    public void Hosts_dosyasinda_yalnizca_ilgilendigimiz_adlar_bulunuyor()
    {
        // Aranan sey dar ve somut: TEST HEDEFLERIMIZDEN biri yonlendirilmis mi.
        // "Butun hosts girdilerini supheli say" yaklasimi gurultu uretirdi --
        // reklam engelleyiciler o dosyayi mesru olarak dolduruyor.
        const string hosts = """
            # Copyright (c) 1993-2009 Microsoft Corp.
            127.0.0.1       localhost
            0.0.0.0 ads.example.net
            195.175.254.2   discord.com      # eski bir deneme
            10.0.0.5        gateway.discord.gg www.youtube.com
            """;

        var bulunan = ConflictScanner.ParseHostsOverrides(
            hosts, ["discord.com", "gateway.discord.gg", "www.youtube.com", "updates.discord.com"]);

        Assert.Equal(3, bulunan.Count);
        Assert.Contains(bulunan, b => b.Host == "discord.com" && b.Address == "195.175.254.2");

        // Tek satirda birden cok ad olabiliyor; ikisi de bulunmali.
        Assert.Contains(bulunan, b => b.Host == "gateway.discord.gg" && b.Address == "10.0.0.5");
        Assert.Contains(bulunan, b => b.Host == "www.youtube.com" && b.Address == "10.0.0.5");

        // Ilgilenmedigimiz ad ve yorum satiri gecmemeli.
        Assert.DoesNotContain(bulunan, b => b.Host == "ads.example.net");
    }

    [Fact]
    public void Hosts_yorum_satirlari_bulgu_sayilmiyor()
    {
        // Yorumlanmis bir satir ETKISIZ. Bulgu saymak, kullaniciyi olmayan bir
        // sorunun pesine dusururdu.
        const string hosts = """
            # 195.175.254.2 discord.com
            127.0.0.1 localhost   # discord.com
            """;

        Assert.Empty(ConflictScanner.ParseHostsOverrides(hosts, ["discord.com"]));
    }
}
