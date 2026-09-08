using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Core.Engine;

/// <summary>Kullanicinin secimleri. Uygulama kapanip acildiginda korunur.</summary>
public sealed class AppConfig
{
    [JsonPropertyName("selectedIspId")]
    public string? SelectedIspId { get; set; }

    [JsonPropertyName("selectedStrategyId")]
    public string? SelectedStrategyId { get; set; }

    /// <summary>
    /// Secili stratejinin argumanlari.
    /// </summary>
    /// <remarks>
    /// Id'nin yaninda argumanlar da saklaniyor: bir sonraki surumde aday id'leri
    /// degisirse ya da aday listeden kalkarsa, kullanicinin calisan ayari id
    /// eslesmedi diye kaybolmasin.
    /// </remarks>
    [JsonPropertyName("selectedStrategyArgs")]
    public string? SelectedStrategyArgs { get; set; }

    [JsonPropertyName("secureDnsEnabled")]
    public bool SecureDnsEnabled { get; set; } = true;

    [JsonPropertyName("customTarget")]
    public string? CustomTarget { get; set; }

    /// <summary>Acilista yeni surum var mi diye sorulsun mu.</summary>
    /// <remarks>
    /// Uygulamanin disari istek yapan TEK yeri bu ve kapatilabilir olmasi sart:
    /// engellemenin konu oldugu bir arac, kullanicinin haberi olmadan ag istegi
    /// yapmamali. Varsayilan ACIK, cunku gunde birkac surum cikabiliyor ve
    /// kullaniciya ulasmayan bir duzeltme ise yaramiyor -- ama ne gonderildigi
    /// (yalnizca "en son surum ne" sorusu) README'de ve bu yorumda yazili.
    /// </remarks>
    [JsonPropertyName("updateCheckEnabled")]
    public bool UpdateCheckEnabled { get; set; } = true;

    /// <summary>Bu yapilandirmayi en son yazan surum.</summary>
    /// <remarks>
    /// Guncellemeden sonra kullaniciya "sürüm degisti, stratejiniz korundu"
    /// diyebilmek icin. Strateji SILINMIYOR: surum degisikligi DPI'yi
    /// degistirmiyor, dolayisiyla olcum hala gecerli. Yalnizca durum
    /// bildiriliyor; parametrenin hala ise yarayip yaramadigini baslatmadan
    /// sonraki dogrulama zaten olcuyor.
    /// </remarks>
    [JsonPropertyName("lastRunVersion")]
    public string? LastRunVersion { get; set; }
}

/// <summary>
/// Parametre testinde BU BAGLANTIDA dogrulanmis bir aday.
/// </summary>
/// <remarks>
/// Dagitimla gelen profillere yazilmiyor; onlar depo dosyalari ve uretecin
/// ciktisi. Kullaniciya ait olan bu bilgi ayri duruyor ve yuklemede uzerine
/// bindiriliyor. Boylece upstream profil guncellemesi kullanicinin kendi
/// dogrulamalarini silmiyor.
/// </remarks>
public sealed class LearnedCandidate
{
    [JsonPropertyName("ispId")]
    public required string IspId { get; init; }

    [JsonPropertyName("candidateId")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("args")]
    public required string Args { get; init; }

    [JsonPropertyName("verifiedFor")]
    public IReadOnlyList<string> VerifiedFor { get; init; } = Array.Empty<string>();

    [JsonPropertyName("lastVerified")]
    public string LastVerified { get; init; } = string.Empty;
}

/// <summary>
/// Kullanici durumunu diske yazar ve okur.
/// </summary>
/// <remarks>
/// Bu sinif olmadan uygulama her acilista sifirdan basliyordu: kullanici
/// parametre testini calistirip calisan bir strateji buluyor, uygulamayi
/// kapatiyor ve bulunan her sey kayboluyordu. Testin 2-3 dakika surdugu
/// dusunulurse bu, araci gunluk kullanim icin neredeyse kullanilamaz yapiyordu.
///
/// Okuma yollari HICBIR ZAMAN firlatmaz. Bozuk ya da eski surumden kalma bir
/// yapilandirma yuzunden uygulamanin acilmamasi kabul edilemez; boyle bir durumda
/// varsayilanlara donulur.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ConfigStore
{

    public static string ConfigPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "config.json");

    public static string LearnedPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "learned.json");

    /// <summary>Kayitli yapilandirmayi okur. Yoksa ya da bozuksa varsayilanlari doner.</summary>
    public static AppConfig Load()
    {
        try
        {
            if (!File.Exists(ConfigPath))
            {
                return new AppConfig();
            }

            return JsonSerializer.Deserialize(File.ReadAllText(ConfigPath), CoreJsonContext.Default.AppConfig)
                   ?? new AppConfig();
        }
        catch (Exception)
        {
            // Bozuk yapilandirma uygulamanin acilmasini engellememeli.
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, CoreJsonContext.Default.AppConfig));
    }

    /// <summary>Ogrenilmis dogrulamalari okur. Yoksa ya da bozuksa bos liste doner.</summary>
    public static IReadOnlyList<LearnedCandidate> LoadLearned()
    {
        try
        {
            if (!File.Exists(LearnedPath))
            {
                return [];
            }

            return JsonSerializer.Deserialize(
                       File.ReadAllText(LearnedPath), CoreJsonContext.Default.ListLearnedCandidate) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>
    /// Yeni dogrulamalari kaydeder; ayni ISS + BOLUM icin eskisinin yerine gecer.
    /// </summary>
    /// <remarks>
    /// Anahtar (ISS, bolum) -- arguman DEGIL. Calisma zamaninda bolum basina tek
    /// kazanan kullaniliyor, dolayisiyla ayni bolum icin ikinci bir "dogrulandi"
    /// kaydi hicbir sey eklemiyor; yalnizca hangisinin guncel oldugunu
    /// belirsizlestiriyor.
    ///
    /// "Eskiden calisiyordu" bir kanit degil: o olcum artik gecerli olmayan bir
    /// ag durumuna aitti ve engelleme degistiginde yaniltici hale geliyor.
    /// </remarks>
    /// <summary>
    /// Yeni dogrulamalari mevcutlarla birlestirir. Diske DOKUNMAZ.
    /// </summary>
    /// <remarks>
    /// Ayri durmasinin sebebi sinanabilirlik: kural <see cref="AddLearned"/>
    /// icinde gomulu kaldiginda test onu ancak KOPYALAYARAK sinayabiliyordu,
    /// ve kopyalayan bir test kod degistiginde sessizce yesil kalir.
    /// </remarks>
    public static List<LearnedCandidate> Merge(
        IEnumerable<LearnedCandidate> existing, IEnumerable<LearnedCandidate> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var merged = existing.ToList();

        foreach (var candidate in incoming)
        {
            // Anahtar (ISS, BOLUM) -- arguman DEGIL.
            //
            // Eskiden args de karsilastiriliyordu ve sonuc suydu: bir testte
            // "fake+ttl4" dogrulanip kaydediliyor, engelleme degisip yeni testte
            // "multisplit pos=2" kazaniyor, ve IKISI birden "bu baglantida
            // dogrulandi" etiketiyle listede duruyordu. Calisma zamaninda bolum
            // basina tek kazanan kullanildigi icin ikinci kayit hicbir sey
            // eklemiyor; yalnizca hangisinin guncel oldugunu belirsizlestiriyor
            // ve kayitli secim eskisini gosterebiliyordu.
            //
            // "Eskiden calisiyordu" bir kanit degil: o olcum artik gecerli
            // olmayan bir ag durumuna aitti.
            merged.RemoveAll(mevcut =>
                string.Equals(mevcut.IspId, candidate.IspId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(mevcut.Section, candidate.Section, StringComparison.Ordinal));

            merged.Add(candidate);
        }

        return merged;
    }

    public static void AddLearned(IEnumerable<LearnedCandidate> newlyVerified)
    {
        ArgumentNullException.ThrowIfNull(newlyVerified);

        var incoming = newlyVerified.ToList();
        if (incoming.Count == 0)
        {
            return;
        }

        var merged = Merge(LoadLearned(), incoming);

        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        File.WriteAllText(LearnedPath, JsonSerializer.Serialize(merged, CoreJsonContext.Default.ListLearnedCandidate));
    }
}
