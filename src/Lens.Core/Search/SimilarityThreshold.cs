using System.Globalization;

namespace Lens.Core.Search;

/// <summary>
/// "Minimum benzerlik (%)" kullanici girdisinin validasyonu. Pahali
/// index/search islemlerinden ONCE cagrilmasi amaclanir (bkz. MainWindow
/// SearchButton_Click siralamasi). Turkce virgul ("80,5") ve nokta ("80.5")
/// girdisini guvenle kabul eder; bos, metin, negatif, NaN/Infinity veya
/// 100'den buyuk deger gecersiz sayilir - keyfi bir varsayilan UYGULANMAZ
/// (bkz. proje talimati "OPEN PRODUCT DECISION": bu sinif bir default
/// URETMEZ, yalnizca kullanicinin girdigi degeri dogrular).
/// </summary>
public static class SimilarityThreshold
{
    public const double MinPercent = 0;
    public const double MaxPercent = 100;

    public static bool TryParse(string? input, out double percent)
    {
        percent = 0;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalized = input.Trim().Replace(',', '.');

        // NumberStyles.Float ile birlikte double.TryParse "NaN"/"Infinity"
        // gibi ozel metinleri de basariyla parse edebilir - bu yuzden
        // IsNaN/IsInfinity kontrolu ayrica yapiliyor (girdi metni degil,
        // parse SONUCU kontrol ediliyor).
        if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return false;
        }

        if (value < MinPercent || value > MaxPercent)
        {
            return false;
        }

        percent = value;
        return true;
    }
}
