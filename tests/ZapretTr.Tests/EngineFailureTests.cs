using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Motor art arda hic baslamadiginda aramanin durmasi.
/// </summary>
/// <remarks>
/// Gercek bir kullanicida 304 aday 23 SANIYEDE "denendi" ve hepsi ayni sebeple
/// dustu: winws hic baslamadi. Arayuz sonunda "CALISAN STRATEJI YOK" dedi,
/// kullanici da bu hatta aracin yetmedigini dusundu. Dogru cumle "hicbir
/// strateji DENENEMEDI" idi.
///
/// Bu kontrol iki yonlu tehlikeli, testler de o yuzden iki yonlu: cok gec
/// tetiklenirse kullanici dakikalarca bosa bekler, cok erken tetiklenirse
/// GERCEK aramayi keser ve calisan bir strateji varken "olcum yapilamadi" der.
/// Ikincisi daha kotu.
/// </remarks>
public sealed class EngineFailureTests
{
    private static CandidateResult Sonuc(bool basarili, string detay) => new(
        CandidateId: "x",
        Args: "--dpi-desync=fake",
        Section: StrategySection.Tcp443,
        TargetHost: "discord.com",
        TargetCategory: "discord",
        Succeeded: basarili,
        Detail: detay,
        Duration: TimeSpan.FromMilliseconds(90));

    private static CandidateResult MotorHatasi() =>
        Sonuc(false, StrategyProber.EngineFailurePrefix +
                     "winws baslar baslamaz 34 koduyla kapandi.");

    [Fact]
    public void Art_arda_motor_hatasi_aramayi_durdurur()
    {
        var denemeler = Enumerable.Range(0, StrategyProber.MotorHataEsigi)
            .Select(_ => MotorHatasi())
            .ToList();

        Assert.True(StrategyProber.MotorSurekliDusuyor(denemeler, out var sebep));
        Assert.Contains("34", sebep, StringComparison.Ordinal);
    }

    [Fact]
    public void Esigin_altinda_durdurmaz()
    {
        // Tek tuk basarisizlik normal: gecici kilit, gecersiz parametre.
        var denemeler = Enumerable.Range(0, StrategyProber.MotorHataEsigi - 1)
            .Select(_ => MotorHatasi())
            .ToList();

        Assert.False(StrategyProber.MotorSurekliDusuyor(denemeler, out _));
    }

    [Fact]
    public void Son_pencerede_basari_varsa_durdurmaz()
    {
        // EN ONEMLI DURUM: motor CALISIYOR. Burada durmak, gercekten calisan
        // bir strateji varken aramayi kesip "olcum yapilamadi" demek olurdu.
        // Ilk hali bu senaryoyu kurmuyordu: basariyi pencerenin DISINA
        // koydugum icin kontrol hic sinanmiyordu ve mutasyon yakalanmadi.
        var denemeler = new List<CandidateResult>();
        denemeler.AddRange(Enumerable.Range(0, StrategyProber.MotorHataEsigi - 1)
            .Select(_ => MotorHatasi()));
        denemeler.Add(Sonuc(true, null!));

        Assert.False(StrategyProber.MotorSurekliDusuyor(denemeler, out _));
    }
    [Fact]
    public void Baska_sebeple_dusenler_motor_hatasi_sayilmaz()
    {
        // RST demek motorun CALISTIGI demek: paket gitti, DPI sifirladi. Bu,
        // aranan bilginin ta kendisi -- durdurulacak sey degil.
        var denemeler = Enumerable.Range(0, StrategyProber.MotorHataEsigi)
            .Select(_ => Sonuc(false, "baglanti sifirlandi (RST)"))
            .ToList();

        Assert.False(StrategyProber.MotorSurekliDusuyor(denemeler, out _));
    }

    [Fact]
    public void Karisik_sebepler_durdurmaz()
    {
        var denemeler = Enumerable.Range(0, StrategyProber.MotorHataEsigi)
            .Select(i => i % 2 == 0
                ? MotorHatasi()
                : Sonuc(false, "zaman asimi"))
            .ToList();

        Assert.False(StrategyProber.MotorSurekliDusuyor(denemeler, out _));
    }
}
