using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Core.Engine;

/// <summary>Kullanıcının seçimleri. Uygulama kapanıp açıldığında korunur.</summary>
public sealed class AppConfig
{
    [JsonPropertyName("selectedIspId")]
    public string? SelectedIspId { get; set; }

    [JsonPropertyName("selectedStrategyId")]
    public string? SelectedStrategyId { get; set; }

    /// <summary>
    /// Seçili stratejinin argümanları.
    /// </summary>
    /// <remarks>
    /// Kimliğin yanında argümanlar da saklanıyor: bir sonraki sürümde aday kimlikleri
    /// değişirse ya da aday listeden kalkarsa, kullanıcının çalışan ayarı kimlik
    /// eşleşmedi diye kaybolmasın.
    /// </remarks>
    [JsonPropertyName("selectedStrategyArgs")]
    public string? SelectedStrategyArgs { get; set; }

    [JsonPropertyName("secureDnsEnabled")]
    public bool SecureDnsEnabled { get; set; } = true;

    [JsonPropertyName("customTarget")]
    public string? CustomTarget { get; set; }

    /// <summary>Açılışta yeni sürüm var mı diye sorulsun mu.</summary>
    /// <remarks>
    /// Uygulamanın dışarı istek yapan TEK yeri bu ve kapatılabilir olması şart:
    /// engellemenin konu olduğu bir araç, kullanıcının haberi olmadan ağ isteği
    /// yapmamalı. Varsayılan AÇIK, çünkü günde birkaç sürüm çıkabiliyor ve
    /// kullanıcıya ulaşmayan bir düzeltme işe yaramıyor; ama ne gönderildiği
    /// (yalnızca "en son sürüm ne" sorusu) README'de ve bu yorumda yazılı.
    /// </remarks>
    [JsonPropertyName("updateCheckEnabled")]
    public bool UpdateCheckEnabled { get; set; } = true;

    /// <summary>Bu yapılandırmayı en son yazan sürüm.</summary>
    /// <remarks>
    /// Güncellemeden sonra kullanıcıya "Sürüm değişti ... Kayıtlı stratejiniz korundu"
    /// diyebilmek için. Strateji SİLİNMİYOR: sürüm değişikliği DPI'ı
    /// değiştirmiyor, dolayısıyla ölçüm hâlâ geçerli. Yalnızca durum
    /// bildiriliyor; parametrenin hâlâ işe yarayıp yaramadığını başlatmadan
    /// sonraki doğrulama zaten ölçüyor.
    /// </remarks>
    [JsonPropertyName("lastRunVersion")]
    public string? LastRunVersion { get; set; }

    /// <summary>Otomatik başlatma servisi kullanıcı tarafından duraklatıldı mı.</summary>
    /// <remarks>
    /// Gerçeğin kaynağı servisin kendi başlangıç türü
    /// (<see cref="ServiceManager.GetStatusAsync"/>); bu alan yalnızca YÜKSELTMEDE
    /// gerekiyor. Kurulum paketi eski servisleri silip yeniden kuruyor ve silinen
    /// servisle birlikte "duraklatıldı" bilgisi de kayboluyordu; VPN için
    /// korumayı kapatan kullanıcı, güncellemeden sonra VPN'inin yine
    /// bağlanmadığını görürdü.
    /// </remarks>
    [JsonPropertyName("servicePaused")]
    public bool ServicePaused { get; set; }

    /// <summary>Arayüz teması: "dark", "light" ya da null (Windows'un ayarına uy).</summary>
    /// <remarks>
    /// Metin, bool değil: null "kullanıcı hiç seçmedi" demek ve o durumda uygulama
    /// Windows'un açık/koyu ayarını izliyor. bool olsaydı eski yapılandırmalar
    /// "açık temayı seçmiş" gibi okunurdu.
    /// </remarks>
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }
}

/// <summary>
/// Parametre testinde BU BAĞLANTIDA doğrulanmış bir aday.
/// </summary>
/// <remarks>
/// Dağıtımla gelen profillere yazılmıyor; onlar depo dosyaları ve üretecin
/// çıktısı. Kullanıcıya ait olan bu bilgi ayrı duruyor ve yüklemede üzerine
/// bindiriliyor. Böylece upstream profil güncellemesi kullanıcının kendi
/// doğrulamalarını silmiyor.
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
/// Kullanıcı durumunu diske yazar ve okur.
/// </summary>
/// <remarks>
/// Bu sınıf olmadan uygulama her açılışta sıfırdan başlıyordu: kullanıcı
/// parametre testini çalıştırıp çalışan bir strateji buluyor, uygulamayı
/// kapatıyor ve bulunan her şey kayboluyordu. Testin 2-3 dakika sürdüğü
/// düşünülürse bu, aracı günlük kullanım için neredeyse kullanılamaz yapıyordu.
///
/// Okuma yolları HİÇBİR ZAMAN fırlatmaz. Bozuk ya da eski sürümden kalma bir
/// yapılandırma yüzünden uygulamanın açılmaması kabul edilemez; böyle bir durumda
/// varsayılanlara dönülür.
/// </remarks>
[SupportedOSPlatform("windows")]
public static class ConfigStore
{

    public static string ConfigPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "config.json");

    public static string LearnedPath => Path.Combine(WinDivertCleanup.ConfigDirectory, "learned.json");

    /// <summary>Kayıtlı yapılandırmayı okur. Yoksa ya da bozuksa varsayılanları döner.</summary>
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
            // Bozuk yapılandırma uygulamanın açılmasını engellememeli.
            return new AppConfig();
        }
    }

    public static void Save(AppConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Directory.CreateDirectory(WinDivertCleanup.ConfigDirectory);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(config, CoreJsonContext.Default.AppConfig));
    }

    /// <summary>Öğrenilmiş doğrulamaları okur. Yoksa ya da bozuksa boş liste döner.</summary>
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
    /// Yeni doğrulamaları kaydeder; aynı İSS + BÖLÜM için eskisinin yerine geçer.
    /// </summary>
    /// <remarks>
    /// Anahtar (İSS, bölüm); argüman DEĞİL. Çalışma zamanında bölüm başına tek
    /// kazanan kullanılıyor, dolayısıyla aynı bölüm için ikinci bir "doğrulandı"
    /// kaydı hiçbir şey eklemiyor; yalnızca hangisinin güncel olduğunu
    /// belirsizleştiriyor.
    ///
    /// "Eskiden çalışıyordu" bir kanıt değil: o ölçüm artık geçerli olmayan bir
    /// ağ durumuna aitti ve engelleme değiştiğinde yanıltıcı hâle geliyor.
    /// </remarks>
    /// <summary>
    /// Yeni doğrulamaları mevcutlarla birleştirir. Diske DOKUNMAZ.
    /// </summary>
    /// <remarks>
    /// Ayrı durmasının sebebi sınanabilirlik: kural <see cref="AddLearned"/>
    /// içinde gömülü kaldığında test onu ancak KOPYALAYARAK sınayabiliyordu
    /// ve kopyalayan bir test, kod değiştiğinde sessizce yeşil kalır.
    /// </remarks>
    public static List<LearnedCandidate> Merge(
        IEnumerable<LearnedCandidate> existing, IEnumerable<LearnedCandidate> incoming)
    {
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(incoming);

        var merged = existing.ToList();

        foreach (var candidate in incoming)
        {
            // Anahtar (İSS, BÖLÜM); argüman DEĞİL.
            //
            // Eskiden args de karşılaştırılıyordu ve sonuç şuydu: bir testte
            // "fake+ttl4" doğrulanıp kaydediliyor, engelleme değişip yeni testte
            // "multisplit pos=2" kazanıyor ve İKİSİ birden "bu bağlantıda
            // doğrulandı" etiketiyle listede duruyordu. Çalışma zamanında bölüm
            // başına tek kazanan kullanıldığı için ikinci kayıt hiçbir şey
            // eklemiyor; yalnızca hangisinin güncel olduğunu belirsizleştiriyor
            // ve kayıtlı seçim eskisini gösterebiliyordu.
            //
            // "Eskiden çalışıyordu" bir kanıt değil: o ölçüm artık geçerli
            // olmayan bir ağ durumuna aitti.
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
