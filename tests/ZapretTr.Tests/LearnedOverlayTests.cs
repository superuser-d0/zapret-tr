using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// Kullanıcının kendi doğruladıklarının dağıtım profilleri üzerine bindirilmesi.
/// </summary>
/// <remarks>
/// Bu katman olmadan uygulama her açılışta sıfırdan başlıyordu: parametre testi
/// 2-3 dakika koşup çalışan bir strateji buluyor, uygulama kapanınca kayboluyordu.
///
/// Öğrenilenler dağıtımla gelen profillerin İÇİNE yazılmıyor; o dosyalar depo
/// dosyaları ve üretecin çıktısı. Ayrı durup yüklemede bindirilmeleri, upstream
/// profil güncellemesinin kullanıcının kendi doğrulamalarını silmemesini sağlıyor.
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

        // Aday sayısı artmamalı: var olan değiştirildi, yenisi eklenmedi.
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
        // "Bu makinede gerçekten çalıştı", toplulukta bildirilmiş ya da
        // mekanizmadan türetilmiş her şeyden daha güçlü bir kanıt.
        var profile = SampleProfile().WithLearned([Learned("--dpi-desync=multisplit")]);

        var first = profile.CandidatesFor(StrategySection.Tcp443).First();

        Assert.Equal("--dpi-desync=multisplit", first.Args);
        Assert.Equal(CandidateSource.Verified, first.Source);
    }

    [Fact]
    public void BaskaIssinDogrulamasi_Sizmaz()
    {
        // Kullanıcı başka bir bağlantıda test yaptıysa o sonuç bu profile
        // uygulanmamalı: strateji İSS'e bağlı.
        var profile = SampleProfile()
            .WithLearned([Learned("--dpi-desync=fakedsplit", isp: "baska-isp")]);

        Assert.Equal(2, profile.Candidates.Count);
        Assert.DoesNotContain(profile.Candidates, c => c.Source == CandidateSource.Verified);
    }

    [Fact]
    public void BozukBolumAdi_Sessizce_Atlanir()
    {
        // Eski bir sürümden kalan ya da elle bozulmuş bir kayıt yüzünden
        // uygulamanın açılmaması kabul edilemez.
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
        // Aynı argüman iki bölümde geçerli olabilir; birbirinin yerine geçmemeli.
        var profile = SampleProfile()
            .WithLearned([Learned("--dpi-desync=fake --dpi-desync-ttl=4", section: "tcp80")]);

        Assert.Equal(3, profile.Candidates.Count);
        Assert.Single(profile.CandidatesFor(StrategySection.Tcp80));
        Assert.Equal(2, profile.CandidatesFor(StrategySection.Tcp443).Count());
    }
}
