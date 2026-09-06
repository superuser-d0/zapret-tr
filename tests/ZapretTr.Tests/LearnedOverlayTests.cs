using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// Kullanicinin kendi dogruladiklarinin dagitim profilleri uzerine bindirilmesi.
/// </summary>
/// <remarks>
/// Bu katman olmadan uygulama her acilista sifirdan basliyordu: parametre testi
/// 2-3 dakika kosup calisan bir strateji buluyor, uygulama kapaninca kayboluyordu.
///
/// Ogrenilenler dagitimla gelen profillerin ICINE yazilmiyor; o dosyalar depo
/// dosyalari ve uretecin ciktisi. Ayri durup yuklemede bindirilmeleri, upstream
/// profil guncellemesinin kullanicinin kendi dogrulamalarini silmemesini sagliyor.
/// </remarks>
public sealed class LearnedOverlayTests
{
    private static IspProfile SampleProfile() => new()
    {
        Id = "test-isp",
        DisplayName = "Test",
        Candidates =
        [
            new StrategyCandidate
            {
                Id = "a",
                Section = StrategySection.Tcp443,
                Args = "--dpi-desync=fake --dpi-desync-ttl=4",
                Weight = 100,
                Source = CandidateSource.CommunityUnverified,
            },
            new StrategyCandidate
            {
                Id = "b",
                Section = StrategySection.Tcp443,
                Args = "--dpi-desync=multisplit",
                Weight = 90,
                Source = CandidateSource.Hypothesis,
            },
        ],
    };

    private static LearnedCandidate Learned(string args, string section = "tcp443", string isp = "test-isp")
        => new()
        {
            IspId = isp,
            CandidateId = "ogrenilmis",
            Section = section,
            Args = args,
            VerifiedFor = ["discord"],
            LastVerified = "2026-09-06",
        };

    [Fact]
    public void VarOlanAday_Dogrulanmis_Olarak_Guncellenir()
    {
        var profile = SampleProfile()
            .WithLearned([Learned("--dpi-desync=fake --dpi-desync-ttl=4")]);

        var match = profile.Candidates.Single(c => c.Args == "--dpi-desync=fake --dpi-desync-ttl=4");

        Assert.Equal(CandidateSource.Verified, match.Source);
        Assert.Equal(["discord"], match.VerifiedFor);
        Assert.Equal("2026-09-06", match.LastVerified);

        // Aday sayisi artmamali: var olan degistirildi, yenisi eklenmedi.
        Assert.Equal(2, profile.Candidates.Count);
    }

    [Fact]
    public void ListedeOlmayanAday_Eklenir()
    {
        // Genel aramada bulunan kazananlar bu yoldan giriyor: profilde yoklar.
        var profile = SampleProfile().WithLearned([Learned("--dpi-desync=fakedsplit")]);

        Assert.Equal(3, profile.Candidates.Count);
        Assert.Contains(profile.Candidates, c => c.Args == "--dpi-desync=fakedsplit");
    }

    [Fact]
    public void DogrulanmisAday_Listenin_Basina_Gecer()
    {
        // "Bu makinede gercekten calisti", toplulukta bildirilmis ya da
        // mekanizmadan turetilmis her seyden daha guclu bir kanit.
        var profile = SampleProfile().WithLearned([Learned("--dpi-desync=multisplit")]);

        var first = profile.CandidatesFor(StrategySection.Tcp443).First();

        Assert.Equal("--dpi-desync=multisplit", first.Args);
        Assert.Equal(CandidateSource.Verified, first.Source);
    }

    [Fact]
    public void BaskaIssinDogrulamasi_Sizmaz()
    {
        // Kullanici baska bir baglantida test yaptiysa o sonuc bu profile
        // uygulanmamali: strateji ISS'e bagli.
        var profile = SampleProfile()
            .WithLearned([Learned("--dpi-desync=fakedsplit", isp: "baska-isp")]);

        Assert.Equal(2, profile.Candidates.Count);
        Assert.DoesNotContain(profile.Candidates, c => c.Source == CandidateSource.Verified);
    }

    [Fact]
    public void BozukBolumAdi_Sessizce_Atlanir()
    {
        // Eski bir surumden kalan ya da elle bozulmus bir kayit yuzunden
        // uygulamanin acilmamasi kabul edilemez.
        var profile = SampleProfile()
            .WithLearned([Learned("--dpi-desync=fake", section: "boyle-bir-bolum-yok")]);

        Assert.Equal(2, profile.Candidates.Count);
    }

    [Fact]
    public void BosOgrenilmisListe_Profili_Degistirmez()
    {
        var original = SampleProfile();
        var result = original.WithLearned([]);

        Assert.Same(original, result);
    }

    [Fact]
    public void FarkliBolumdeAyniArguman_AyriAday_Sayilir()
    {
        // Ayni arguman iki bolumde gecerli olabilir; birbirinin yerine gecmemeli.
        var profile = SampleProfile()
            .WithLearned([Learned("--dpi-desync=fake --dpi-desync-ttl=4", section: "tcp80")]);

        Assert.Equal(3, profile.Candidates.Count);
        Assert.Single(profile.CandidatesFor(StrategySection.Tcp80));
        Assert.Equal(2, profile.CandidatesFor(StrategySection.Tcp443).Count());
    }
}
