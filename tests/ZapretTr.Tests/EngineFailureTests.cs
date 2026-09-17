using ZapretTr.Core.Profiles;
using ZapretTr.Prober;

namespace ZapretTr.Tests;

/// <summary>
/// Motor art arda hiç başlamadığında aramanın durması.
/// </summary>
/// <remarks>
/// Gerçek bir kullanıcıda 304 aday 23 SANİYEDE "denendi" ve hepsi aynı sebeple
/// düştü: winws hiç başlamadı. Arayüz sonunda "ÇALIŞAN STRATEJİ YOK" dedi,
/// kullanıcı da bu hatta aracın yetmediğini düşündü. Doğru cümle "hiçbir
/// strateji DENENEMEDİ" idi.
///
/// Bu kontrol iki yönlü tehlikeli, testler de o yüzden iki yönlü: çok geç
/// tetiklenirse kullanıcı dakikalarca boşa bekler, çok erken tetiklenirse
/// GERÇEK aramayı keser ve çalışan bir strateji varken "ölçüm yapılamadı" der.
/// İkincisi daha kötü.
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
        // Tek tük başarısızlık normal: geçici kilit, geçersiz parametre.
        var denemeler = Enumerable.Range(0, StrategyProber.MotorHataEsigi - 1)
            .Select(_ => MotorHatasi())
            .ToList();

        Assert.False(StrategyProber.MotorSurekliDusuyor(denemeler, out _));
    }

    [Fact]
    public void Son_pencerede_basari_varsa_durdurmaz()
    {
        // EN ÖNEMLİ DURUM: motor ÇALIŞIYOR. Burada durmak, gerçekten çalışan
        // bir strateji varken aramayı kesip "ölçüm yapılamadı" demek olurdu.
        // İlk hâli bu senaryoyu kurmuyordu: başarıyı pencerenin DIŞINA
        // koyduğum için kontrol hiç sınanmıyordu ve mutasyon yakalanmadı.
        var denemeler = new List<CandidateResult>();
        denemeler.AddRange(Enumerable.Range(0, StrategyProber.MotorHataEsigi - 1)
            .Select(_ => MotorHatasi()));
        denemeler.Add(Sonuc(true, null!));

        Assert.False(StrategyProber.MotorSurekliDusuyor(denemeler, out _));
    }
    [Fact]
    public void Baska_sebeple_dusenler_motor_hatasi_sayilmaz()
    {
        // RST demek motorun ÇALIŞTIĞI demek: paket gitti, DPI sıfırladı. Bu,
        // aranan bilginin ta kendisi; durdurulacak şey değil.
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
