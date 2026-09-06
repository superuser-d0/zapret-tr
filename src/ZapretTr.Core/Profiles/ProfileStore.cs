using System.Text.Json;
using ZapretTr.Core.Engine;

namespace ZapretTr.Core.Profiles;

/// <summary>
/// ISP profillerini ve genel merdiveni diskten yukler, ASN/kurulus adina gore eslestirir.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private ProfileStore(IReadOnlyList<IspProfile> profiles, GenericLadder ladder, string root)
    {
        Profiles = profiles;
        Ladder = ladder;
        Root = root;
    }

    /// <summary>Yuklendigi profiles/ dizini.</summary>
    public string Root { get; }

    /// <summary>Tum ISP profilleri, priority sirasinda (kucuk once).</summary>
    public IReadOnlyList<IspProfile> Profiles { get; }

    public GenericLadder Ladder { get; }

    /// <param name="learned">
    /// Kullanicinin kendi testlerinde dogruladigi adaylar. Dagitimla gelen
    /// profillerin uzerine bindirilir.
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
                var profile = JsonSerializer.Deserialize<IspProfile>(File.ReadAllText(file), JsonOptions);
                if (profile is not null)
                {
                    profiles.Add(profile);
                }
            }
            catch (JsonException ex)
            {
                // Tek bozuk profil yuzunden tum uygulamanin acilmamasi kabul edilemez,
                // ama sessizce yutmak da kabul edilemez -- dosya adiyla birlikte yukselt.
                throw new InvalidDataException($"Profil okunamadi: {Path.GetFileName(file)} -- {ex.Message}", ex);
            }
        }

        var ladderPath = Path.Combine(root, "generic-ladder.json");
        if (!File.Exists(ladderPath))
        {
            throw new FileNotFoundException($"Genel merdiven dosyasi yok: {ladderPath}");
        }

        var ladder = JsonSerializer.Deserialize<GenericLadder>(File.ReadAllText(ladderPath), JsonOptions)
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
    /// ASN ve kurulus adina gore eslesen profilleri en iyi eslesme once olacak
    /// sekilde dondurur.
    /// </summary>
    /// <remarks>
    /// ASN ile eslesenler once gelir: kurulus adi eslesmesi anahtar kelimeye dayali
    /// ve daha zayif. Ornegin "vodafone" hem sabit hat hem mobil profiliyle eslesir;
    /// bu durumda kullaniciya secim sunmak dogru davranis, birini sessizce secmek degil.
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
    /// Tier 2 icin komsu profiller: verilen profil disindaki hepsi, priority sirasinda.
    /// </summary>
    public IReadOnlyList<IspProfile> NeighboursOf(IspProfile profile)
        => Profiles.Where(p => p.Id != profile.Id).ToList();

    /// <summary>
    /// profiles/ dizinini arar: once uygulamanin yaninda (kurulu hal), sonra yukari
    /// dogru depo kokunde (gelistirme hali).
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
