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
    /// Yeni dogrulamalari kaydeder; ayni ISS + bolum + arguman varsa uzerine yazar.
    /// </summary>
    /// <remarks>
    /// Ayni stratejinin her testte yeniden eklenmesi dosyayi sisirir ve hangisinin
    /// guncel oldugunu belirsizlestirir. Anahtar olarak arguman kullaniliyor cunku
    /// asil kimlik o: aday id'si profil surumleri arasinda degisebilir.
    /// </remarks>
    public static void AddLearned(IEnumerable<LearnedCandidate> newlyVerified)
    {
        ArgumentNullException.ThrowIfNull(newlyVerified);

        var incoming = newlyVerified.ToList();
        if (incoming.Count == 0)
        {
            return;
        }

        var merged = LoadLearned().ToList();

        foreach (var candidate in incoming)
        {
            merged.RemoveAll(existing =>
                string.Equals(existing.IspId, candidate.IspId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(existing.Section, candidate.Section, StringComparison.Ordinal)
                && string.Equals(existing.Args, candidate.Args, StringComparison.Ordinal));

            merged.Add(candidate);
        }

        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        File.WriteAllText(LearnedPath, JsonSerializer.Serialize(merged, CoreJsonContext.Default.ListLearnedCandidate));
    }
}
