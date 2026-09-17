using System.Text.Encodings.Web;
using System.Text.Json.Serialization;

// Ad alanı YOK, bilerek: Program.cs üst düzey deyimler kullanıyor ve global ad
// alanında çalışıyor. Yanındaki CliOptions da aynı şekilde ad alanısız duruyor.

/// <summary>
/// Diske yazılan rapor dosyasının şekli.
/// </summary>
/// <remarks>
/// Bu tipler eskiden ANONİM tiplerdi. Anonim tipler kaynak üretimiyle ele
/// ALINAMAZ, dolayısıyla rapor yazma yolu kırpılmış yayınlarda çalışan tek
/// yansıma yolu olarak kalırdı. Kırpma açılınca bu yol sessizce bozulur ve
/// belirti "test bitti ama rapor boş/eksik" olur.
///
/// Alan adları JSON'da küçük harfle başlıyor; bu, rapor şemasının ÖNCEDEN
/// yayınlanmış hâli ve saha kullanıcılarından gelen dosyalarla uyumlu kalması
/// gerekiyor. Bu yüzden adlar açıkça JsonPropertyName ile sabitlendi; adlandırma
/// politikasına bırakılsaydı bir seçenek değişikliği şemayı sessizce kırardı.
///
/// KASITLI OLARAK DAR: kişiyi tanımlayabilecek hiçbir alan yok. IP adresi
/// (ResolvedIp dahil), makine adı, kullanıcı adı dışarıda.
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
/// Rapor yazma yolunun kaynak üretimli bağlamı.
/// </summary>
/// <remarks>
/// UnsafeRelaxedJsonEscaping korunuyor: rapor Türkçe metin içeriyor ve varsayılan
/// kaçışlama "baglanti sifirlandi" gibi dizgileri \uXXXX yığınına çevirip dosyayı
/// insan tarafından okunamaz hâle getiriyor. Rapor kullanıcıya "açıp okuyabilirsin,
/// düz metindir" diye sunuluyor; okunamaz olması o sözü bozar.
/// </remarks>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReportDocument))]
internal sealed partial class ReportJsonContext : JsonSerializerContext
{
    /// <summary>Türkçe metni kaçışlamadan yazan seçenekler.</summary>
    public static readonly ReportJsonContext Relaxed = new(new System.Text.Json.JsonSerializerOptions
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    });
}
