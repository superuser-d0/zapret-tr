using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>
/// dnscrypt-proxy surecini yonetir ve sistem DNS'ini ona yonlendirir.
/// </summary>
/// <remarks>
/// Neden gerekli: Turkiye'de engelleme cogu zaman IKI KATMANLI. Once DNS
/// kaciriliyor (sistem cozumleyicisi engel sunucusunun adresini donuyor), altta da
/// SNI'ye bakan bir DPI duruyor. winws yalnizca ikinci katmani asabilir; birinci
/// katman durdukca trafik zaten gercek sunucuya gitmiyor ve hicbir strateji ise
/// yaramiyor. Bu makinede olculdu: discord.com, pornhub.com ve xvideos.com'un ucu
/// de 195.175.254.2'ye, yani saglayicinin engel sunucusuna cozumleniyordu.
///
/// Baslatma sirasi kasitli ve degistirilmemeli:
///   1. dnscrypt-proxy baslatilir.
///   2. GERCEKTEN cevap verdigi dogrulanir (127.0.0.1:53'e bir sorgu atilir).
///   3. Ancak ondan sonra sistem DNS'i oraya cevrilir.
/// Ters sirada yapilsaydi, proxy acilmadigi bir durumda kullanici ad cozemez hale
/// gelirdi -- ona gore internetin tamamen gitmesi demek.
///
/// Surec beklenmedik sekilde olurse DNS ANINDA geri alinir; bu sinifin en onemli
/// gorevi de budur.
/// </remarks>
[SupportedOSPlatform("windows")]
public sealed class DnsCryptRunner : IAsyncDisposable
{
    private readonly VendorPaths _vendor;
    private readonly object _gate = new();
    private Process? _process;
    private bool _dnsRedirected;

    public DnsCryptRunner(VendorPaths vendor) => _vendor = vendor;

    public event Action<string>? LogLineReceived;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <summary>Sistem DNS'i su an bize yonlendirilmis durumda mi.</summary>
    public bool IsDnsRedirected => _dnsRedirected;

    /// <summary>
    /// dnscrypt-proxy'yi baslatir, cevap verdigini dogrular ve sistem DNS'ini cevirir.
    /// </summary>
    /// <exception cref="FileNotFoundException">Ikili ya da yapilandirma yoksa.</exception>
    /// <exception cref="InvalidOperationException">Proxy cevap vermezse.</exception>
    /// <param name="owner">
    /// Yonlendirmenin sahibi. Servis kurulumu sirasinda
    /// <see cref="DnsBackupOwner.Service"/> verilir; o zaman uygulama kapanirken
    /// DNS geri ALINMAZ, cunku yonlendirme acilistan acilisa surekli olmali.
    /// </param>
    public async Task StartAsync(
        string owner = DnsBackupOwner.App, CancellationToken cancellationToken = default)
    {
        ElevationGuard.EnsureElevated();

        if (!File.Exists(_vendor.DnsCryptExe))
        {
            throw new FileNotFoundException(
                VendorPaths.MissingFilesAdvice("dnscrypt-proxy.exe"),
                _vendor.DnsCryptExe);
        }

        // SIFRELI DNS SERVISI ZATEN AYAKTAYSA IKINCI BIR KOPYA ACILMAZ.
        //
        // winws servisi kurulu ama durmussa arayuz "Baslat"i acik birakiyor (bu
        // dogru: koruma yok ve kullanici elle baslatabilmeli). Ama o durumda
        // ZapretTR-DNS servisi cogu zaman CALISIYOR ve 127.0.0.1:53'u tutuyor.
        // Eskiden uygulama yine de kendi dnscrypt'ini aciyordu; o surec portu
        // baglayamayip hemen oluyor, dogrulama "surec yasamiyor" diye dusuyor ve
        // "Baslat" tamamen BASARISIZ oluyordu -- sifreli DNS aslinda calisirken.
        // Servisin cozumleyicisi kullaniliyor ve yonlendirme servise ait sayiliyor:
        // uygulama kapaninca geri alinmamali, cozumleyici uygulamayla birlikte gitmiyor.
        var dnsServisi = await ServiceManager.GetDnsServiceStateAsync(cancellationToken).ConfigureAwait(false);
        if (dnsServisi.Running
            && await IsLocalResolverRespondingAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
        {
            var servisle = await SystemDnsManager
                .RedirectToLocalAsync(DnsBackupOwner.Service, cancellationToken).ConfigureAwait(false);
            SystemDnsManager.ClearSuspended();
            _dnsRedirected = true;
            Publish("Sifreli DNS servisi zaten calisiyor; onun cozumleyicisi kullaniliyor."
                    + (servisle.Count == 0 ? string.Empty : " Yonlendirilen: " + string.Join(", ", servisle)));
            return;
        }

        var configPath = await EnsureConfigAsync(cancellationToken).ConfigureAwait(false);

        lock (_gate)
        {
            if (_process is { HasExited: false })
            {
                throw new InvalidOperationException("dnscrypt-proxy zaten calisiyor.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _vendor.DnsCryptExe,
                WorkingDirectory = Path.GetDirectoryName(configPath)!,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };

            startInfo.ArgumentList.Add("-config");
            startInfo.ArgumentList.Add(configPath);

            var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            process.OutputDataReceived += (_, e) => Publish(e.Data);
            process.ErrorDataReceived += (_, e) => Publish(e.Data);

            // Beklenmedik olum: DNS bizde kalirsa makine ad cozemez. Geri alma
            // burada, en erken noktada tetikleniyor.
            process.Exited += (_, _) => OnUnexpectedExit();

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }

        // Cevap verdigini DOGRULAMADAN sistem DNS'ine dokunmuyoruz.
        var wait = TimeSpan.FromSeconds(15);
        if (!await WaitUntilRespondingAsync(wait, cancellationToken).ConfigureAwait(false))
        {
            // Teshis, surec oldurulmeden ONCE toplaniyor: StopAsync'ten sonra
            // "surec yasiyor muydu" sorusunun cevabi kalmiyor.
            var diagnosis = DescribeStartupFailure(wait);
            await StopAsync(CancellationToken.None).ConfigureAwait(false);
            throw new InvalidOperationException(
                "dnscrypt-proxy baslatildi ama DNS sorgularina cevap vermedi. " +
                "Sistem DNS ayarina DOKUNULMADI. " + diagnosis);
        }

        var changed = await SystemDnsManager
            .RedirectToLocalAsync(owner, cancellationToken).ConfigureAwait(false);
        _dnsRedirected = true;
        Publish($"Sistem DNS'i yonlendirildi: {string.Join(", ", changed)}");
    }

    /// <summary>
    /// Sistem DNS'ini geri alir ve dnscrypt-proxy'yi durdurur.
    /// </summary>
    /// <remarks>
    /// Sira yine kasitli: once DNS geri alinir, sonra proxy durdurulur. Ters
    /// yapilsaydi aradaki kisa surede makine ad cozemezdi.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Yonlendirmenin sahibi kurulu servisse dokunmuyoruz: kullanici otomatik
        // baslatmayi kurdu, uygulamanin kapanmasi onu bozmamali.
        if (SystemDnsManager.IsOwnedByService)
        {
            _dnsRedirected = false;
        }
        else if (_dnsRedirected || SystemDnsManager.HasBackup)
        {
            try
            {
                var restored = await SystemDnsManager.RestoreAsync(cancellationToken).ConfigureAwait(false);
                if (restored.Count > 0)
                {
                    Publish($"Sistem DNS'i geri alindi: {string.Join(", ", restored)}");
                }
            }
            catch (Exception ex)
            {
                Publish("DNS geri alinamadi: " + ex.Message);
            }

            _dnsRedirected = false;
        }

        Process? process;
        lock (_gate)
        {
            process = _process;
            _process = null;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (InvalidOperationException)
        {
            // Zaten olmus.
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Bu satir arayuz gunlugune GIRMEMELI mi.
    /// </summary>
    /// <remarks>
    /// dnscrypt-proxy acilista butun cozumleyici listesini yokluyor ve her biri
    /// icin satir yaziyor: "OK (DNSCrypt) rtt: ...", "additional certificate",
    /// "post-quantum ... key exchange", ardindan da 340 satirlik bir "Sorted
    /// latencies" tablosu. Toplam bes yuz satiri asiyor.
    ///
    /// Arayuz gunlugu 500 satirla sinirli. Yani bu dokum, gunlukteki HER SEYI
    /// disari itiyor -- baslatma komutunu, winws'in soylediklerini, test
    /// sonuclarini. Gercek bir kullanicinin gonderdigi raporda tam olarak bu
    /// oldu: 523 satirlik dosyanin 470'i cozumleyici listesiydi ve teshis icin
    /// gereken satirlarin cogu ring buffer'dan dusmustu.
    ///
    /// Gurultu susturuluyor, BILGI degil: hata ve uyari seviyeleri her zaman
    /// geciyor, "en dusuk gecikmeli sunucu" ozeti de geciyor. Susturulan sey
    /// yalnizca sunucu basina tekrar eden satirlar.
    /// </remarks>
    public static bool IsNoise(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        // Hata ve uyarilar HER ZAMAN gecer: susturma yalnizca gurultu icin.
        // dnscrypt'in bir sorunu varsa kullanicinin ad cozumu tehlikede demektir.
        foreach (var seviye in new[] { "[ERROR]", "[WARNING]", "[CRITICAL]", "[FATAL]", "[PANIC]" })
        {
            if (line.Contains(seviye, StringComparison.Ordinal))
            {
                return false;
            }
        }

        foreach (var gurultu in new[]
                 {
                     "Sorted latencies:",
                     "] OK (DNSCrypt)",
                     "] OK (DoH)",
                     "] OK (ODoH)",
                     "additional certificate",
                     "post-quantum",
                 })
        {
            if (line.Contains(gurultu, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return IsLatencyRow(line);
    }

    /// <summary>Gecikme tablosunun bir satiri mi: "[NOTICE] -    19ms &lt;ad&gt;".</summary>
    private static bool IsLatencyRow(string line)
    {
        var i = line.IndexOf("] -", StringComparison.Ordinal);
        if (i < 0)
        {
            return false;
        }

        var j = i + 3;
        while (j < line.Length && line[j] == ' ')
        {
            j++;
        }

        var basi = j;
        while (j < line.Length && char.IsAsciiDigit(line[j]))
        {
            j++;
        }

        return j > basi && j + 1 < line.Length && line[j] == 'm' && line[j + 1] == 's';
    }

    /// <summary>
    /// Yerel cozumleyici cevap veriyor mu. Ag ayarina bakmadan, dogrudan
    /// 127.0.0.1:53'e bir sorgu atarak olcer.
    /// </summary>
    public static async Task<bool> IsLocalResolverRespondingAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        // Elle kurulmus en kucuk DNS sorgusu: "example.com A". Bir DNS kutuphanesi
        // eklemek yerine bunu yazmak, bagimlilik yuzeyini buyutmemek icin.
        var query = BuildQuery("example.com");

        try
        {
            using var udp = new UdpClient();
            udp.Client.ReceiveTimeout = 2000;
            await udp.SendAsync(query, query.Length,
                new IPEndPoint(IPAddress.Parse(SystemDnsManager.LocalResolver), 53)).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout ?? TimeSpan.FromSeconds(2));

            var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);

            // Cevap, sordugumuz islemin cevabi mi: ilk iki bayt islem kimligi,
            // ucuncu baytin ust biti "bu bir cevaptir" demek.
            return result.Buffer.Length > 3
                   && result.Buffer[0] == query[0]
                   && result.Buffer[1] == query[1]
                   && (result.Buffer[2] & 0x80) != 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Baslatma dogrulamasi basarisiz oldugunda "neden" sorusuna cevap uretir.
    /// </summary>
    /// <remarks>
    /// Bu yol bir kez gercekten yasandi ve teshis edilemedi: bir makinede
    /// dnscrypt-proxy aciliyor, cozumleyicilere baglaniyor (gunlukte
    /// "OK (DNSCrypt) rtt: ...") ama bizim dogrulama sorgumuz cevapsiz kaliyordu.
    /// Guvenli davranis dogru calisti -- sistem DNS'ine dokunulmadi -- ama elde
    /// yalnizca "cevap vermedi" cumlesi kaldigi icin sebep bulunamadi. Baska bir
    /// hatta ve baska bir makinede, hem sicak hem SOGUK baslangicta yeniden
    /// uretilemedi (ikisi de saniyeler icinde gecti).
    ///
    /// Bu yuzden burada ayirt edici iki bilgi toplaniyor:
    ///   - surec hala yasiyor mu (yasamiyorsa cikis kodu),
    ///   - 127.0.0.1:53'u dinleyen BIRI var mi.
    /// Ikisi birlikte uc farkli durumu ayiriyor: surec olmus, surec yasiyor ama
    /// portu hic baglayamamis (baska bir servis tutuyor olabilir), ve port bagli
    /// ama sorgu cevapsiz (asil bilinmeyen durum).
    /// </remarks>
    private string DescribeStartupFailure(TimeSpan waited)
    {
        var parts = new List<string> { $"{waited.TotalSeconds:F0} sn beklendi." };

        Process? process;
        lock (_gate)
        {
            process = _process;
        }

        try
        {
            parts.Add(process is null || process.HasExited
                ? $"Surec yasamiyor (cikis kodu: {(process is null ? "yok" : process.ExitCode.ToString())})."
                : "Surec hala calisiyor.");
        }
        catch (InvalidOperationException)
        {
            parts.Add("Surec durumu okunamadi.");
        }

        try
        {
            var listening = System.Net.NetworkInformation.IPGlobalProperties
                .GetIPGlobalProperties()
                .GetActiveUdpListeners()
                .Any(e => e.Port == 53
                          && (e.Address.Equals(IPAddress.Parse(SystemDnsManager.LocalResolver))
                              || e.Address.Equals(IPAddress.Any)));

            parts.Add(listening
                ? $"{SystemDnsManager.LocalResolver}:53 dinleniyor, yani port bagli ama sorgu cevapsiz kaldi."
                : $"{SystemDnsManager.LocalResolver}:53 dinlenmiyor -- port baglanamamis olabilir (baska bir DNS servisi tutuyor olabilir).");
        }
        catch (Exception)
        {
            // Teshis, asil hatanin onune gecmemeli.
            parts.Add("Port durumu okunamadi.");
        }

        return string.Join(' ', parts);
    }

    private async Task<bool> WaitUntilRespondingAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_gate)
            {
                if (_process is null or { HasExited: true })
                {
                    return false;
                }
            }

            if (await IsLocalResolverRespondingAsync(cancellationToken: cancellationToken).ConfigureAwait(false))
            {
                return true;
            }

            await Task.Delay(500, cancellationToken).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Yapilandirmayi yazar ve yolunu doner.
    /// </summary>
    /// <remarks>
    /// Kendi toml'umuzu uretiyoruz; dagitimla gelen 40 KB'lik ornek dosya bize
    /// gereken ondan cok daha dar bir yapilandirmanin yaninda okunamaz kaliyor.
    /// </remarks>
    private Task<string> EnsureConfigAsync(CancellationToken cancellationToken)
        => WriteConfigAsync(_vendor, cancellationToken);

    /// <summary>
    /// Yapilandirmayi yazar; servis kurulumu da bunu kullaniyor.
    /// </summary>
    /// <remarks>
    /// Servis kurulumu eskiden dosyanin VAR OLMASINI bekliyordu ve dosyayi yalnizca
    /// uygulamanin "Baslat"i yaziyordu. Arayuzsuz kurulumda (<c>--install-services</c>:
    /// yukseltme ve sessiz dagitim) uygulama hic baslatilmamissa sifreli DNS servisi
    /// sessizce KURULMUYORDU -- ayarlarda "sifreli DNS acik" yazarken.
    /// </remarks>
    internal static async Task<string> WriteConfigAsync(VendorPaths vendor, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(vendor.DnsCryptExe)!;
        var path = Path.Combine(directory, "zapret-tr-dnscrypt.toml");

        var config = $"""
            # ZapretTR tarafindan uretildi. Elle duzenlenirse bir sonraki baslatmada
            # uzerine yazilir.

            listen_addresses = ['{SystemDnsManager.LocalResolver}:53']
            max_clients = 250

            # Hem DNSCrypt hem DoH kabul ediliyor. Yalnizca DoH birakmak riskli:
            # DoH uc noktalarinin kendisi de engellenebiliyor, DNSCrypt protokolunun
            # engellenmesi ise daha zor.
            dnscrypt_servers = true
            doh_servers = true

            require_dnssec = false
            require_nolog = true
            require_nofilter = true

            # Kayit tutmayan ve filtrelemeyen sunucular arasindan otomatik secim.
            # Sabit bir sunucu yazmak, o sunucu engellendiginde ad cozumunun tamamen
            # durmasi demek olurdu.
            server_names = []

            timeout = 3000
            keepalive = 30
            cert_refresh_delay = 240

            # Onbellek: her sorgunun disari cikmasi hem yavas hem gereksiz.
            cache = true
            cache_size = 4096
            cache_min_ttl = 600
            cache_max_ttl = 86400

            # Sistem cozumleyicisi kacirilmis olabilecegi icin ilk acilista
            # kullanilmiyor; sunucu listesi dogrudan IP'lerden cekiliyor.
            bootstrap_resolvers = ['9.9.9.9:53', '1.1.1.1:53']
            ignore_system_dns = true

            netprobe_timeout = 60
            netprobe_address = '9.9.9.9:53'

            [sources]
              [sources.'public-resolvers']
              urls = [
                'https://raw.githubusercontent.com/DNSCrypt/dnscrypt-resolvers/master/v3/public-resolvers.md',
                'https://download.dnscrypt.info/resolvers-list/v3/public-resolvers.md'
              ]
              cache_file = 'public-resolvers.md'
              minisign_key = 'RWQf6LRCGA9i53mlYecO4IzT51TGPpvWucNSCh1CBM0QTaLn73Y7GFO3'
              refresh_delay = 72
              prefix = ''
            """;

        await File.WriteAllTextAsync(path, config, cancellationToken).ConfigureAwait(false);
        return path;
    }

    /// <summary>
    /// Sorgu paketi kurar: standart bir A kaydi sorgusu.
    /// </summary>
    private static byte[] BuildQuery(string host)
    {
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var bytes = new List<byte>
        {
            (byte)(id >> 8), (byte)(id & 0xFF),
            0x01, 0x00,             // standart sorgu, ozyineleme istiyoruz
            0x00, 0x01,             // 1 soru
            0x00, 0x00,             // 0 cevap
            0x00, 0x00,             // 0 yetkili kayit
            0x00, 0x00,             // 0 ek kayit
        };

        foreach (var label in host.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }

        bytes.Add(0x00);            // ad sonu
        bytes.AddRange([0x00, 0x01]); // tip A
        bytes.AddRange([0x00, 0x01]); // sinif IN

        return [.. bytes];
    }

    private void OnUnexpectedExit()
    {
        if (!_dnsRedirected)
        {
            return;
        }

        // Yonlendirmeyi bu arada servis devraldiysa DNS artik bu surece bagli
        // degil; geri almak servisin sifreli DNS'ini sokmek olurdu.
        if (SystemDnsManager.IsOwnedByService)
        {
            _dnsRedirected = false;
            return;
        }

        Publish("dnscrypt-proxy beklenmedik sekilde kapandi. DNS geri aliniyor...");

        // Bu yolu beklemeye birakamayiz: DNS bizde kaldigi her saniye makine ad
        // cozemiyor demek.
        try
        {
            SystemDnsManager.RestoreAsync().GetAwaiter().GetResult();
            _dnsRedirected = false;
            Publish("DNS geri alindi.");
        }
        catch (Exception ex)
        {
            Publish("DNS geri alinamadi: " + ex.Message);
        }
    }

    private void Publish(string? line)
    {
        if (!string.IsNullOrWhiteSpace(line))
        {
            LogLineReceived?.Invoke(line);
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);
}
