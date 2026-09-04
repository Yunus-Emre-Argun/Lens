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

    /// <summary>
    /// [Arama varsayılanları] 15 → 20 (yönetici kararı). Üç yerde AYNI değer:
    /// (1) eski/bu alanı içermeyen veya bozuk/aralık dışı kayıtlı tercih için
    /// güvenli varsayılan (bkz. <see cref="ValidateOrDefault"/>), (2) açılışta
    /// kutuya yazılan başlangıç değeri, (3) arama sırasında boş/yalnızca-boşluklu
    /// girdi için kullanılan değer (bkz. <see cref="ResolveOrDefault"/>). Mevcut
    /// GEÇERLİ kayıtlı tercihler (ör. 15, 50, 200) bu değişiklikten ETKİLENMEZ -
    /// yalnızca "tercih hiç yok / geçersiz / boş girdi" durumlarında kullanılır.
    /// </summary>
    public const int Default = 20;

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

    /// <summary>
    /// [Arama varsayılanları] <see cref="TryParse"/>'in KATI sözleşmesine bir
    /// şey EKLEMEZ/DEĞİŞTİRMEZ - yalnızca boş/yalnızca-boşluklu girdiyi
    /// <see cref="Default"/>'a (20) çözer, diğer HER ŞEYİ (metin, ondalık,
    /// negatif, 0, 200 üstü) olduğu gibi TryParse'e devreder (yani geçersiz
    /// kalır) - "0" GEÇERSİZDİR (varsayılana çevrilmez), yalnızca gerçekten
    /// BOŞ girdi varsayılan alır.
    /// </summary>
    public static bool ResolveOrDefault(string? input, out int value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = Default;
            return true;
        }

        return TryParse(input, out value);
    }
}
