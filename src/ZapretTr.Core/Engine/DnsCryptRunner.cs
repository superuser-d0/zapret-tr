using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;

namespace ZapretTr.Core.Engine;

/// <summary>
/// dnscrypt-proxy sürecini yönetir ve sistem DNS'ini ona yönlendirir.
/// </summary>
/// <remarks>
/// Neden gerekli: Türkiye'de engelleme çoğu zaman İKİ KATMANLI. Önce DNS
/// kaçırılıyor (sistem çözümleyicisi engel sunucusunun adresini dönüyor), altta da
/// SNI'ye bakan bir DPI duruyor. winws yalnızca ikinci katmanı aşabilir; birinci
/// katman durdukça trafik zaten gerçek sunucuya gitmiyor ve hiçbir strateji işe
/// yaramıyor. Bu makinede ölçüldü: discord.com, pornhub.com ve xvideos.com'un üçü
/// de 195.175.254.2'ye, yani sağlayıcının engel sunucusuna çözümleniyordu.
///
/// Başlatma sırası kasıtlı ve değiştirilmemeli:
///   1. dnscrypt-proxy başlatılır.
///   2. GERÇEKTEN cevap verdiği doğrulanır (127.0.0.1:53'e bir sorgu atılır).
///   3. Ancak ondan sonra sistem DNS'i oraya çevrilir.
/// Ters sırada yapılsaydı, proxy açılmadığı bir durumda kullanıcı ad çözemez hâle
/// gelirdi; ona göre internetin tamamen gitmesi demek.
///
/// Süreç beklenmedik şekilde ölürse DNS ANINDA geri alınır; bu sınıfın en önemli
/// görevi de budur.
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

    /// <summary>Sistem DNS'i şu an bize yönlendirilmiş durumda mı.</summary>
    public bool IsDnsRedirected => _dnsRedirected;

    /// <summary>
    /// dnscrypt-proxy'yi başlatır, cevap verdiğini doğrular ve sistem DNS'ini çevirir.
    /// </summary>
    /// <exception cref="FileNotFoundException">İkili ya da yapılandırma yoksa.</exception>
    /// <exception cref="InvalidOperationException">Proxy cevap vermezse.</exception>
    /// <param name="owner">
    /// Yönlendirmenin sahibi. Servis kurulumu sırasında
    /// <see cref="DnsBackupOwner.Service"/> verilir; o zaman uygulama kapanırken
    /// DNS geri ALINMAZ, çünkü yönlendirme açılıştan açılışa sürekli olmalı.
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

        // ŞİFRELİ DNS SERVİSİ ZATEN AYAKTAYSA İKİNCİ BİR KOPYA AÇILMAZ.
        //
        // winws servisi kurulu ama durmuşsa arayüz "Başlat"ı açık bırakıyor (bu
        // doğru: koruma yok ve kullanıcı elle başlatabilmeli). Ama o durumda
        // ZapretTR-DNS servisi çoğu zaman ÇALIŞIYOR ve 127.0.0.1:53'ü tutuyor.
        // Eskiden uygulama yine de kendi dnscrypt'ini açıyordu; o süreç portu
        // bağlayamayıp hemen ölüyor, doğrulama "süreç yaşamıyor" diye düşüyor ve
        // "Başlat" tamamen BAŞARISIZ oluyordu; şifreli DNS aslında çalışırken.
        // Servisin çözümleyicisi kullanılıyor ve yönlendirme servise ait sayılıyor:
        // uygulama kapanınca geri alınmamalı, çözümleyici uygulamayla birlikte gitmiyor.
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

            // Beklenmedik ölüm: DNS bizde kalırsa makine ad çözemez. Geri alma
            // burada, en erken noktada tetikleniyor.
            process.Exited += (_, _) => OnUnexpectedExit();

            try
            {
                process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
                when (SecurityBlockAdvice.Describe(ex, "dnscrypt-proxy.exe") is { } engel)
            {
                // dnscrypt-proxy.exe imzasız; Defender ya da Akıllı Uygulama Denetimi
                // onu da engelleyebiliyor. Gerekçesi SecurityBlockAdvice'ta.
                process.Dispose();
                throw new InvalidOperationException(engel, ex);
            }

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            _process = process;
        }

        // Cevap verdiğini DOĞRULAMADAN sistem DNS'ine dokunmuyoruz.
        var wait = TimeSpan.FromSeconds(15);
        if (!await WaitUntilRespondingAsync(wait, cancellationToken).ConfigureAwait(false))
        {
            // Teşhis, süreç öldürülmeden ÖNCE toplanıyor: StopAsync'ten sonra
            // "süreç yaşıyor muydu" sorusunun cevabı kalmıyor.
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
    /// Sistem DNS'ini geri alır ve dnscrypt-proxy'yi durdurur.
    /// </summary>
    /// <remarks>
    /// Sıra yine kasıtlı: önce DNS geri alınır, sonra proxy durdurulur. Ters
    /// yapılsaydı aradaki kısa sürede makine ad çözemezdi.
    /// </remarks>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Yönlendirmenin sahibi kurulu servisse dokunmuyoruz: kullanıcı otomatik
        // başlatmayı kurdu, uygulamanın kapanması onu bozmamalı.
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
            // Zaten ölmüş.
        }
        finally
        {
            process.Dispose();
        }
    }

    /// <summary>
    /// Bu satır arayüz günlüğüne GİRMEMELİ mi.
    /// </summary>
    /// <remarks>
    /// dnscrypt-proxy açılışta bütün çözümleyici listesini yokluyor ve her biri
    /// için satır yazıyor: "OK (DNSCrypt) rtt: ...", "additional certificate",
    /// "post-quantum ... key exchange", ardından da 340 satırlık bir "Sorted
    /// latencies" tablosu. Toplam beş yüz satırı aşıyor.
    ///
    /// Arayüz günlüğü 500 satırla sınırlı. Yani bu döküm, günlükteki HER ŞEYİ
    /// dışarı itiyor: başlatma komutunu, winws'in söylediklerini, test
    /// sonuçlarını. Gerçek bir kullanıcının gönderdiği raporda tam olarak bu
    /// oldu: 523 satırlık dosyanın 470'i çözümleyici listesiydi ve teşhis için
    /// gereken satırların çoğu halka arabellekten düşmüştü.
    ///
    /// Gürültü susturuluyor, BİLGİ değil: hata ve uyarı seviyeleri her zaman
    /// geçiyor, "en düşük gecikmeli sunucu" özeti de geçiyor. Susturulan şey
    /// yalnızca sunucu başına tekrar eden satırlar.
    /// </remarks>
    public static bool IsNoise(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        // Hata ve uyarılar HER ZAMAN geçer: susturma yalnızca gürültü için.
        // dnscrypt'in bir sorunu varsa kullanıcının ad çözümü tehlikede demektir.
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

    /// <summary>Gecikme tablosunun bir satırı mı: "[NOTICE] -    19ms &lt;ad&gt;".</summary>
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
    /// Yerel çözümleyici cevap veriyor mu. Ağ ayarına bakmadan, doğrudan
    /// 127.0.0.1:53'e bir sorgu atarak ölçer.
    /// </summary>
    public static async Task<bool> IsLocalResolverRespondingAsync(
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        // Elle kurulmuş en küçük DNS sorgusu: "example.com A". Bir DNS kütüphanesi
        // eklemek yerine bunu yazmak, bağımlılık yüzeyini büyütmemek için.
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

            // Cevap, sorduğumuz işlemin cevabı mı: ilk iki bayt işlem kimliği,
            // üçüncü baytın üst biti "bu bir cevaptır" demek.
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
    /// Başlatma doğrulaması başarısız olduğunda "neden" sorusuna cevap üretir.
    /// </summary>
    /// <remarks>
    /// Bu yol bir kez gerçekten yaşandı ve teşhis edilemedi: bir makinede
    /// dnscrypt-proxy açılıyor, çözümleyicilere bağlanıyor (günlükte
    /// "OK (DNSCrypt) rtt: ...") ama bizim doğrulama sorgumuz cevapsız kalıyordu.
    /// Güvenli davranış doğru çalıştı, sistem DNS'ine dokunulmadı; ama elde
    /// yalnızca "cevap vermedi" cümlesi kaldığı için sebep bulunamadı. Başka bir
    /// hatta ve başka bir makinede, hem sıcak hem SOĞUK başlangıçta yeniden
    /// üretilemedi (ikisi de saniyeler içinde geçti).
    ///
    /// Bu yüzden burada ayırt edici iki bilgi toplanıyor:
    ///   - süreç hâlâ yaşıyor mu (yaşamıyorsa çıkış kodu),
    ///   - 127.0.0.1:53'ü dinleyen BİRİ var mı.
    /// İkisi birlikte üç farklı durumu ayırıyor: süreç ölmüş, süreç yaşıyor ama
    /// portu hiç bağlayamamış (başka bir servis tutuyor olabilir) ve port bağlı
    /// ama sorgu cevapsız (asıl bilinmeyen durum).
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
            // Teşhis, asıl hatanın önüne geçmemeli.
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
    /// Yapılandırmayı yazar ve yolunu döner.
    /// </summary>
    /// <remarks>
    /// Kendi toml'umuzu üretiyoruz; dağıtımla gelen 40 KB'lık örnek dosya bize
    /// gereken ondan çok daha dar bir yapılandırmanın yanında okunamaz kalıyor.
    /// </remarks>
    private Task<string> EnsureConfigAsync(CancellationToken cancellationToken)
        => WriteConfigAsync(_vendor, cancellationToken);

    /// <summary>
    /// Yapılandırmayı yazar; servis kurulumu da bunu kullanıyor.
    /// </summary>
    /// <remarks>
    /// Servis kurulumu eskiden dosyanın VAR OLMASINI bekliyordu ve dosyayı yalnızca
    /// uygulamanın "Başlat"ı yazıyordu. Arayüzsüz kurulumda (<c>--install-services</c>:
    /// yükseltme ve sessiz dağıtım) uygulama hiç başlatılmamışsa şifreli DNS servisi
    /// sessizce KURULMUYORDU; ayarlarda "şifreli DNS açık" yazarken.
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
    /// Sorgu paketi kurar: standart bir A kaydı sorgusu.
    /// </summary>
    private static byte[] BuildQuery(string host)
    {
        var id = (ushort)Random.Shared.Next(1, ushort.MaxValue);
        var bytes = new List<byte>
        {
            (byte)(id >> 8), (byte)(id & 0xFF),
            0x01, 0x00,             // standart sorgu, özyineleme istiyoruz
            0x00, 0x01,             // 1 soru
            0x00, 0x00,             // 0 cevap
            0x00, 0x00,             // 0 yetkili kayıt
            0x00, 0x00,             // 0 ek kayıt
        };

        foreach (var label in host.Split('.'))
        {
            bytes.Add((byte)label.Length);
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes(label));
        }

        bytes.Add(0x00);            // ad sonu
        bytes.AddRange([0x00, 0x01]); // tip A
        bytes.AddRange([0x00, 0x01]); // sınıf IN

        return [.. bytes];
    }

    private void OnUnexpectedExit()
    {
        if (!_dnsRedirected)
        {
            return;
        }

        // Yönlendirmeyi bu arada servis devraldıysa DNS artık bu sürece bağlı
        // değil; geri almak servisin şifreli DNS'ini sökmek olurdu.
        if (SystemDnsManager.IsOwnedByService)
        {
            _dnsRedirected = false;
            return;
        }

        Publish("dnscrypt-proxy beklenmedik sekilde kapandi. DNS geri aliniyor...");

        // Bu yolu beklemeye bırakamayız: DNS bizde kaldığı her saniye makine ad
        // çözemiyor demek.
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
