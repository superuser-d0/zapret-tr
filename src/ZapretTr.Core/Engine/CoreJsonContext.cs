using System.Text.Json;
using System.Text.Json.Serialization;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Core.Engine;

/// <summary>
/// Kaynak üretimli JSON bağlamı.
/// </summary>
/// <remarks>
/// Yansımaya dayalı JSON, kırpılmış (trimmed) yayınlarda sessizce bozulur:
/// derleyici hangi tiplerin gerektiğini göremez, onları atar ve hata ancak
/// ÇALIŞMA ANINDA ortaya çıkar. Etkilenen yollardan biri DNS yedeğinin geri
/// yüklenmesi; orada sessiz bir hata, kullanıcının sistem DNS'i 127.0.0.1'de
/// kalmış ve hiçbir adı çözemiyor demek.
///
/// Bu bağlam o riski ortadan kaldırıyor: tipler derleme zamanında biliniyor,
/// kırpma onlara dokunmuyor ve IL2026 uyarıları kalmıyor.
/// </remarks>
// ReadCommentHandling ve AllowTrailingCommas, ProfileStore'un elle kurduğu
// seçenekleri karşılıyor: profil JSON'ları elle bakım görüyor ve içinde yorum
// bulunabiliyor. Bunlar buraya taşınmazsa kaynak üretimine geçiş, yorumlu bir
// profil dosyasını ayrıştırılamaz hâle getirirdi.
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(DnsBackup))]
[JsonSerializable(typeof(DnsBackupEntry))]
[JsonSerializable(typeof(List<LearnedCandidate>))]
[JsonSerializable(typeof(LearnedCandidate))]
[JsonSerializable(typeof(IspProfile))]
[JsonSerializable(typeof(GenericLadder))]
[JsonSerializable(typeof(HostlistDocument))]
public partial class CoreJsonContext : JsonSerializerContext
{
}
