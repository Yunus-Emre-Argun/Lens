using System.Globalization;

namespace Lens.Core.Search;

/// <summary>
/// "En fazla sonuç" kullanıcı girdisinin validasyonu (bkz. SimilarityThreshold -
/// aynı desen: pahalı işlemlerden ÖNCE çağrılması amaçlanır). Sabit teknik üst
/// sınır <see cref="SimilaritySearch.MaxResults"/> (200) ile senkron tutulur -
/// kullanıcının seçebileceği üst sınır budur. Boş, metin, ondalıklı, negatif,
/// 0 veya üst sınırı aşan değer geçersiz sayılır - keyfi bir değere sessizce
/// dönüştürülmez, yalnızca doğrulanır.
/// </summary>
public static class MaxResultsPreference
{
    public const int MinAllowed = 1;
    public const int MaxAllowed = SimilaritySearch.MaxResults;

    /// <summary>Eski (bu alanı içermeyen) ayar dosyası veya bozuk/aralık dışı kayıtlı değer için güvenli varsayılan.</summary>
    public const int Default = 15;

    public static bool TryParse(string? input, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        // NumberStyles.Integer: ondalık noktası/virgülü YOK, binlik ayırıcı YOK,
        // yalnızca isteğe bağlı öncü işaret + rakamlar - "15.5"/"15,5" zaten reddedilir.
        if (!int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            return false;
        }

        if (parsed < MinAllowed || parsed > MaxAllowed)
        {
            return false;
        }

        value = parsed;
        return true;
    }

    /// <summary>Kayıtlı bir tercihi (ör. eski/bozuk ayar dosyasından) güvenle doğrular - aralık dışıysa <see cref="Default"/> döner.</summary>
    public static int ValidateOrDefault(int storedValue) =>
        storedValue is >= MinAllowed and <= MaxAllowed ? storedValue : Default;

    /// <summary>
    /// [UI mesaj ayrımı] <see cref="TryParse"/> zaten false dönmüş olmalı - bu
    /// yalnızca hangi uyarı metninin gösterileceğini ayırt etmek içindir:
    /// GEÇERLİ bir tam sayı ama <see cref="MaxAllowed"/>'ı aşıyorsa true (ör.
    /// "300"); boş/metin/ondalık/negatif/0 gibi diğer TÜM geçersiz durumlarda
    /// false döner (bunlar tek bir genel mesajla gösterilir).
    /// </summary>
    public static bool IsAboveMaxAllowed(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        return int.TryParse(input.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            && parsed > MaxAllowed;
    }
}
