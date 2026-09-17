using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Başka DPI araçlarının kalıntılarını bulan ayrıştırıcılar.
/// </summary>
/// <remarks>
/// Üç biçim de DIŞARIDAN geliyor: <c>sc qc</c>, <c>netstat -ano</c> ve
/// <c>hosts</c>. Biçimi biz belirlemiyoruz, dolayısıyla ayrıştırıcıların
/// sınanması şart. Üstelik bu yolun sonu SİLME: yanlış ayrıştırma, silinmemesi
/// gereken bir şeyi silinebilir göstermek demek.
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

        // Başlatma türü "açılışta geri geliyor mu" sorusunun cevabı; kalıntının
        // tehlikeli olup olmadığını belirleyen şey de bu.
        Assert.Contains("AUTO_START", ConflictScanner.ReadScField(GoodbyeDpiQc, "START_TYPE")!,
            StringComparison.Ordinal);

        // Boş alan null dönmeli, boş dizgi değil: "DEPENDENCIES :" satırı
        // "bağımlılık var ama adı boş" diye okunmamalı.
        Assert.Null(ConflictScanner.ReadScField(GoodbyeDpiQc, "DEPENDENCIES"));
        Assert.Null(ConflictScanner.ReadScField(GoodbyeDpiQc, "BOYLE_BIR_ALAN_YOK"));
        Assert.Null(ConflictScanner.ReadScField(null, "START_TYPE"));
    }

    [Fact]
    public void Surucu_yolundaki_nt_oneki_kaldiriliyor()
    {
        // Sürücü servislerinde yol NT ad alanında geliyor. Ön ek kalırsa
        // File.Exists her zaman false döner ve çalışan bir sürücü "kalıntı"
        // sanılırdı; yani silinmemesi gereken şey silinebilir görünürdü.
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
        // Tırnaksız ve boşluklu yollar diskteki dosyaya bakılarak ayrılıyor;
        // burada dosya yok, dolayısıyla ilk boşlukta kesiliyor. Bu geri
        // çekilmenin doğru davranması önemli: yanlış kesilen bir yol
        // File.Exists'te false verir ve çalışan bir kurulumu "öksüz" gösterir.
        Assert.Equal(
            @"C:\Tools\ciadpi.exe",
            ConflictScanner.ExtractExecutablePath(@"C:\Tools\ciadpi.exe -p 1080"));

        Assert.Null(ConflictScanner.ExtractExecutablePath(null));
        Assert.Null(ConflictScanner.ExtractExecutablePath("   "));
    }

    [Fact]
    public void Netstat_ciktisinda_portu_tutan_pid_bulunuyor()
    {
        // Şifreli DNS 127.0.0.1:53'ü dinlemek zorunda. Orayı başkası tutuyorsa
        // dnscrypt hiç açılamaz ve kullanıcı yalnızca "şifreli DNS çalışmadı"
        // görür. Tutanın adını söylemek o duvarı tek cümleye indiriyor.
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
        // 5353 ve 5355 de "53" ile BAŞLIYOR. Metin karşılaştırmasıyla yazılmış
        // bir ayrıştırıcı bunları 53 sanardı ve kullanıcıya masum bir servisi
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
        // Aranan şey dar ve somut: TEST HEDEFLERİMİZDEN biri yönlendirilmiş mi.
        // "Bütün hosts girdilerini şüpheli say" yaklaşımı gürültü üretirdi;
        // reklam engelleyiciler o dosyayı meşru olarak dolduruyor.
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

        // Tek satırda birden çok ad olabiliyor; ikisi de bulunmalı.
        Assert.Contains(bulunan, b => b.Host == "gateway.discord.gg" && b.Address == "10.0.0.5");
        Assert.Contains(bulunan, b => b.Host == "www.youtube.com" && b.Address == "10.0.0.5");

        // İlgilenmediğimiz ad ve yorum satırı geçmemeli.
        Assert.DoesNotContain(bulunan, b => b.Host == "ads.example.net");
    }

    [Fact]
    public void Hosts_yorum_satirlari_bulgu_sayilmiyor()
    {
        // Yorumlanmış bir satır ETKİSİZ. Bulgu saymak, kullanıcıyı olmayan bir
        // sorunun peşine düşürürdü.
        const string hosts = """
            # 195.175.254.2 discord.com
            127.0.0.1 localhost   # discord.com
            """;

        Assert.Empty(ConflictScanner.ParseHostsOverrides(hosts, ["discord.com"]));
    }

    // --- Kendi sürücümüz kalıntı sayılmamalı ---------------------------------------
    //
    // Ölçüldü (2026-09-16, 0.2.5): her parametre testinde "Sahipsiz ağ sürücüsü
    // kaydı: windivert" sorusu çıkıyordu; kayıt bir önceki testte kendi winws'imizin
    // yüklediği sürücüyü gösteriyordu.

    private const string OwnWinDivertQc = """
        [SC] QueryServiceConfig SUCCESS

        SERVICE_NAME: windivert
                TYPE               : 1  KERNEL_DRIVER
                START_TYPE         : 4   DISABLED
                ERROR_CONTROL      : 1   NORMAL
                BINARY_PATH_NAME   : \??\C:\Program Files\ZapretTR\zapret-winws\WinDivert64.sys
                LOAD_ORDER_GROUP   :
                TAG                : 0
                DISPLAY_NAME       : WinDivert
                DEPENDENCIES       :
                SERVICE_START_NAME :
        """;

    [Fact]
    public void Kendi_Surucumuzu_Gosteren_Kayit_Kalinti_Sayilmiyor()
    {
        // ExtractExecutablePath boşluklu yolu diskte doğruluyor; CI'da ZapretTR kurulu
        // olmadığı için burada yalnızca \??\ öneki soyuluyor.
        var yol = ConflictScanner.ReadScField(OwnWinDivertQc, "BINARY_PATH_NAME")![4..];

        Assert.True(ConflictScanner.IsOwnDriver(yol, @"C:\Program Files\ZapretTR\zapret-winws\WinDivert64.sys"));
        Assert.True(ConflictScanner.IsOwnDriver(yol, @"c:\program files\zapretTR\zapret-winws\windivert64.sys"));
    }

    [Theory]
    [InlineData(@"C:\Tools\zapret-discord-youtubein\WinDivert64.sys")]
    [InlineData(@"C:\GoodbyeDPI_64\WinDivert64.sys")]
    [InlineData(null)]
    [InlineData("")]
    public void Baska_Yerdeki_Surucu_Kalinti_Olarak_Kaliyor(string? yol)
    {
        Assert.False(ConflictScanner.IsOwnDriver(yol, @"C:\Program Files\ZapretTR\zapret-winws\WinDivert64.sys"));
    }

    [Fact]
    public void Kendi_Yolumuz_Bilinmiyorsa_Hicbir_Surucu_Bizim_Sayilmiyor()
    {
        // Vendor bulunamadıysa güvenli taraf: eski davranış (bildir, kullanıcı karar versin).
        Assert.False(ConflictScanner.IsOwnDriver(@"C:\Program Files\ZapretTR\zapret-winws\WinDivert64.sys", null));
    }
}
