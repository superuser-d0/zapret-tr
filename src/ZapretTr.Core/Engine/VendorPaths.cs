namespace ZapretTr.Core.Engine;

/// <summary>
/// vendor/zapret-winws altındaki upstream dosyalarının yerini bulur ve profillerdeki
/// yer tutucuları gerçek yollara çevirir.
/// </summary>
/// <remarks>
/// Profiller mutlak yol içermez; <c>{FAKE_QUIC_GOOGLE}</c> gibi yer tutucular kullanır.
/// Böylece aynı profil JSON'u hem geliştirme ağacında hem kurulu uygulamada hem de
/// saha testi için paketlenmiş tek dosyalık Prober'da çalışır.
/// </remarks>
public sealed class VendorPaths
{
    public const string FakeQuicGooglePlaceholder = "{FAKE_QUIC_GOOGLE}";
    public const string FakeTlsIanaPlaceholder = "{FAKE_TLS_IANA}";

    // QUIC sahte yük çeşitleri. Hangisinin işe yaradığı DPI kutusunun neyi
    // doğruladığına bağlı olduğu için merdivende ayrı bir eksen olarak duruyorlar.
    public const string FakeQuicFacebookPlaceholder = "{FAKE_QUIC_FACEBOOK}";
    public const string FakeQuicVkPlaceholder = "{FAKE_QUIC_VK}";
    public const string FakeQuicKyberPlaceholder = "{FAKE_QUIC_KYBER}";
    public const string QuicShortHeaderPlaceholder = "{QUIC_SHORT_HEADER}";

    /// <summary>--dpi-desync-udplen-pattern için dolgu deseni.</summary>
    public const string Zero512Placeholder = "{ZERO_512}";

    private VendorPaths(string root) => Root = root;

    /// <summary>vendor/zapret-winws dizini.</summary>
    public string Root { get; }

    /// <summary>
    /// Belirtilen dizini vendor kökü kabul eder; varlığını DOĞRULAMAZ.
    /// </summary>
    /// <remarks>
    /// İki kullanımı var: kullanıcının ikilileri elle başka bir yere koyduğu kurulumlar
    /// ve testler. Komut kurma mantığı diskte gerçek bir winws.exe olmadan da
    /// doğrulanabilmeli, yoksa testler upstream indirmesine bağımlı hâle gelir.
    /// </remarks>
    public static VendorPaths ForRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new VendorPaths(root);
    }

    public string WinwsExe => Path.Combine(Root, "winws.exe");

    /// <summary>
    /// winws.exe cygwin ile derlendiği için bu DLL olmadan hiç başlamıyor.
    /// Eksikse Windows kalıcı (modal) bir hata penceresi açar ve süreç ölmez; yani
    /// çağıran taraf hata almak yerine SONSUZA KADAR BEKLER. Bu yüzden
    /// varlığı çalıştırmadan önce kontrol ediliyor.
    /// </summary>
    public string CygwinDll => Path.Combine(Root, "cygwin1.dll");

    /// <summary>
    /// dnscrypt-proxy. zapret-winws'in KARDEŞ dizininde duruyor, içinde değil:
    /// ayrı bir projeden geliyor ve ayrı bir sürümle sabitleniyor.
    /// </summary>
    public string DnsCryptExe => Path.Combine(
        Path.GetDirectoryName(Root) ?? Root, "dnscrypt-proxy", "dnscrypt-proxy.exe");

    public string WinDivertDll => Path.Combine(Root, "WinDivert.dll");
    public string WinDivertSys => Path.Combine(Root, "WinDivert64.sys");

    public string FakeQuicGoogle => Path.Combine(Root, "files", "quic_initial_www_google_com.bin");
    public string FakeTlsIana => Path.Combine(Root, "files", "tls_clienthello_iana_org.bin");

    public string FakeQuicFacebook => Path.Combine(Root, "files", "quic_initial_facebook_com.bin");
    public string FakeQuicVk => Path.Combine(Root, "files", "quic_initial_vk_com.bin");
    public string FakeQuicKyber => Path.Combine(Root, "files", "quic_initial_rutracker_org_kyber_1.bin");
    public string QuicShortHeader => Path.Combine(Root, "files", "quic_short_header.bin");
    public string Zero512 => Path.Combine(Root, "files", "zero_512.bin");

    public string DiscordMediaFilter => Path.Combine(Root, "windivert.filter", "windivert_part.discord_media.txt");
    public string StunFilter => Path.Combine(Root, "windivert.filter", "windivert_part.stun.txt");
    public string QuicInitialFilter => Path.Combine(Root, "windivert.filter", "windivert_part.quic_initial_ietf.txt");

    /// <summary>
    /// vendor dizinini arar: önce uygulamanın yanına bakar (kurulu hâl), sonra
    /// yukarı doğru depo kökünü arar (geliştirme hâli).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// Bulunamazsa. Mesaj önce KULLANICININ yapabileceği şeyi söyler.
    /// </exception>
    /// <remarks>
    /// Mesajın sırası kasıtlı. Eskiden yalnızca "depo kökünden
    /// fetch-upstream.ps1 çalıştır" yazıyordu; yani kurulum paketiyle gelen bir
    /// kullanıcıya, elinde olmayan bir depoda, kullanamayacağı bir komut. Oysa
    /// KURULU bir makinede bu dosyanın kaybolmasının en olası sebebi belli:
    /// WinDivert çekirdek sürücüsü taşıdığı için virüs programları
    /// <c>winws.exe</c> ve <c>WinDivert64.sys</c>'i sık sık karantinaya alıyor.
    /// Kurulum sorunsuz bitiyor, dosyalar sonradan siliniyor ve kullanıcının
    /// gördüğü tek şey uygulamanın çalışmaması oluyor.
    /// </remarks>
    public static VendorPaths Locate(string? startDirectory = null)
    {
        foreach (var candidate in EnumerateCandidates(startDirectory))
        {
            if (File.Exists(Path.Combine(candidate, "winws.exe")))
            {
                return new VendorPaths(candidate);
            }
        }

        throw new DirectoryNotFoundException(MissingFilesAdvice("winws.exe"));
    }

    /// <summary>
    /// Eksik dosya durumunda kullanıcıya gösterilecek metin.
    /// </summary>
    /// <remarks>
    /// Tek yerde duruyor, çünkü aynı durum üç ayrı yoldan bildiriliyordu
    /// (<see cref="Locate"/>, <c>WinwsRunner.Start</c>, <c>DnsCryptRunner</c>) ve
    /// üçü de kullanıcıya yalnızca geliştirici talimatı veriyordu.
    /// </remarks>
    public static string MissingFilesAdvice(string missing) =>
        $"Kurulum dosyalari eksik: {missing}. " +
        "En sik sebep virus programinin dosyalari karantinaya almasi -- ZapretTR " +
        "cekirdek modunda calisan bir ag surucusu (WinDivert) tasiyor ve bu " +
        "surucu sik sik yanlis alarm veriyor. Cozum: virus programinin " +
        "karantinasina bakip (Microsoft Defender icin: Windows Guvenligi > Virus ve " +
        "tehdit korumasi > Koruma gecmisi > Izin ver / Geri yukle) ZapretTR " +
        "klasorunu istisna listesine ekleyin, sonra " +
        "kurulum paketini yeniden calistirin. " +
        "(Depodan calistiriyorsaniz: 'powershell -ExecutionPolicy Bypass -File " +
        "tools\\fetch-upstream.ps1'.)";

    /// <summary>Bulunabildiyse döndürür, bulunamazsa null. Kullanıcıdan önce durum göstermek isteyen kod için.</summary>
    public static VendorPaths? TryLocate(string? startDirectory = null)
    {
        try
        {
            return Locate(startDirectory);
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateCandidates(string? startDirectory)
    {
        var start = startDirectory ?? AppContext.BaseDirectory;

        // Kurulu hâl: ikililer uygulamanın yanında.
        yield return Path.Combine(start, "zapret-winws");
        yield return start;

        // Geliştirme hâli: bin/Debug/net8.0 içinden yukarı doğru depo kökünü ara.
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "vendor", "zapret-winws");
            dir = dir.Parent;
        }
    }

    /// <summary>
    /// Tek bir argüman parçasındaki yer tutucuları çözer.
    /// </summary>
    /// <remarks>
    /// Parça bazında çalışır, çünkü komut önce boşluklardan bölünür, sonra değiştirilir.
    /// Ters sırada yapılsaydı içinde boşluk olan bir kullanıcı yolu
    /// (C:\Users\Ali Veli\...) iki ayrı argümana bölünürdü.
    /// </remarks>
    public string ResolvePlaceholders(string argument) => argument
        .Replace(FakeQuicGooglePlaceholder, FakeQuicGoogle, StringComparison.Ordinal)
        .Replace(FakeTlsIanaPlaceholder, FakeTlsIana, StringComparison.Ordinal)
        .Replace(FakeQuicFacebookPlaceholder, FakeQuicFacebook, StringComparison.Ordinal)
        .Replace(FakeQuicVkPlaceholder, FakeQuicVk, StringComparison.Ordinal)
        .Replace(FakeQuicKyberPlaceholder, FakeQuicKyber, StringComparison.Ordinal)
        .Replace(QuicShortHeaderPlaceholder, QuicShortHeader, StringComparison.Ordinal)
        .Replace(Zero512Placeholder, Zero512, StringComparison.Ordinal);

    /// <summary>İndirilmesi gereken dosyalardan eksik olanları listeler. Boş liste = her şey yerinde.</summary>
    public IReadOnlyList<string> FindMissingFiles()
    {
        var required = new[]
        {
            WinwsExe, CygwinDll, WinDivertDll, WinDivertSys,
            FakeQuicGoogle, DiscordMediaFilter, StunFilter, QuicInitialFilter,
            FakeQuicFacebook, FakeQuicVk, FakeQuicKyber, QuicShortHeader, Zero512,
        };

        return required.Where(p => !File.Exists(p)).ToList();
    }
}
