using ZapretTr.Core.Profiles;

namespace ZapretTr.Core.Engine;

/// <summary>
/// winws.exe için argüman listesi üretir.
/// </summary>
/// <remarks>
/// Argümanlar tek bir dizgi değil, <see cref="IReadOnlyList{T}"/> olarak üretilir ve
/// çağıran taraf bunu <c>ProcessStartInfo.ArgumentList</c>'e verir. Böylece kabuk
/// tırnaklama kurallarını elle taklit etmek gerekmez; içinde boşluk olan kullanıcı
/// yolları (C:\Users\Ali Veli\...) bu yüzden sorun çıkarmaz.
///
/// Üretilen yapının kaynağı upstream'in kendi preset1_example.cmd dosyası:
///   global WinDivert filtresi, ardından --new ile ayrılmış bölümler.
/// </remarks>
public sealed class WinwsCommandBuilder(VendorPaths vendor)
{
    private readonly VendorPaths _vendor = vendor;

    /// <summary>
    /// Günlük kullanım komutu: her bölümün kazanan stratejisini tek bir winws
    /// örneği altında birleştirir.
    /// </summary>
    /// <param name="winners">
    /// Bölüm -> o bölümün kazanan argüman dizgisi. Kazananı olmayan bölümler
    /// komuta hiç girmez; gereksiz trafik yakalamak yalnızca CPU harcar ve
    /// bağlantıyı yavaşlatır.
    /// </param>
    /// <param name="hostlistDomains">
    /// Stratejinin UYGULANACAĞI alan adları. Boş ya da null verilirse bayrak hiç
    /// eklenmez ve strateji bütün trafiğe uygulanır (0.2.1 ve öncesinin davranışı).
    /// </param>
    public IReadOnlyList<string> BuildRuntimeCommand(
        IReadOnlyDictionary<StrategySection, string> winners,
        IReadOnlyCollection<string>? hostlistDomains = null)
    {
        ArgumentNullException.ThrowIfNull(winners);

        if (winners.Count == 0)
        {
            throw new ArgumentException(
                "En az bir bolum icin kazanan strateji gerekli; bos komut winws'i anlamsizca calistirir.",
                nameof(winners));
        }

        var args = new List<string>();
        var sections = OrderSections(winners.Keys);

        AddGlobalFilters(args, sections);

        var domains = hostlistDomains is { Count: > 0 }
            ? string.Join(',', hostlistDomains)
            : null;

        var first = true;
        foreach (var section in sections)
        {
            if (!first)
            {
                args.Add("--new");
            }

            first = false;
            args.Add(section.ToWinwsFilter());

            // Bölüm bazında: winws hostlist'i profil başına denetliyor ("hostlist
            // check for profile %d"), dolayısıyla bayrak her --new bölümüne ayrı
            // ayrı girmek zorunda. Bir kez, en başta yazmak yalnızca ilk bölümü
            // daraltırdı ve kalan bölümler yine bütün trafiğe dokunurdu.
            if (domains is not null && SupportsHostlist(section))
            {
                args.Add($"--hostlist-domains={domains}");
            }

            args.AddRange(SplitAndResolve(winners[section]));
        }

        return args;
    }

    /// <summary>
    /// Bölümde ana bilgisayar adı GÖRÜNÜYOR mu; yani hostlist ile daraltılabilir mi.
    /// </summary>
    /// <remarks>
    /// <see cref="StrategySection.DiscordVoice"/> HARİÇ tutuluyor ve bu, doğru olması
    /// zorunlu bir ayrıntı. O bölüm STUN/UDP medya trafiği (<c>--filter-l7=discord,stun</c>)
    /// ve içeriğinde alan adı YOK. Hostlist eklenirse winws o profil için ad eşleşmesi
    /// arar, hiçbir pakette bulamaz ve bölüm hiç devreye girmez; yani Discord sesi
    /// sessizce korumasız kalırdı. Diğerlerinde ad var: tcp80 Host başlığı, tcp443 TLS
    /// SNI, quic ise QUIC ClientHello SNI.
    /// </remarks>
    private static bool SupportsHostlist(StrategySection section)
        => section != StrategySection.DiscordVoice;

    /// <summary>
    /// Test komutu: tek bir bölüm, tek bir strateji, tek bir hedef IP.
    /// </summary>
    /// <remarks>
    /// <paramref name="targetIp"/> için <c>--ipset-ip</c> kullanılması paralel testin
    /// temeli. winws WinDivert'e global bir filtreyle bağlanır, yani aynı anda çalışan
    /// iki örnek aynı paketi görür. Ama strateji yalnızca kendi hedef IP'sine
    /// uygulandığı için eşleşmeyen paketler dokunulmadan geçer. Bu sayede N test
    /// işçisi ayrık hedeflerle birbirine karışmadan çalışabilir.
    ///
    /// Bu varsayım deneysel olarak doğrulanmalı (Adım 5 spike). Tutmazsa test motoru
    /// sıralı moda düşer; İSS kısayolu sayesinde özellik yine hızlı kalır.
    /// </remarks>
    public IReadOnlyList<string> BuildProbeCommand(StrategySection section, string strategyArgs, string targetIp)
        => BuildProbeCommand(section, strategyArgs, [targetIp]);

    /// <summary>
    /// Test komutu: tek bir bölüm, tek bir strateji, BİRDEN ÇOK hedef IP.
    /// </summary>
    /// <remarks>
    /// Çok hedefli hâl, paralel sınamanın doğru yolu. Önce her hedef için AYRI bir
    /// winws örneği başlatılıyordu; winws bunu reddediyor:
    ///
    ///   "A copy of winws is already running with the same filter"
    ///
    /// --ipset-ip GLOBAL WinDivert FİLTRESİNE GİRMİYOR; yalnızca süreç içindeki
    /// profil eşleşmesinde kullanılıyor. Dolayısıyla aynı bölümün iki işçisi birebir
    /// aynı filtreyi kuruyor ve ikincisi hemen 1 koduyla ölüyordu. Sonuç sessizdi:
    /// aday, hedeflerin yalnızca birinde ölçülüyor, diğerinde "calistirilamadi"
    /// yazılıyordu. Aynı strateji bir koşumda başarısız, bir koşumda başarılı
    /// görünüyordu; ölçülen şey aslında hangi işçinin önce başladığıydı.
    ///
    /// Doğru çözüm tek örnek: aynı ADAY zaten bütün hedeflere aynı stratejiyi
    /// uyguluyor, dolayısıyla hedefleri tek bir ipset'te toplamak anlam olarak
    /// aynı şey. Bölümün "aynı hedefe iki strateji birden uygulanamaz" kuralı
    /// bozulmuyor: burada tek strateji, çok hedef var.
    /// </remarks>
    public IReadOnlyList<string> BuildProbeCommand(
        StrategySection section, string strategyArgs, IReadOnlyList<string> targetIps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyArgs);
        ArgumentNullException.ThrowIfNull(targetIps);

        if (targetIps.Count == 0 || targetIps.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "En az bir gecerli hedef IP gerekli; hedefsiz ipset butun trafige dokunur.",
                nameof(targetIps));
        }

        var args = new List<string>();
        AddGlobalFilters(args, [section]);

        args.Add(section.ToWinwsFilter());

        // --ipset-ip liste alıyor (<ip_list>), tekrarlı bayrak değil.
        args.Add($"--ipset-ip={string.Join(',', targetIps)}");
        args.AddRange(SplitAndResolve(strategyArgs));

        return args;
    }

    /// <summary>
    /// Komutu insan tarafından okunabilir tek satıra çevirir. Yalnızca günlüğe yazmak
    /// ve kullanıcıya göstermek için; süreci başlatmak için ASLA kullanılmamalı,
    /// çünkü burada yapılan tırnaklama kabuk kurallarıyla birebir aynı değil.
    /// </summary>
    public static string ToDisplayString(IReadOnlyList<string> args)
        => string.Join(' ', args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));

    /// <summary>
    /// Bölümleri komuttaki kanonik sırasına koyar: önce TCP, sonra UDP tabanlı olanlar.
    /// Sıra davranışı değiştirmez ama üretilen komutu deterministik yapar, bu da
    /// günlüklerin ve testlerin karşılaştırılabilir olması demek.
    /// </summary>
    private static List<StrategySection> OrderSections(IEnumerable<StrategySection> sections)
    {
        var rank = new Dictionary<StrategySection, int>
        {
            [StrategySection.Tcp80] = 0,
            [StrategySection.Tcp443] = 1,
            [StrategySection.Quic] = 2,
            [StrategySection.DiscordVoice] = 3,
        };

        return sections.Distinct().OrderBy(s => rank[s]).ToList();
    }

    /// <summary>
    /// Global WinDivert filtresini yazar: hangi trafiğin çekirdekten kullanıcı alanına
    /// aktarılacağı. Filtre gereken en dar hâli olmalı; fazlası yalnızca CPU harcar.
    /// </summary>
    private void AddGlobalFilters(List<string> args, IReadOnlyCollection<StrategySection> sections)
    {
        var tcpPorts = new List<string>();
        if (sections.Contains(StrategySection.Tcp80))
        {
            tcpPorts.Add("80");
        }

        if (sections.Contains(StrategySection.Tcp443))
        {
            tcpPorts.Add("443");
        }

        if (tcpPorts.Count > 0)
        {
            args.Add($"--wf-tcp={string.Join(',', tcpPorts)}");
        }

        if (sections.Contains(StrategySection.Quic))
        {
            args.Add("--wf-udp=443");
            // QUIC Initial paketlerini ayırt eden hazır filtre parçası.
            args.Add($"--wf-raw-part=@{_vendor.QuicInitialFilter}");
        }

        if (sections.Contains(StrategySection.DiscordVoice))
        {
            // Discord ses trafiği sabit bir porta oturmaz; upstream'in hazır
            // filtre parçaları olmadan bu trafiği yakalamak mümkün değil.
            args.Add($"--wf-raw-part=@{_vendor.DiscordMediaFilter}");
            args.Add($"--wf-raw-part=@{_vendor.StunFilter}");
        }
    }

    /// <summary>
    /// Argüman dizgisini önce boşluklardan böler, sonra her parçadaki yer tutucuları çözer.
    /// Sıra önemli: ters yapılsaydı içinde boşluk olan bir dosya yolu iki argümana bölünürdü.
    /// </summary>
    private IEnumerable<string> SplitAndResolve(string strategyArgs)
        => strategyArgs
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(_vendor.ResolvePlaceholders);
}
