using System.IO;
using System.Text.RegularExpressions;
using ZapretTr.App.ViewModels;
using ZapretTr.Core.Engine;
using ZapretTr.Core.Profiles;
using ZapretTr.Prober;
using IoPath = System.IO.Path;

namespace ZapretTr.Tests;

/// <summary>
/// Uygulamanın bağlandığı dış adresler belgede yazılı olmalı; ölçümler olduklarından fazlasını söylememeli.
/// </summary>
/// <remarks>
/// Dışarıdan bir kod incelemesi iki şeyi yakaladı:
///
///   1. Ayrıntılı rehber, güncelleme denetimi kapatılınca uygulamanın "hiçbir ağ isteği
///      yapmadığını" söylüyordu. Parametre testi IP adresini İSS tespit servislerine
///      gönderiyor, DNS'i Cloudflare'e ya da Google'a soruyor; şifreli DNS sunucu listesi
///      indiriyor. Hiçbiri belgede yoktu. İSS tespitinde ilk sorulan kaynak da şifresizdi.
///   2. discord-voice bölümü STUN ile ölçülüyor, ama günlük "Discord ses" yazıyor ve kayıt
///      "doğrulandı" diyordu.
///
/// İkisi de derlemeyi ve testleri geçiyordu: yanlış olan kod değil, kodun kendisi hakkında
/// söylediği şeydi.
/// </remarks>
public sealed class NetworkDisclosureTests
{
    [Fact]
    public void Koddaki_her_dis_adres_ayrintili_rehberde_geciyor()
    {
        var kok = XmlCommentTests.RepoRoot;
        var rehber = File.ReadAllText(IoPath.Combine(kok, "README-DETAYLI.md"));
        var adres = new Regex("[\"']https?://([a-z0-9.-]+)", RegexOptions.IgnoreCase);

        var eksik = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dosya in Directory.EnumerateFiles(IoPath.Combine(kok, "src"), "*.cs", SearchOption.AllDirectories))
        {
            if (dosya.Contains($"{IoPath.DirectorySeparatorChar}obj{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal)
                || dosya.Contains($"{IoPath.DirectorySeparatorChar}bin{IoPath.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var satir in File.ReadLines(dosya))
            {
                var kirpik = satir.TrimStart();
                if (kirpik.StartsWith("//", StringComparison.Ordinal))
                {
                    continue;
                }

                foreach (Match m in adres.Matches(satir))
                {
                    var host = m.Groups[1].Value;

                    // Ağ isteği değil: zamanlanmış görev XML'inin ad alanı.
                    if (host.Equals("schemas.microsoft.com", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    // GitHub adresleri (güncelleme, yayın, hata formu, dnscrypt listesi)
                    // rehberde "GitHub" olarak geçiyor.
                    var bulundu = host.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)
                                  || host.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase)
                        ? rehber.Contains("GitHub", StringComparison.Ordinal)
                        : rehber.Contains(host, StringComparison.OrdinalIgnoreCase);

                    if (!bulundu)
                    {
                        eksik.Add($"{host} ({IoPath.GetFileName(dosya)})");
                    }
                }
            }
        }

        Assert.True(eksik.Count == 0,
            "README-DETAYLI.md 'Uygulama internete bir şey gönderiyor mu?' bölümünde adı geçmeyen dış adresler: "
            + string.Join(", ", eksik));
    }

    [Fact]
    public void Iss_tespitinde_ilk_kaynak_sifreli()
    {
        Assert.StartsWith("https://", IspDetector.QueryOrder[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Stun_olcumu_Discord_sesi_dogrulanmis_gibi_gostermiyor()
    {
        Assert.DoesNotContain("Discord", MainViewModel.SectionLabel(StrategySection.DiscordVoice), StringComparison.Ordinal);

        var not = IspProfile.LearnedNote(StrategySection.DiscordVoice);
        Assert.Contains("UDP", not, StringComparison.Ordinal);
        Assert.Contains("dogrulanmadi", not, StringComparison.Ordinal);

        // Diğer bölümlerin notu değişmedi.
        Assert.Equal("Bu baglantida parametre testiyle dogrulandi.", IspProfile.LearnedNote(StrategySection.Tcp443));
    }

    [Fact]
    public void Ogrenilmis_ses_adayi_kayit_notunda_da_abartmiyor()
    {
        var profil = new IspProfile
        {
            Id = "test-isp",
            DisplayName = "Test",
            Candidates = [],
        }.WithLearned(
        [
            new LearnedCandidate
            {
                IspId = "test-isp",
                CandidateId = "ses",
                Section = "discord-voice",
                Args = "--dpi-desync=fake",
                VerifiedFor = ["discord-voice"],
                LastVerified = "2026-09-14",
            },
        ]);

        var aday = Assert.Single(profil.Candidates);

        // Kaynak Verified kalıyor: ölçümü geçti ve çalışma zamanında geçmeyenlerden önce gelmeli.
        Assert.Equal(CandidateSource.Verified, aday.Source);
        Assert.Equal(IspProfile.LearnedNote(StrategySection.DiscordVoice), aday.Note);
    }
}
