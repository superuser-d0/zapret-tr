using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// Günlük kullanımda hangi bölümlere dokunulduğunun kuralı.
/// </summary>
/// <remarks>
/// Bu testlerin var olma sebebi ölçülmüş bir zarar: ilk sürümde seçilen HTTPS
/// stratejisinin yanına diğer bütün bölümlerin en yüksek ağırlıklı adayları da
/// otomatik ekleniyordu. Gerçek bir koşumda sorunsuz çalışan QUIC bağlantısı,
/// üzerine denenmemiş bir QUIC stratejisi uygulanınca bozuldu: hiçbir şey
/// düzelmedi, bir şey bozuldu.
/// </remarks>
public sealed class RuntimeSelectionTests
{
    private static StrategyCandidate Candidate(
        string id, StrategySection section, string args, CandidateSource source, int weight = 100)
        => new()
        {
            Id = id,
            Section = section,
            Args = args,
            Weight = weight,
            Source = source,
        };

    private static IspProfile ProfileWith(params StrategyCandidate[] candidates)
        => new()
        {
            Id = "test",
            DisplayName = "Test",
            Candidates = candidates,
        };

    [Fact]
    public void SecilenStrateji_Her_Zaman_Uygulanir()
    {
        // Kullanıcının bilerek yaptığı tercih, doğrulanmamış olsa da uygulanır.
        var selection = RuntimeSelection.Build(profile: null, "--dpi-desync=fake");

        Assert.Single(selection);
        Assert.Equal("--dpi-desync=fake", selection[StrategySection.Tcp443]);
    }

    [Fact]
    public void DogrulanmamisBolumler_Komuta_Girmez()
    {
        var profile = ProfileWith(
            Candidate("quic-1", StrategySection.Quic, "--dpi-desync=fake --dpi-desync-repeats=11",
                CandidateSource.UpstreamPreset),
            Candidate("http-1", StrategySection.Tcp80, "--dpi-desync=multisplit",
                CandidateSource.Hypothesis),
            Candidate("voice-1", StrategySection.DiscordVoice, "--dpi-desync=fake",
                CandidateSource.CommunityUnverified));

        var selection = RuntimeSelection.Build(profile, "--dpi-desync=fake --dpi-desync-ttl=4");

        // Yalnızca kullanıcının seçtiği HTTPS bölümü. Diğerlerine dokunulmaz:
        // o trafik zaten çalışıyor olabilir ve denenmemiş bir strateji onu bozar.
        Assert.Single(selection);
        Assert.True(selection.ContainsKey(StrategySection.Tcp443));
        Assert.False(selection.ContainsKey(StrategySection.Quic));
        Assert.False(selection.ContainsKey(StrategySection.Tcp80));
        Assert.False(selection.ContainsKey(StrategySection.DiscordVoice));
    }

    [Fact]
    public void DogrulanmisBolum_Komuta_Girer()
    {
        var profile = ProfileWith(
            Candidate("quic-dogrulanmis", StrategySection.Quic, "--dpi-desync=fake",
                CandidateSource.Verified),
            Candidate("quic-tahmin", StrategySection.Quic, "--dpi-desync=udplen",
                CandidateSource.Hypothesis, weight: 200));

        var selection = RuntimeSelection.Build(profile, "--dpi-desync=fake --dpi-desync-ttl=4");

        Assert.Equal(2, selection.Count);

        // Ağırlığı daha yüksek olan tahmin değil, DOĞRULANMIŞ olan seçilir.
        Assert.Equal("--dpi-desync=fake", selection[StrategySection.Quic]);
    }

    [Fact]
    public void KorunmayanBolumler_Raporlanir()
    {
        var profile = ProfileWith(
            Candidate("quic-dogrulanmis", StrategySection.Quic, "--dpi-desync=fake",
                CandidateSource.Verified),
            Candidate("http-tahmin", StrategySection.Tcp80, "--dpi-desync=multisplit",
                CandidateSource.Hypothesis));

        var unprotected = RuntimeSelection.UnprotectedSections(profile);

        Assert.Contains(StrategySection.Tcp80, unprotected);
        Assert.Contains(StrategySection.DiscordVoice, unprotected);
        Assert.DoesNotContain(StrategySection.Quic, unprotected);
    }

    [Fact]
    public void ProfilYoksa_Hicbir_Bolum_Korunmuyor_Sayilir()
    {
        var unprotected = RuntimeSelection.UnprotectedSections(null);
        Assert.Equal(3, unprotected.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BosStrateji_Reddedilir(string args)
    {
        Assert.Throws<ArgumentException>(() => RuntimeSelection.Build(null, args));
    }

    [Fact]
    public void GercekTurkTelekomProfili_QuicBolumune_Yalnizca_DogrulanmisStratejiyle_Dokunuyor()
    {
        // Gerçek veriye karşı koşuluyor. Kural "QUIC'e hiç dokunma" DEĞİL, "yalnızca
        // DOĞRULANMIŞ stratejiyle dokun": ölçülmüş zarar, sorunsuz çalışan bir QUIC
        // bağlantısının üzerine DENENMEMİŞ bir strateji uygulanmasından gelmişti.
        //
        // Bu test bir süre "QUIC komuta hiç girmemeli" diye duruyordu, çünkü profilde
        // doğrulanmış QUIC adayı yoktu. Artık var (tt-quic-anyproto-cutoff, gerçek
        // TTNET hattında 3/3). Testin öncülü değişti, koruduğu kural değişmedi;
        // bu yüzden silinmedi, doğrulanmışlığı AÇIKÇA kontrol edecek şekilde yazıldı.
        var store = ProfileStore.Load();
        var tt = store.FindById("turk-telekom");
        Assert.NotNull(tt);

        var primary = tt.CandidatesFor(StrategySection.Tcp443).First();
        var selection = RuntimeSelection.Build(tt, primary.Args);

        Assert.True(selection.ContainsKey(StrategySection.Quic),
            "Dogrulanmis QUIC adayi var ama bolum komuta girmedi.");

        var verifiedQuic = tt.CandidatesFor(StrategySection.Quic)
            .Where(c => c.Source == CandidateSource.Verified)
            .Select(c => c.Args)
            .ToList();

        Assert.Contains(selection[StrategySection.Quic], verifiedQuic);
    }
}
