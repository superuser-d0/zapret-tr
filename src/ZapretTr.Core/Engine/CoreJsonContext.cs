using System.Text.Json.Serialization;
using ZapretTr.Core.Profiles;

namespace ZapretTr.Core.Engine;

/// <summary>
/// Kaynak uretimli JSON baglami.
/// </summary>
/// <remarks>
/// Yansimaya dayali JSON, kirpilmis (trimmed) yayinlarda sessizce bozulur:
/// derleyici hangi tiplerin gerektigini goremez, onlari atar ve hata ancak
/// CALISMA ANINDA ortaya cikar. Etkilenen yollardan biri DNS yedeginin geri
/// yuklenmesi -- orada sessiz bir hata, kullanicinin sistem DNS'i 127.0.0.1'de
/// kalmis ve hicbir adi cozemiyor demek.
///
/// Bu baglam o riski ortadan kaldiriyor: tipler derleme zamaninda biliniyor,
/// kirpma onlara dokunmuyor ve IL2026 uyarilari kalmiyor.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(DnsBackup))]
[JsonSerializable(typeof(DnsBackupEntry))]
[JsonSerializable(typeof(List<LearnedCandidate>))]
[JsonSerializable(typeof(LearnedCandidate))]
[JsonSerializable(typeof(IspProfile))]
[JsonSerializable(typeof(GenericLadder))]
public partial class CoreJsonContext : JsonSerializerContext
{
}
