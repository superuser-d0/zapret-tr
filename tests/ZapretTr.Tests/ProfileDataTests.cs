using ZapretTr.Core.Profiles;

namespace ZapretTr.Tests;

/// <summary>
/// Depoyla birlikte gelen gerçek profil verisini doğrular.
/// </summary>
/// <remarks>
/// Bu testler kod değil VERİ test ediyor. Profil veritabanı projenin asıl değeri ve
/// elle düzenlenecek; bir aday eklenirken bölüm adının yanlış yazılması ya da iki adaya
/// aynı kimlik verilmesi gibi hataların çalışma zamanında değil test zamanında
/// yakalanması gerekiyor.
/// </remarks>
public sealed class ProfileDataTests
{
    private static readonly ProfileStore Store = ProfileStore.Load();

    [Fact]
    public void TumProfiller_Yuklenebiliyor()
    {
        Assert.NotEmpty(Store.Profiles);
        Assert.All(Store.Profiles, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Id));
            Assert.False(string.IsNullOrWhiteSpace(p.DisplayName));
            Assert.NotEmpty(p.Candidates);
        });
    }

    [Fact]
    public void ProfilIdleri_Benzersiz()
    {
        var ids = Store.Profiles.Select(p => p.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void AdayIdleri_ProfilIcinde_Benzersiz()
    {
        foreach (var profile in Store.Profiles)
        {
            var ids = profile.Candidates.Select(c => c.Id).ToList();
            Assert.True(
                ids.Count == ids.Distinct(StringComparer.Ordinal).Count(),
                $"{profile.Id}: tekrar eden aday id var");
        }
    }

    [Fact]
    public void HerAday_BosOlmayanArgumanaSahip()
    {
        foreach (var candidate in Store.Profiles.SelectMany(p => p.Candidates))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(candidate.Args),
                $"{candidate.Id}: bos arguman");
            Assert.StartsWith("--", candidate.Args, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void HerProfil_En_Az_Tcp443_Adayi_Iceriyor()
    {
        // 443 asıl savaş alanı. Bir profilin 80/quic bölümü boş olabilir ama
        // 443'ü boşsa o profil pratikte işe yaramaz.
        foreach (var profile in Store.Profiles)
        {
            Assert.True(
                profile.CandidatesFor(StrategySection.Tcp443).Any(),
                $"{profile.Id}: tcp443 bolumu bos");
        }
    }

    [Fact]
    public void Adaylar_AgirligaGore_AzalanSirada_Geliyor()
    {
        foreach (var profile in Store.Profiles)
        {
            foreach (var section in Enum.GetValues<StrategySection>())
            {
                var weights = profile.CandidatesFor(section).Select(c => c.Weight).ToList();
                Assert.Equal(weights.OrderByDescending(w => w).ToList(), weights);
            }
        }
    }

    // --- Superonline: öncelikli profil, ayrı ve daha sıkı kontroller ------------

    [Fact]
    public void Superonline_Profili_Var_ve_DortBolumu_de_Dolu()
    {
        var sol = Store.FindById("superonline");
        Assert.NotNull(sol);

        foreach (var section in Enum.GetValues<StrategySection>())
        {
            Assert.True(
                sol.CandidatesFor(section).Any(),
                $"Superonline: {section.ToJsonName()} bolumu bos");
        }
    }

    [Fact]
    public void Superonline_Md5sig_Disinda_Alternatif_Iceriyor()
    {
        // Bu testin var olma sebebi tasarımın özü: upstream belgesine göre md5sig
        // yalnızca sunucu TCP MD5 seçeneğini reddettiğinde işe yarar. Yani md5sig
        // hedefe göre çalışabilir ya da çalışmayabilir. Superonline merdiveni md5sig'e
        // indirgenirse md5sig'in tutmadığı hedeflerde profil boşa düşer.
        var sol = Store.FindById("superonline");
        Assert.NotNull(sol);

        var tls = sol.CandidatesFor(StrategySection.Tcp443).ToList();

        Assert.Contains(tls, c => c.Args.Contains("md5sig", StringComparison.Ordinal));
        Assert.Contains(tls, c => !c.Args.Contains("md5sig", StringComparison.Ordinal));
    }

    [Fact]
    public void Superonline_SahtePaket_Kullanmayan_Aile_Iceriyor()
    {
        // Aynı gerekçe, farklı eksen: DPI sahte paketleri eliyorsa (TTL/checksum
        // doğruluyorsa) "fake" tabanlı adayların HEPSİ birden çöker. O durumda
        // çalışacak tek şey saf bölme; merdivende mutlaka bulunmalı.
        var sol = Store.FindById("superonline");
        Assert.NotNull(sol);

        Assert.Contains(
            sol.CandidatesFor(StrategySection.Tcp443),
            c => !c.Args.Contains("dpi-desync=fake", StringComparison.Ordinal));
    }

    [Fact]
    public void Superonline_Asn_ile_Eslesiyor()
    {
        // AS34984 = TELLCOM-AS / Turkcell Superonline.
        var matches = Store.Match(asn: 34984, orgName: null);
        Assert.NotEmpty(matches);
        Assert.Equal("superonline", matches[0].Id);
    }

    [Fact]
    public void Superonline_KurulusAdi_ile_de_Eslesiyor()
    {
        // ASN bilinmiyorsa kuruluş adı yedek yol. Gerçek ip-api çıktısına benzer bir dizgi.
        var matches = Store.Match(asn: null, orgName: "Superonline Iletisim Hizmetleri A.S.");
        Assert.Contains(matches, p => p.Id == "superonline");
    }

    [Fact]
    public void TurkTelekom_Asn_ile_Eslesiyor()
    {
        // AS9121 = TTNet. Geliştirme makinesinin bağlantısı.
        var matches = Store.Match(asn: 9121, orgName: null);
        Assert.NotEmpty(matches);
        Assert.Equal("turk-telekom", matches[0].Id);
    }

    [Fact]
    public void EslesmeYoksa_BosListe_Doner()
    {
        var matches = Store.Match(asn: 15169, orgName: "Google LLC");
        Assert.Empty(matches);
    }
}

