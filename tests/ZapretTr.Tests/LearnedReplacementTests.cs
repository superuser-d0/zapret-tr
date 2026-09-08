using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Yeni bir dogrulama, ayni bolumdeki eskisinin YERINE geciyor mu.
/// </summary>
/// <remarks>
/// Eskiden anahtar (ISS, bolum, ARGUMAN) idi ve sonuc suydu: bir testte
/// "fake+ttl4" dogrulanip kaydediliyor, engelleme degisip yeni testte
/// "multisplit pos=2" kazaniyor, ve IKISI birden "bu baglantida dogrulandi"
/// etiketiyle listede duruyordu. Calisma zamaninda bolum basina tek kazanan
/// kullanildigi icin ikinci kayit hicbir sey eklemiyor -- yalnizca hangisinin
/// guncel oldugunu belirsizlestiriyor ve kayitli secim eskisini gosterebiliyor.
///
/// Test diske DOKUNMUYOR: birlestirme kurali saf bir metoda (ConfigStore.Merge)
/// ayrildi, dolayisiyla GERCEK kod sinaniyor ama %ProgramData% yoluna hicbir
/// sey yazilmiyor.
/// </remarks>
public sealed class LearnedReplacementTests
{
    private static LearnedCandidate Aday(string isp, string bolum, string args) => new()
    {
        IspId = isp,
        CandidateId = args.GetHashCode(StringComparison.Ordinal).ToString("x8"),
        Section = bolum,
        Args = args,
        VerifiedFor = ["discord"],
        LastVerified = "2026-09-09",
    };


    [Fact]
    public void Ayni_bolumde_yeni_aday_eskisini_siliyor()
    {
        var mevcut = new List<LearnedCandidate>
        {
            Aday("turk-telekom", "tcp443", "--dpi-desync=fake --dpi-desync-ttl=4"),
        };

        var sonuc = ConfigStore.Merge(mevcut,
        [
            Aday("turk-telekom", "tcp443", "--dpi-desync=multisplit --dpi-desync-split-pos=2"),
        ]);

        // TAM OLARAK BIR kayit kalmali: eski parametre artik dogru degil.
        Assert.Single(sonuc);
        Assert.Contains("multisplit", sonuc[0].Args, StringComparison.Ordinal);
    }

    [Fact]
    public void Farkli_bolumler_birbirini_silmiyor()
    {
        // tcp80, tcp443 ve quic ayri ayri aranip ayri ayri uygulaniyor;
        // birinin guncellenmesi digerini gecersiz kilmaz.
        var mevcut = new List<LearnedCandidate>
        {
            Aday("turk-telekom", "tcp80", "--dpi-desync=fake,fakedsplit"),
            Aday("turk-telekom", "quic", "--dpi-desync=fake --dpi-desync-any-protocol=1"),
        };

        var sonuc = ConfigStore.Merge(mevcut,
        [
            Aday("turk-telekom", "tcp443", "--dpi-desync=fake --dpi-desync-ttl=4"),
        ]);

        Assert.Equal(3, sonuc.Count);
        Assert.Equal(
            new[] { "tcp80", "quic", "tcp443" },
            sonuc.Select(x => x.Section).ToArray());
    }

    [Fact]
    public void Farkli_saglayicilar_birbirini_silmiyor()
    {
        // Kullanici ag degistirdiginde eski agin dogrulamasi kaybolmamali:
        // eve donunce yeniden test yapmasi gerekmesin.
        var mevcut = new List<LearnedCandidate>
        {
            Aday("turk-telekom", "tcp443", "--dpi-desync=fake --dpi-desync-ttl=4"),
        };

        var sonuc = ConfigStore.Merge(mevcut,
        [
            Aday("turkcell-mobil", "tcp443", "--dpi-desync=multisplit"),
        ]);

        Assert.Equal(2, sonuc.Count);
    }

    [Fact]
    public void Ayni_aday_tekrar_dogrulanirsa_cogalmiyor()
    {
        var args = "--dpi-desync=fake --dpi-desync-ttl=4";
        var mevcut = new List<LearnedCandidate> { Aday("turk-telekom", "tcp443", args) };

        var sonuc = ConfigStore.Merge(mevcut, [Aday("turk-telekom", "tcp443", args)]);

        Assert.Single(sonuc);
    }
}
