using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

// Ad alani YOK, bilerek: Program.cs ust duzey deyimler kullaniyor ve global ad
// alaninda calisiyor. Yanindaki CliOptions da ayni sekilde ad alanisiz duruyor.

/// <summary>
/// Diske yazilan rapor dosyasinin sekli.
/// </summary>
/// <remarks>
/// Bu tipler eskiden ANONIM tiplerdi. Anonim tipler kaynak uretimiyle ele
/// ALINAMAZ, dolayisiyla rapor yazma yolu kirpilmis yayinlarda calisan tek
/// yansima yolu olarak kalirdi. Kirpma acilinca bu yol sessizce bozulur ve
/// belirti "test bitti ama rapor bos/eksik" olur.
///
/// Alan adlari JSON'da kucuk harfle basliyor; bu, rapor semasinin ONCEDEN
/// yayinlanmis hali ve saha kullanicilarindan gelen dosyalarla uyumlu kalmasi
/// gerekiyor. Bu yuzden adlar acikca JsonPropertyName ile sabitlendi -- adlandirma
/// politikasina birakilsaydi bir secenek degisikligi semayi sessizce kirardi.
///
/// KASITLI OLARAK DAR: kisiyi tanimlayabilecek hicbir alan yok. IP adresi
/// (ResolvedIp dahil), makine adi, kullanici adi disarida.
/// </remarks>
internal sealed class ReportDocument
{
    [JsonPropertyName("schema")]
    public int Schema { get; init; } = 1;

    [JsonPropertyName("createdAt")]
    public required string CreatedAt { get; init; }

    [JsonPropertyName("durationSeconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("isp")]
    public string? Isp { get; init; }

    [JsonPropertyName("ispDisplayName")]
    public string? IspDisplayName { get; init; }

    [JsonPropertyName("baseline")]
    public required IReadOnlyList<ReportBaseline> Baseline { get; init; }

    [JsonPropertyName("attempts")]
    public required IReadOnlyList<ReportAttempt> Attempts { get; init; }

    [JsonPropertyName("winners")]
    public required IReadOnlyList<ReportWinner> Winners { get; init; }
}

internal sealed class ReportBaseline
{
    [JsonPropertyName("host")]
    public required string Host { get; init; }

    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }
}

internal sealed class ReportAttempt
{
    [JsonPropertyName("candidateId")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("args")]
    public required string Args { get; init; }

    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("targetHost")]
    public required string TargetHost { get; init; }

    [JsonPropertyName("targetCategory")]
    public required string TargetCategory { get; init; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; init; }

    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    [JsonPropertyName("durationMs")]
    public int DurationMs { get; init; }
}

internal sealed class ReportWinner
{
    [JsonPropertyName("section")]
    public required string Section { get; init; }

    [JsonPropertyName("candidateId")]
    public required string CandidateId { get; init; }

    [JsonPropertyName("args")]
    public required string Args { get; init; }

    [JsonPropertyName("verifiedFor")]
    public required IReadOnlyList<string> VerifiedFor { get; init; }
}

/// <summary>
/// Rapor yazma yolunun kaynak uretimli baglami.
/// </summary>
/// <remarks>
/// UnsafeRelaxedJsonEscaping korunuyor: rapor Turkce metin iceriyor ve varsayilan
/// kacislama "baglanti sifirlandi" gibi dizgileri \uXXXX yiginina cevirip dosyayi
/// insan tarafindan okunamaz hale getiriyor. Rapor kullaniciya "acip okuyabilirsin,
/// duz metindir" diye sunuluyor; okunamaz olmasi o sozu bozar.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReportDocument))]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
    /// <summary>Turkce metni kacislamadan yazan secenekler.</summary>
    public static readonly ReportJsonContext Relaxed = new(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
