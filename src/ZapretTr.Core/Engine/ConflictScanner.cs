using System.Diagnostics;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>Bir çakışma bulgusunun türü.</summary>
public enum ConflictKind
{
    /// <summary>Başka bir DPI aracı ŞU AN çalışıyor.</summary>
    RunningProcess,

    /// <summary>Başka bir aracın servisi kurulu ve ikilisi yerinde: çalışan bir kurulum.</summary>
    InstalledTool,

    /// <summary>Servis kayıtlı ama ikilisi diskte yok: kaldırılmış bir aracın kalıntısı.</summary>
    OrphanService,

    /// <summary>Sahipsiz WinDivert sürücü servisi.</summary>
    DriverLeftover,

    /// <summary>127.0.0.1:53'ü başka biri tutuyor.</summary>
    DnsPortHolder,

    /// <summary>hosts dosyasında test hedeflerimizden birini yönlendiren satır.</summary>
    HostsOverride,
}

/// <summary>Tek bir çakışma bulgusu.</summary>
/// <param name="Kind">Bulgunun türü.</param>
/// <param name="Name">Servis/süreç adı ya da hosts girdisindeki ad.</param>
/// <param name="Description">Kullanıcıya gösterilecek cümle.</param>
/// <param name="Detail">İkili yolu, başlatma türü, adres gibi ayrıntı.</param>
/// <param name="Removable">
/// "Kalıntıları temizle" bunu silebilir mi. <b>false</b> olması "önemsiz" demek
/// değil; "silmek BİZİM işimiz değil" demek.
/// </param>
public sealed record ConflictFinding(
    ConflictKind Kind,
    string Name,
    string Description,
    string? Detail,
    bool Removable);

/// <summary>
/// Başka DPI atlatma araçlarının bıraktığı kalıntıları ve DNS'i bozan durumları arar.
/// </summary>
/// <remarks>
/// <para>
/// Neden gerekli: bu araca gelenlerin çoğu başka bir araçtan geliyor (Türkiye'de
/// en yaygını GoodbyeDPI). Eski araç "kaldırıldı" sanılıyor ama geride bir
/// <b>servis kaydı</b> kalıyor ve o servis açılışta ayağa kalkıp WinDivert
/// sürücüsünü kapıyor. winws kendi sürücüsünü yükleyemiyor ve BÜTÜN adaylar
/// aynı şekilde düşüyor -- dışarıdan görünen şey "hiçbir strateji çalışmadı".
/// Bir kullanıcıda tam bu tablo ölçüldü: 176 aday, 1105 saniye, sonuç yok.
/// </para>
/// <para>
/// Eskiden yalnızca <b>çalışan süreçlere</b> bakılıyordu. O kontrol, en sık
/// karşılaşılan hâli -- kapalı ama kurulu kalıntıyı -- hiç görmüyordu: kullanıcı
/// eski aracı kapatıyor, bize "kapattım" diyor, servis yine de açılışta geri
/// geliyor.
/// </para>
/// <para>
/// <b>Sınır:</b> bu sınıf başka bir ürünün DOSYALARINI silmez. Bizi engelleyen
/// şey dosyalar değil, <b>servis ve sürücü kaydı</b>; kayıt gidince disktekiler
/// zararsız duruyor. Başka bir ürünün klasörünü silmek ise çalışan bir kurulumu
/// mahvetmek olur ve geri dönüşü yok. İkilisi yerinde duran bir araç için
/// yapılan tek şey adını ve yolunu söylemek: kaldırma kararı ve yolu
/// kullanıcının.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ConflictScanner
{
    /// <summary>Bizim servislerimiz. Hiçbir koşulda çakışma sayılmaz.</summary>
    private static readonly string[] OwnServices =
        [ServiceManager.WinwsServiceName, ServiceManager.DnsServiceName];

    /// <summary>Bilinen araçların süreç adları (uzantısız).</summary>
    /// <remarks>
    /// <c>winws</c> BİLEREK yok: bizim motorumuzun adı da o. Listeye eklemek,
    /// uygulamanın kendini çakışma olarak bildirmesi demek olurdu.
    /// </remarks>
    private static readonly string[] KnownProcesses =
        ["goodbyedpi", "ciadpi", "byedpi", "spoofdpi", "zapret", "winws2", "greentunnel", "powertunnel"];

    /// <summary>Bilinen araçların kurduğu servis adları ve hangi araca ait oldukları.</summary>
    private static readonly (string Service, string Tool)[] KnownServices =
    [
        ("GoodbyeDPI", "GoodbyeDPI"),
        ("winws1", "Zapret / Zapret Win TR"),
        ("winws2", "Zapret / Zapret Win TR"),
        ("zapret", "Zapret"),
        ("ByeDPI", "ByeDPI"),
        ("ciadpi", "ByeDPI (ciadpi)"),
        ("SpoofDPI", "SpoofDPI"),
        ("PowerTunnel", "PowerTunnel"),
        ("GreenTunnel", "GreenTunnel"),
    ];

    /// <summary>
    /// Bütün kontrolleri koşar. Hiçbir koşulda fırlatmaz; okunamayan bir kontrol
    /// yalnızca sonuç üretmez.
    /// </summary>
    /// <param name="interestingHosts">
    /// hosts dosyasında aranacak adlar. Test hedefleri veriliyor: DNS zehirlenmesi
    /// tam olarak bu adları başka bir adrese çeviriyor.
    /// </param>
    public static async Task<IReadOnlyList<ConflictFinding>> ScanAsync(
        IEnumerable<string>? interestingHosts = null,
        CancellationToken cancellationToken = default)
    {
        var bulgular = new List<ConflictFinding>();

        var calisan = RunningTools();
        foreach (var ad in calisan)
        {
            bulgular.Add(new ConflictFinding(
                ConflictKind.RunningProcess,
                ad,
                $"{ad}.exe şu anda çalışıyor.",
                "WinDivert sürücüsünü aynı anda iki araç kullanamaz; ölçüm yapılamaz.",
                Removable: false));
        }

        await AddServiceFindingsAsync(bulgular, cancellationToken).ConfigureAwait(false);
        await AddDriverFindingsAsync(bulgular, calisan.Count > 0, cancellationToken).ConfigureAwait(false);
        await AddDnsFindingsAsync(bulgular, cancellationToken).ConfigureAwait(false);
        AddHostsFindings(bulgular, interestingHosts);

        return bulgular;
    }

    /// <summary>Şu anda çalışan bilinen araçların süreç adları.</summary>
    public static IReadOnlyList<string> RunningTools()
    {
        var found = new List<string>();

        foreach (var name in KnownProcesses)
        {
            try
            {
                if (Process.GetProcessesByName(name).Length > 0)
                {
                    found.Add(name);
                }
            }
            catch (Exception)
            {
                // Surec listesi okunamiyorsa teshis ugruna akisi durdurmuyoruz.
            }
        }

        return found;
    }

    private static async Task AddServiceFindingsAsync(
        List<ConflictFinding> bulgular, CancellationToken cancellationToken)
    {
        foreach (var (servis, arac) in KnownServices)
        {
            if (OwnServices.Contains(servis, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var (exitCode, output) = await RunScAsync(["qc", servis], cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                continue;
            }

            var yol = ExtractExecutablePath(ReadScField(output, "BINARY_PATH_NAME"));
            var baslatma = ReadScField(output, "START_TYPE") ?? "?";
            var otomatik = baslatma.Contains("AUTO_START", StringComparison.OrdinalIgnoreCase);

            // IKILI DISKTE YOKSA BU BIR KALINTI.
            //
            // Arac kaldirilmis ama servis kaydi kalmis. Boyle bir servis
            // acilista baslatilmaya calisiliyor; hicbir ise yaramiyor ama
            // surucu kaydini ve WinDivert'i kurcalayabiliyor. Silmesi guvenli:
            // arkasinda calisan bir urun yok.
            if (yol is not null && !File.Exists(yol))
            {
                bulgular.Add(new ConflictFinding(
                    ConflictKind.OrphanService,
                    servis,
                    $"{arac} kaldırılmış ama \"{servis}\" servis kaydı duruyor.",
                    $"İkili diskte yok: {yol}" + (otomatik ? " · açılışta başlatılıyor" : string.Empty),
                    Removable: true));

                continue;
            }

            // IKILI YERINDEYSE BU CALISAN BIR KURULUM. Silmek bizim isimiz degil.
            bulgular.Add(new ConflictFinding(
                ConflictKind.InstalledTool,
                servis,
                $"{arac} kurulu (\"{servis}\" servisi)."
                + (otomatik ? " Bilgisayar her açıldığında çalışıyor." : string.Empty),
                yol is null ? baslatma : yol + " · " + baslatma,
                Removable: false));
        }
    }

    private static async Task AddDriverFindingsAsync(
        List<ConflictFinding> bulgular, bool baskaAracCalisiyor, CancellationToken cancellationToken)
    {
        // WinDivert surucu servisi BIZIM de kullandigimiz sey: winws calisirken
        // orada durmasi normal. Bu yuzden yalnizca ortalikta calisan bir DPI
        // araci YOKKEN kalinti sayiliyor.
        if (baskaAracCalisiyor || IsOurEngineRunning())
        {
            return;
        }

        foreach (var surucu in WinDivertCleanup.DriverServiceNames)
        {
            var (exitCode, _) = await RunScAsync(["qc", surucu], cancellationToken).ConfigureAwait(false);
            if (exitCode != 0)
            {
                continue;
            }

            bulgular.Add(new ConflictFinding(
                ConflictKind.DriverLeftover,
                surucu,
                $"Sahipsiz ağ sürücüsü kaydı: \"{surucu}\".",
                "Hiçbir DPI aracı çalışmıyor ama sürücü kayıtlı. Çekirdekte asılı kalmış bir "
                + "sürücü, winws'in kendi sürücüsünü yüklemesini engelliyor ve bütün adaylar "
                + "aynı şekilde başarısız oluyor.",
                Removable: true));
        }
    }

    private static bool IsOurEngineRunning()
    {
        try
        {
            return Process.GetProcessesByName("winws").Length > 0;
        }
        catch (Exception)
        {
            // Okuyamiyorsak calisiyor VARSAYIYORUZ: yanlis tarafa dusmek
            // gerekiyorsa, kullanilan bir surucuyu silmeye kalkmaktansa
            // kalintiyi bildirmemek yeglenir.
            return true;
        }
    }

    private static async Task AddDnsFindingsAsync(
        List<ConflictFinding> bulgular, CancellationToken cancellationToken)
    {
        // 127.0.0.1:53'U BASKA BIRI TUTUYORSA sifreli DNS hic acilamaz.
        //
        // Belirtisi teshis edilmesi zor bir sey: dnscrypt-proxy baslatiliyor,
        // portu baglayamiyor, dogrulama sorgusu cevapsiz kaliyor ve kullanici
        // yalnizca "sifreli DNS calismadi" goruyor. Baska DPI/DNS araclarinin
        // bir kismi (GoodbyeDPI'in dns yonlendirmesi, yerel DNS onbellekleri)
        // tam olarak o portu tutuyor. Tutanin ADINI soylemek, bu duvari bir
        // cumleye indiriyor.
        if (await DnsCryptRunner.IsLocalResolverRespondingAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false))
        {
            // Cevap veren bizim dnscrypt'imiz olabilir; bu bir bulgu degil.
            return;
        }

        var tutan = await FindUdpPortHolderAsync(53, cancellationToken).ConfigureAwait(false);
        if (tutan is null)
        {
            return;
        }

        bulgular.Add(new ConflictFinding(
            ConflictKind.DnsPortHolder,
            tutan,
            $"127.0.0.1:53 portunu \"{tutan}\" tutuyor.",
            "Şifreli DNS o portu dinlemek zorunda. Başka bir program orayı tutuyorsa "
            + "dnscrypt-proxy açılamaz ve DNS engellemesi aşılamaz.",
            Removable: false));
    }

    private static void AddHostsFindings(List<ConflictFinding> bulgular, IEnumerable<string>? interestingHosts)
    {
        if (interestingHosts is null)
        {
            return;
        }

        try
        {
            var yol = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                "drivers", "etc", "hosts");

            if (!File.Exists(yol))
            {
                return;
            }

            foreach (var (host, adres) in ParseHostsOverrides(File.ReadAllText(yol), interestingHosts))
            {
                bulgular.Add(new ConflictFinding(
                    ConflictKind.HostsOverride,
                    host,
                    $"hosts dosyası {host} adresini {adres} yapıyor.",
                    "Bu satır sistemdeki BÜTÜN DNS çözümlemelerini atlıyor: şifreli DNS açık "
                    + $"olsa bile {host} oraya gider. Genellikle başka bir aracın ya da elle "
                    + $"yapılmış bir denemenin kalıntısıdır. Dosya: {yol}",
                    Removable: false));
            }
        }
        catch (Exception)
        {
            // hosts okunamadi. Teshis, asil akisi durdurmamali.
        }
    }

    // --- Ayristiricilar (saf, sinanabilir) --------------------------------------

    /// <summary>
    /// hosts dosyasında ilgilendiğimiz adları yönlendiren satırları bulur.
    /// </summary>
    /// <remarks>
    /// Yalnızca verilen adlar aranıyor. "Bütün hosts girdilerini şüpheli say"
    /// yaklaşımı gürültü üretirdi: reklam engelleyiciler ve geliştirme
    /// ortamları o dosyayı meşru olarak dolduruyor. Aradığımız şey dar ve
    /// somut: TEST HEDEFLERİMİZDEN biri yönlendirilmiş mi.
    /// </remarks>
    public static IReadOnlyList<(string Host, string Address)> ParseHostsOverrides(
        string? content, IEnumerable<string> interestingHosts)
    {
        var sonuc = new List<(string, string)>();
        if (string.IsNullOrWhiteSpace(content))
        {
            return sonuc;
        }

        var aranan = interestingHosts.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var satir in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var temiz = satir.Trim();

            var yorum = temiz.IndexOf('#');
            if (yorum >= 0)
            {
                temiz = temiz[..yorum].Trim();
            }

            if (temiz.Length == 0)
            {
                continue;
            }

            var parcalar = temiz.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parcalar.Length < 2)
            {
                continue;
            }

            for (var i = 1; i < parcalar.Length; i++)
            {
                if (aranan.Contains(parcalar[i]))
                {
                    sonuc.Add((parcalar[i], parcalar[0]));
                }
            }
        }

        return sonuc;
    }

    /// <summary><c>sc qc</c> çıktısından bir alanın değerini okur.</summary>
    public static string? ReadScField(string? output, string field)
    {
        if (string.IsNullOrEmpty(output))
        {
            return null;
        }

        foreach (var satir in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var i = satir.IndexOf(field, StringComparison.OrdinalIgnoreCase);
            if (i < 0)
            {
                continue;
            }

            var iki = satir.IndexOf(':', i + field.Length);
            if (iki < 0)
            {
                continue;
            }

            var deger = satir[(iki + 1)..].Trim();
            return deger.Length == 0 ? null : deger;
        }

        return null;
    }

    /// <summary>
    /// Servis komut satırından çalıştırılabilir dosyanın yolunu çıkarır.
    /// </summary>
    /// <remarks>
    /// Üç biçim birden geliyor: sürücülerde <c>\??\C:\...\x.sys</c>, tırnaklı
    /// yollar, ve tırnaksız "boşluklu yol + argüman" karışımı. Sonuncusu
    /// tek başına ayrıştırılamaz -- <c>C:\Program Files\x\y.exe -5</c> ile
    /// <c>C:\Program.exe Files\x</c> aynı görünür. Bu yüzden en uzun VAR OLAN
    /// önek aranıyor: diskin kendisi hakem.
    /// </remarks>
    public static string? ExtractExecutablePath(string? binaryPathName)
    {
        if (string.IsNullOrWhiteSpace(binaryPathName))
        {
            return null;
        }

        var s = binaryPathName.Trim();

        if (s.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            s = s[4..];
        }

        if (s.StartsWith('"'))
        {
            var son = s.IndexOf('"', 1);
            return son > 1 ? s[1..son] : null;
        }

        for (var i = s.Length; i > 0; i = s.LastIndexOf(' ', i - 1))
        {
            var aday = s[..i].TrimEnd();
            if (aday.Length > 0 && File.Exists(aday))
            {
                return aday;
            }
        }

        var bosluk = s.IndexOf(' ');
        return bosluk < 0 ? s : s[..bosluk];
    }

    /// <summary><c>netstat -ano</c> çıktısında verilen UDP portunu tutan PID.</summary>
    public static int? ParseUdpPortOwner(string? netstatOutput, int port)
    {
        if (string.IsNullOrEmpty(netstatOutput))
        {
            return null;
        }

        foreach (var satir in netstatOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parcalar = satir.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parcalar.Length < 3 || !parcalar[0].Equals("UDP", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var adres = parcalar[1];
            var ikiNokta = adres.LastIndexOf(':');
            if (ikiNokta < 0 || !int.TryParse(adres[(ikiNokta + 1)..], out var bulunan) || bulunan != port)
            {
                continue;
            }

            if (int.TryParse(parcalar[^1], out var pid))
            {
                return pid;
            }
        }

        return null;
    }

    private static async Task<string?> FindUdpPortHolderAsync(int port, CancellationToken cancellationToken)
    {
        var (exitCode, output) = await RunProcessAsync("netstat.exe", ["-ano", "-p", "UDP"], cancellationToken)
            .ConfigureAwait(false);

        if (exitCode != 0)
        {
            return null;
        }

        var pid = ParseUdpPortOwner(output, port);
        if (pid is null)
        {
            return null;
        }

        try
        {
            using var surec = Process.GetProcessById(pid.Value);
            return surec.ProcessName + ".exe";
        }
        catch (Exception)
        {
            return "PID " + pid.Value;
        }
    }

    // --- Kaldirma ----------------------------------------------------------------

    /// <summary>
    /// Yalnızca <see cref="ConflictFinding.Removable"/> olan bulguları kaldırır.
    /// </summary>
    /// <remarks>
    /// Silinebilir sayılan tek iki şey var ve ikisi de <b>kayıt</b>, dosya değil:
    /// ikilisi diskte olmayan servis kayıtları, ve hiçbir araç çalışmıyorken
    /// duran WinDivert sürücü kayıtları. Başka bir ürünün dosyalarını silmek
    /// bu sınıfın işi değil -- bizi engelleyen şey kayıt, ve çalışan bir kurulumu
    /// bozmanın geri dönüşü yok.
    /// </remarks>
    public static async Task<IReadOnlyList<CleanupStep>> RemoveAsync(
        IEnumerable<ConflictFinding> findings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ElevationGuard.EnsureElevated();

        var steps = new List<CleanupStep>();

        foreach (var bulgu in findings)
        {
            if (!bulgu.Removable)
            {
                continue;
            }

            // BIZIM SERVISLERIMIZE ASLA. Bulgu listesi disaridan geliyor ve bu
            // metot silme yetkisiyle kosuyor; kendi ayagimiza sikmanin onune
            // gecen tek sey bu kontrol.
            if (OwnServices.Contains(bulgu.Name, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            await RunScAsync(["stop", bulgu.Name], cancellationToken).ConfigureAwait(false);
            var (exitCode, output) = await RunScAsync(["delete", bulgu.Name], cancellationToken)
                .ConfigureAwait(false);

            // 1060 = "belirtilen servis yuklu degil". Kaldirma acisindan basari.
            steps.Add(exitCode == 0 || output.Contains("1060", StringComparison.Ordinal)
                ? new CleanupStep($"{bulgu.Name} kaydi kaldirildi", true)
                : new CleanupStep($"{bulgu.Name} kaydi kaldirilamadi", false, output.Trim()));
        }

        return steps;
    }

    // --- Surec calistirma ---------------------------------------------------------

    private static Task<(int ExitCode, string Output)> RunScAsync(
        string[] arguments, CancellationToken cancellationToken)
        => RunProcessAsync("sc.exe", arguments, cancellationToken);

    private static async Task<(int ExitCode, string Output)> RunProcessAsync(
        string fileName, string[] arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, $"{fileName} baslatilamadi.");
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            return (process.ExitCode, stdout + stderr);
        }
        catch (Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
