using System.Text.Json;
using ZapretTr.Core.Engine;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// İSS profillerini ve genel merdiveni diskten yükler, ASN/kuruluş adına göre eşleştirir.
/// </summary>
public sealed class ProfileStore
{
    private ProfileStore(IReadOnlyList<IspProfile> profiles, GenericLadder ladder, string root)
    {
        Profiles = profiles;
        Ladder = ladder;
        Root = root;
    }

    /// <summary>Yüklendiği profiles/ dizini.</summary>
    public string Root { get; }

    /// <summary>Tüm İSS profilleri, priority sırasında (küçük önce).</summary>
    public IReadOnlyList<IspProfile> Profiles { get; }

    public GenericLadder Ladder { get; }

    /// <param name="learned">
    /// Kullanıcının kendi testlerinde doğruladığı adaylar. Dağıtımla gelen
    /// profillerin üzerine bindirilir.
    /// </param>
    public static ProfileStore Load(
        string? profilesDirectory = null,
        IReadOnlyList<LearnedCandidate>? learned = null)
    {
        var root = profilesDirectory ?? LocateProfilesDirectory();

        var ispDir = Path.Combine(root, "isp");
        if (!Directory.Exists(ispDir))
        {
            throw new DirectoryNotFoundException($"ISP profil dizini yok: {ispDir}");
        }

        var profiles = new List<IspProfile>();
        foreach (var file in Directory.EnumerateFiles(ispDir, "*.json").OrderBy(f => f, StringComparer.Ordinal))
        {
            try
            {
                var profile = JsonSerializer.Deserialize(File.ReadAllText(file), CoreJsonContext.Default.IspProfile);
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
            }
            catch (JsonException ex)
            {
                // Tek bozuk profil yüzünden tüm uygulamanın açılmaması kabul edilemez,
                // ama sessizce yutmak da kabul edilemez; dosya adıyla birlikte yükselt.
                throw new InvalidDataException($"Profil okunamadi: {Path.GetFileName(file)} -- {ex.Message}", ex);
            }
        }

        var ladderPath = Path.Combine(root, "generic-ladder.json");
        if (!File.Exists(ladderPath))
        {
            throw new FileNotFoundException($"Genel merdiven dosyasi yok: {ladderPath}");
        }

        var ladder = JsonSerializer.Deserialize(File.ReadAllText(ladderPath), CoreJsonContext.Default.GenericLadder)
                     ?? throw new InvalidDataException("generic-ladder.json bos cozumlendi.");

        if (learned is { Count: > 0 })
        {
            profiles = profiles.Select(p => p.WithLearned(learned)).ToList();
        }

        return new ProfileStore(
            profiles.OrderBy(p => p.Priority).ThenBy(p => p.Id, StringComparer.Ordinal).ToList(),
            ladder,
            root);
    }

    public IspProfile? FindById(string id)
        => Profiles.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// ASN ve kuruluş adına göre eşleşen profilleri en iyi eşleşme önce olacak
    /// şekilde döndürür.
    /// </summary>
    /// <remarks>
    /// ASN ile eşleşenler önce gelir: kuruluş adı eşleşmesi anahtar kelimeye dayalı
    /// ve daha zayıf. Örneğin "vodafone" hem sabit hat hem mobil profiliyle eşleşir;
    /// bu durumda kullanıcıya seçim sunmak doğru davranış, birini sessizce seçmek değil.
    /// </remarks>
    public IReadOnlyList<IspProfile> Match(int? asn, string? orgName)
    {
        var byAsn = asn is null
            ? []
            : Profiles.Where(p => p.Asns.Contains(asn.Value)).ToList();

        var byName = Profiles
            .Where(p => !byAsn.Contains(p) && p.Matches(null, orgName))
            .OrderBy(p => p.Priority)
            .ToList();

        return [.. byAsn.OrderBy(p => p.Priority), .. byName];
    }

    /// <summary>
    /// Tier 2 için komşu profiller: verilen profil dışındaki hepsi, priority sırasında.
    /// </summary>
    public IReadOnlyList<IspProfile> NeighboursOf(IspProfile profile)
        => Profiles.Where(p => p.Id != profile.Id).ToList();

    /// <summary>
    /// profiles/ dizinini arar: önce uygulamanın yanında (kurulu hâl), sonra yukarı
    /// doğru depo kökünde (geliştirme hâli).
    /// </summary>
    private static string LocateProfilesDirectory()
    {
        var start = AppContext.BaseDirectory;

        var beside = Path.Combine(start, "profiles");
        if (Directory.Exists(Path.Combine(beside, "isp")))
        {
            return beside;
        }

        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "profiles");
            if (Directory.Exists(Path.Combine(candidate, "isp")))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "profiles/ dizini bulunamadi. Uygulamanin yaninda veya depo kokunde olmali.");
    }
}
