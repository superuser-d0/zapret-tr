namespace ZapretTr.Core.Profiles;

/// <summary>
/// Günlük kullanımda winws'e verilecek bölüm -> strateji eşleşmesini kurar.
/// </summary>
/// <remarks>
/// Kural tek cümlede: <b>sorunu olmayan yere dokunma.</b>
///
/// İlk sürümde bu böyle değildi. Kullanıcının seçtiği HTTPS stratejisinin yanına
/// diğer bütün bölümlerin en yüksek ağırlıklı adayları da otomatik ekleniyordu;
/// oysa o adaylar çoğunlukla <see cref="CandidateSource.UpstreamPreset"/> ya da
/// <see cref="CandidateSource.Hypothesis"/>, yani o bağlantıda hiç denenmemiş
/// tahminler. Gerçek bir koşumda bunun bedeli ölçüldü: QUIC sorunsuz çalışırken
/// üzerine denenmemiş bir QUIC stratejisi uygulandı ve çalışan bağlantı bozuldu.
/// Hiçbir şey düzelmedi, bir şey bozuldu.
///
/// Bu yüzden artık yalnızca şu ikisi uygulanıyor:
///   1. Kullanıcının açıkça seçtiği strateji (bilerek yaptığı tercih).
///   2. Parametre testinde O BAĞLANTIDA doğrulanmış adaylar.
///
/// Denenmemiş bir aday, kullanıcı onu bilerek seçmedikçe çalışan trafiğe
/// uygulanmaz. Bunlar kaybedilmiş bir imkân değil: parametre testi çalıştırıldığında
/// doğrulanıp kendiliğinden devreye girerler.
/// </remarks>
public static class RuntimeSelection
{
    /// <summary>
    /// Kullanıcının seçtiği HTTPS stratejisini, profildeki doğrulanmış diğer
    /// bölümlerle birleştirir.
    /// </summary>
    /// <param name="profile">Seçili servis sağlayıcı profili. null olabilir.</param>
    /// <param name="selectedTcp443Args">
    /// Kullanıcının listeden seçtiği HTTPS stratejisi. Bilerek yapılmış bir tercih
    /// olduğu için doğrulanmamış olsa da uygulanır.
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
            // Yalnızca doğrulanmış aday. Yoksa o bölüm komuta hiç girmez ve
            // trafik dokunulmadan geçer.
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
    /// Seçilen stratejilerin O BAĞLANTIDA hangi hedef sınıflarını açtığı.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Hostlist'i daraltmak için. Kaynak, adayların <c>verifiedFor</c> alanı: yani
    /// "bu hatta ölçüldü ve şu kategoriyi açtı" bilgisi. Tahmin değil, ölçüm.
    /// </para>
    /// <para>
    /// Kullanıcının elle seçtiği strateji doğrulanmamış olabilir; o zaman hiçbir
    /// kategori bilinmez ve liste boş döner. Çağıran taraf bunu "daraltma yapma ya da
    /// bilinen bütün hedeflere in" diye yorumluyor (<see cref="HostlistStore.DomainsFor"/>);
    /// burada uydurma bir kategori döndürmek, ölçülmemiş bir şeyi ölçülmüş gibi
    /// göstermek olurdu.
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
    /// Profilde doğrulanmış adayı olmadığı için komuta girmeyen bölümler.
    /// Arayüzde "bu bölümler için önce parametre testi çalıştırın" demek için.
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
