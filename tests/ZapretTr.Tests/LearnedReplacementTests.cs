using ZapretTr.Core.Engine;

namespace ZapretTr.Tests;

/// <summary>
/// Yeni bir doğrulama, aynı bölümdeki eskisinin YERİNE geçiyor mu.
/// </summary>
/// <remarks>
/// Eskiden anahtar (İSS, bölüm, ARGÜMAN) idi ve sonuç şuydu: bir testte
/// "fake+ttl4" doğrulanıp kaydediliyor, engelleme değişip yeni testte
/// "multisplit pos=2" kazanıyor ve İKİSİ birden "bu bağlantıda doğrulandı"
/// etiketiyle listede duruyordu. Çalışma zamanında bölüm başına tek kazanan
/// kullanıldığı için ikinci kayıt hiçbir şey eklemiyor; yalnızca hangisinin
/// güncel olduğunu belirsizleştiriyor ve kayıtlı seçim eskisini gösterebiliyor.
///
/// Test diske DOKUNMUYOR: birleştirme kuralı saf bir metoda (ConfigStore.Merge)
/// ayrıldı, dolayısıyla GERÇEK kod sınanıyor ama %ProgramData% yoluna hiçbir
/// şey yazılmıyor.
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

        // TAM OLARAK BİR kayıt kalmalı: eski parametre artık doğru değil.
        Assert.Single(sonuc);
        Assert.Contains("multisplit", sonuc[0].Args, StringComparison.Ordinal);
    }

    [Fact]
    public void Farkli_bolumler_birbirini_silmiyor()
    {
        // tcp80, tcp443 ve quic ayrı ayrı aranıp ayrı ayrı uygulanıyor;
        // birinin güncellenmesi diğerini geçersiz kılmaz.
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
        // Kullanıcı ağ değiştirdiğinde eski ağın doğrulaması kaybolmamalı:
        // eve dönünce yeniden test yapması gerekmesin.
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
