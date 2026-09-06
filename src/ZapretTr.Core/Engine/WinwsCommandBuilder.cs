using ZapretTr.Core.Profiles;

namespace ZapretTr.Core.Engine;

/// <summary>
/// winws.exe icin arguman listesi uretir.
/// </summary>
/// <remarks>
/// Argumanlar tek bir dizgi degil, <see cref="IReadOnlyList{T}"/> olarak uretilir ve
/// cagiran taraf bunu <c>ProcessStartInfo.ArgumentList</c>'e verir. Boylece kabuk
/// tirnaklama kurallarini elle taklit etmek gerekmez -- icinde bosluk olan kullanici
/// yollari (C:\Users\Ali Veli\...) bu yuzden sorun cikarmaz.
///
/// Uretilen yapinin kaynagi upstream'in kendi preset1_example.cmd dosyasi:
///   global WinDivert filtresi, ardindan --new ile ayrilmis bolumler.
/// </remarks>
public sealed class WinwsCommandBuilder(VendorPaths vendor)
{
    private readonly VendorPaths _vendor = vendor;

    /// <summary>
    /// Gunluk kullanim komutu: her bolumun kazanan stratejisini tek bir winws
    /// ornegi altinda birlestirir.
    /// </summary>
    /// <param name="winners">
    /// Bolum -> o bolumun kazanan arguman dizgisi. Kazanani olmayan bolumler
    /// komuta hic girmez; gereksiz trafik yakalamak yalnizca CPU harcar ve
    /// baglantiyi yavaslatir.
    /// </param>
    public IReadOnlyList<string> BuildRuntimeCommand(IReadOnlyDictionary<StrategySection, string> winners)
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

        var first = true;
        foreach (var section in sections)
        {
            if (!first)
            {
                args.Add("--new");
            }

            first = false;
            args.Add(section.ToWinwsFilter());
            args.AddRange(SplitAndResolve(winners[section]));
        }

        return args;
    }

    /// <summary>
    /// Test komutu: tek bir bolum, tek bir strateji, tek bir hedef IP.
    /// </summary>
    /// <remarks>
    /// <paramref name="targetIp"/> icin <c>--ipset-ip</c> kullanilmasi paralel testin
    /// temeli. winws WinDivert'e global bir filtreyle baglanir, yani ayni anda calisan
    /// iki ornek ayni paketi gorur. Ama strateji yalnizca kendi hedef IP'sine
    /// uygulandigi icin, eslesmeyen paketler dokunulmadan gecer. Bu sayede N test
    /// isçisi ayrik hedeflerle birbirine karismadan calisabilir.
    ///
    /// Bu varsayim ampirik olarak dogrulanmali (Adim 5 spike). Tutmazsa test motoru
    /// sirali moda duser; ISP kisayolu sayesinde ozellik yine hizli kalir.
    /// </remarks>
    public IReadOnlyList<string> BuildProbeCommand(StrategySection section, string strategyArgs, string targetIp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(strategyArgs);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetIp);

        var args = new List<string>();
        AddGlobalFilters(args, [section]);

        args.Add(section.ToWinwsFilter());
        args.Add($"--ipset-ip={targetIp}");
        args.AddRange(SplitAndResolve(strategyArgs));

        return args;
    }

    /// <summary>
    /// Komutu insan tarafindan okunabilir tek satira cevirir. Yalnizca gunluge yazmak
    /// ve kullaniciya gostermek icin -- sureci baslatmak icin ASLA kullanilmamali,
    /// cunku burada yapilan tirnaklama kabuk kurallariyla birebir ayni degil.
    /// </summary>
    public static string ToDisplayString(IReadOnlyList<string> args)
        => string.Join(' ', args.Select(a => a.Contains(' ', StringComparison.Ordinal) ? $"\"{a}\"" : a));

    /// <summary>
    /// Bolumleri komuttaki kanonik sirasina koyar: once TCP, sonra UDP tabanli olanlar.
    /// Sira davranisi degistirmez ama uretilen komutu deterministik yapar, bu da
    /// gunluklerin ve testlerin karsilastirilabilir olmasi demek.
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
    /// Global WinDivert filtresini yazar: hangi trafigin cekirdekten kullanici alanina
    /// aktarilacagi. Filtre gereken en dar hali olmali -- fazlasi yalnizca CPU harcar.
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
            // QUIC Initial paketlerini ayirt eden hazir filtre parcasi.
            args.Add($"--wf-raw-part=@{_vendor.QuicInitialFilter}");
        }

        if (sections.Contains(StrategySection.DiscordVoice))
        {
            // Discord ses trafigi sabit bir porta oturmaz; upstream'in hazir
            // filtre parcalari olmadan bu trafigi yakalamak mumkun degil.
            args.Add($"--wf-raw-part=@{_vendor.DiscordMediaFilter}");
            args.Add($"--wf-raw-part=@{_vendor.StunFilter}");
        }
    }

    /// <summary>
    /// Arguman dizgisini once bosluklardan boler, sonra her parcadaki yer tutuculari cozer.
    /// Sira onemli: ters yapilsaydi icinde bosluk olan bir dosya yolu iki argumana bolunurdu.
    /// </summary>
    private IEnumerable<string> SplitAndResolve(string strategyArgs)
        => strategyArgs
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(_vendor.ResolvePlaceholders);
}
