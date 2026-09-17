using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Prober;

/// <summary>
/// Prober tarafının kaynak üretimli JSON bağlamı.
/// </summary>
/// <remarks>
/// Gerekçesi <see cref="ZapretTr.Core.Engine.CoreJsonContext"/> ile aynı: yansımaya
/// dayalı JSON kırpılmış yayınlarda SESSİZCE bozulur. Buradaki tipler kırılırsa
/// belirti "engelli" gibi görünür: hedef listesi çözülemezse test hiç hedef
/// bulamaz, DoH yanıtı çözülemezse şifreli DNS sessizce sistem DNS'ine düşer ve
/// Türkiye'de bu, DNS kaçırmasının geri gelmesi demek.
///
/// Bu tiplerin hepsi ilgili sınıfın içinde <c>internal</c> olarak duruyor
/// (<c>private</c> değil): kaynak üretici bağlamın onlara erişebilmesini istiyor.
/// </remarks>
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ProbeTargetStore.TargetDocument))]
[JsonSerializable(typeof(DohResolver.DohResponse))]
[JsonSerializable(typeof(IspDetector.IpApiResponse))]
[JsonSerializable(typeof(IspDetector.IpInfoResponse))]
internal sealed partial class ProberJsonContext : JsonSerializerContext
{
}
