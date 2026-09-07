using System.Text.Json;
using System.Text.Json.Serialization;

namespace ZapretTr.Prober;

/// <summary>
/// Prober tarafinin kaynak uretimli JSON baglami.
/// </summary>
/// <remarks>
/// Gerekcesi <see cref="ZapretTr.Core.Engine.CoreJsonContext"/> ile ayni: yansimaya
/// dayali JSON kirpilmis yayinlarda SESSIZCE bozulur. Buradaki tipler kirilirsa
/// belirti "engelli" gibi gorunur -- hedef listesi cozulemezse test hic hedef
/// bulamaz, DoH yaniti cozulemezse sifreli DNS sessizce sistem DNS'ine duser ve
/// Turkiye'de bu, DNS kacirmasinin geri gelmesi demek.
///
/// Bu tiplerin hepsi ilgili sinifin icinde <c>internal</c> olarak duruyor
/// (<c>private</c> degil): kaynak uretici baglamin onlara erisebilmesini istiyor.
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
