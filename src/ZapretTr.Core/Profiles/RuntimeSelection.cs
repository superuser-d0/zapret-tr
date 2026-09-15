namespace ZapretTr.Core.Profiles;

/// <summary>
/// Gunluk kullanimda winws'e verilecek bolum -> strateji esleismesini kurar.
/// </summary>
/// <remarks>
/// Kural tek cumlede: <b>sorunu olmayan yere dokunma.</b>
///
/// Ilk surumde bu boyle degildi. Kullanicinin sectigi HTTPS stratejisinin yanina
/// diger butun bolumlerin en yuksek agirlikli adaylari da otomatik ekleniyordu --
/// oysa o adaylar cogunlukla <see cref="CandidateSource.UpstreamPreset"/> ya da
/// <see cref="CandidateSource.Hypothesis"/>, yani o baglantida hic denenmemis
/// tahminler. Gercek bir kosumda bunun bedeli olculdu: QUIC sorunsuz calisirken
/// uzerine denenmemis bir QUIC stratejisi uygulandi ve calisan baglanti bozuldu.
/// Hicbir sey duzelmedi, bir sey bozuldu.
///
/// Bu yuzden artik yalnizca su ikisi uygulaniyor:
///   1. Kullanicinin acikca sectigi strateji (bilerek yaptigi tercih).
///   2. Parametre testinde O BAGLANTIDA dogrulanmis adaylar.
///
/// Denenmemis bir aday, kullanici onu bilerek secmedikce calisan trafige
/// uygulanmaz. Bunlar kaybedilmis bir imkan degil: parametre testi calistirildiginda
/// dogrulanip kendiliginden devreye girerler.
/// </remarks>
public static class RuntimeSelection
{
    /// <summary>
    /// Kullanicinin sectigi HTTPS stratejisini, profildeki dogrulanmis diger
    /// bolumlerle birlestirir.
    /// </summary>
    /// <param name="profile">Secili servis saglayici profili. null olabilir.</param>
    /// <param name="selectedTcp443Args">
    /// Kullanicinin listeden sectigi HTTPS stratejisi. Bilerek yapilmis bir tercih
    /// oldugu icin dogrulanmamis olsa da uygulanir.
    /// </param>
    public static Dictionary<StrategySection, string> Build(
        IspProfile? profile, string selectedTcp443Args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedTcp443Args);

        var selection = new Dictionary<StrategySection, string>
        {
            [StrategySection.Tcp443] = selectedTcp443Args,
        };

        if (profile is null)
        {
            return selection;
        }

        foreach (var section in new[]
                 {
                     StrategySection.Tcp80,
                     StrategySection.Quic,
                     StrategySection.DiscordVoice,
                 })
        {
            // Yalnizca dogrulanmis aday. Yoksa o bolum komuta hic girmez ve
            // trafik dokunulmadan gecer.
            var verified = profile
                .CandidatesFor(section)
                .FirstOrDefault(c => c.Source == CandidateSource.Verified);

            if (verified is not null)
            {
                selection[section] = verified.Args;
            }
        }

        return selection;
    }

    /// <summary>
    /// Secilen stratejilerin O BAGLANTIDA hangi hedef siniflarini actigi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hostlist'i daraltmak icin. Kaynak, adaylarin <c>verifiedFor</c> alani: yani
    /// "bu hatta olculdu ve su kategoriyi acti" bilgisi. Tahmin degil, olcum.
    /// </para>
    /// <para>
    /// Kullanicinin elle sectigi strateji dogrulanmamis olabilir; o zaman hicbir
    /// kategori bilinmez ve liste bos doner. Cagiran taraf bunu "daraltma yapma ya da
    /// bilinen butun hedeflere in" diye yorumluyor (<see cref="HostlistStore.DomainsFor"/>);
    /// burada uydurma bir kategori dondurmek, olculmemis bir seyi olculmus gibi
    /// gostermek olurdu.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<string> VerifiedCategories(
        IspProfile? profile, IReadOnlyDictionary<StrategySection, string> selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        if (profile is null)
        {
            return [];
        }

        var kategoriler = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (section, args) in selection)
        {
            foreach (var candidate in profile.CandidatesFor(section))
            {
                if (!string.Equals(candidate.Args, args, StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (var category in candidate.VerifiedFor)
                {
                    if (!string.IsNullOrWhiteSpace(category))
                    {
                        kategoriler.Add(category);
                    }
                }
            }
        }

        return [.. kategoriler];
    }

    /// <summary>
    /// Profilde dogrulanmis adayi olmadigi icin komuta girmeyen bolumler.
    /// Arayuzde "bu bolumler icin once parametre testi calistirin" demek icin.
    /// </summary>
    public static IReadOnlyList<StrategySection> UnprotectedSections(IspProfile? profile)
    {
        if (profile is null)
        {
            return [StrategySection.Tcp80, StrategySection.Quic, StrategySection.DiscordVoice];
        }

        return new[] { StrategySection.Tcp80, StrategySection.Quic, StrategySection.DiscordVoice }
            .Where(s => !profile.CandidatesFor(s).Any(c => c.Source == CandidateSource.Verified))
            .ToList();
    }
}
