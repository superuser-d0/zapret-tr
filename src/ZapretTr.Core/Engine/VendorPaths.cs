namespace ZapretTr.Core.Engine;

/// <summary>
/// vendor/zapret-winws altindaki upstream dosyalarinin yerini bulur ve profillerdeki
/// yer tutuculari gercek yollara cevirir.
/// </summary>
/// <remarks>
/// Profiller mutlak yol icermez -- <c>{FAKE_QUIC_GOOGLE}</c> gibi yer tutucular kullanir.
/// Boylece ayni profil JSON'u hem gelistirme agacinda hem kurulu uygulamada hem de
/// saha testi icin paketlenmis tek dosyalik Prober'da calisir.
/// </remarks>
public sealed class VendorPaths
{
    public const string FakeQuicGooglePlaceholder = "{FAKE_QUIC_GOOGLE}";
    public const string FakeTlsIanaPlaceholder = "{FAKE_TLS_IANA}";

    private VendorPaths(string root) => Root = root;

    /// <summary>vendor/zapret-winws dizini.</summary>
    public string Root { get; }

    /// <summary>
    /// Belirtilen dizini vendor koku kabul eder; varligini DOGRULAMAZ.
    /// </summary>
    /// <remarks>
    /// Iki kullanimi var: kullanicinin ikilileri elle baska bir yere koydugu kurulumlar,
    /// ve testler -- komut kurma mantigi diskte gercek bir winws.exe olmadan da
    /// dogrulanabilmeli, yoksa testler upstream indirmesine bagimli hale gelir.
    /// </remarks>
    public static VendorPaths ForRoot(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        return new VendorPaths(root);
    }

    public string WinwsExe => Path.Combine(Root, "winws.exe");
    public string WinDivertDll => Path.Combine(Root, "WinDivert.dll");
    public string WinDivertSys => Path.Combine(Root, "WinDivert64.sys");

    public string FakeQuicGoogle => Path.Combine(Root, "files", "quic_initial_www_google_com.bin");
    public string FakeTlsIana => Path.Combine(Root, "files", "tls_clienthello_iana_org.bin");

    public string DiscordMediaFilter => Path.Combine(Root, "windivert.filter", "windivert_part.discord_media.txt");
    public string StunFilter => Path.Combine(Root, "windivert.filter", "windivert_part.stun.txt");
    public string QuicInitialFilter => Path.Combine(Root, "windivert.filter", "windivert_part.quic_initial_ietf.txt");

    /// <summary>
    /// vendor dizinini arar: once uygulamanin yanina bakar (kurulu hal), sonra
    /// yukari dogru depo kokunu arar (gelistirme hali).
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">
    /// Bulunamazsa. Mesaj cozumu de soyler: fetch-upstream.ps1 calistirilmamis olabilir.
    /// </exception>
    public static VendorPaths Locate(string? startDirectory = null)
    {
        foreach (var candidate in EnumerateCandidates(startDirectory))
        {
            if (File.Exists(Path.Combine(candidate, "winws.exe")))
            {
                return new VendorPaths(candidate);
            }
        }

        throw new DirectoryNotFoundException(
            "vendor/zapret-winws bulunamadi (winws.exe yok). " +
            "Upstream ikilileri henuz indirilmemis olabilir: depo kokunden " +
            "'powershell -ExecutionPolicy Bypass -File tools\\fetch-upstream.ps1' calistir.");
    }

    /// <summary>Bulunabildiyse dondurur, bulunamazsa null. Kullanicidan once durum gostermek isteyen kod icin.</summary>
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

        // Kurulu hal: ikililer uygulamanin yaninda.
        yield return Path.Combine(start, "zapret-winws");
        yield return start;

        // Gelistirme hali: bin/Debug/net8.0 icinden yukari dogru depo kokunu ara.
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            yield return Path.Combine(dir.FullName, "vendor", "zapret-winws");
            dir = dir.Parent;
        }
    }

    /// <summary>
    /// Tek bir arguman parcasindaki yer tutuculari cozer.
    /// </summary>
    /// <remarks>
    /// Parca bazinda calisir cunku komut once bosluklardan bolunur, sonra degistirilir.
    /// Ters sirada yapilsaydi icinde bosluk olan bir kullanici yolu
    /// (C:\Users\Ali Veli\...) iki ayri argumana bolunurdu.
    /// </remarks>
    public string ResolvePlaceholders(string argument) => argument
        .Replace(FakeQuicGooglePlaceholder, FakeQuicGoogle, StringComparison.Ordinal)
        .Replace(FakeTlsIanaPlaceholder, FakeTlsIana, StringComparison.Ordinal);

    /// <summary>Indirilmesi gereken dosyalardan eksik olanlari listeler. Bos liste = her sey yerinde.</summary>
    public IReadOnlyList<string> FindMissingFiles()
    {
        var required = new[]
        {
            WinwsExe, WinDivertDll, WinDivertSys,
            FakeQuicGoogle, DiscordMediaFilter, StunFilter, QuicInitialFilter,
        };

        return required.Where(p => !File.Exists(p)).ToList();
    }
}
