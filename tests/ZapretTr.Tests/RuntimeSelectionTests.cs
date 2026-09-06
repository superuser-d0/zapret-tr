using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// Gunluk kullanimda hangi bolumlere dokunuldugunun kurali.
/// </summary>
/// <remarks>
/// Bu testlerin varlik sebebi olculmus bir zarar: ilk surumde secilen HTTPS
/// stratejisinin yanina diger butun bolumlerin en yuksek agirlikli adaylari da
/// otomatik ekleniyordu. Gercek bir kosumda sorunsuz calisan QUIC baglantisi,
/// uzerine denenmemis bir QUIC stratejisi uygulanınca bozuldu -- hicbir sey
/// duzelmedi, bir sey bozuldu.
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
        // Kullanicinin bilerek yaptigi tercih, dogrulanmamis olsa da uygulanir.
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

        // Yalnizca kullanicinin sectigi HTTPS bolumu. Digerlerine dokunulmaz:
        // o trafik zaten calisiyor olabilir ve denenmemis bir strateji onu bozar.
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

        // Agirligi daha yuksek olan tahmin degil, DOGRULANMIS olan secilir.
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
    public void GercekTurkTelekomProfili_QuicBolumune_Dokunmuyor()
    {
        // Gercek veriye karsi: turk-telekom profilinde yalnizca tcp443 dogrulandi,
        // dolayisiyla QUIC komuta girmemeli. Bu tam olarak olculmus zarar vakasi.
        var store = ProfileStore.Load();
        var tt = store.FindById("turk-telekom");
        Assert.NotNull(tt);

        var primary = tt.CandidatesFor(StrategySection.Tcp443).First();
        var selection = RuntimeSelection.Build(tt, primary.Args);

        Assert.False(selection.ContainsKey(StrategySection.Quic),
            "QUIC bolumu dogrulanmamis bir stratejiyle komuta girdi.");
    }
}
