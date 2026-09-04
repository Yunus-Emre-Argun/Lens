using System.Globalization;

namespace Lens.Core.Search;

/// <summary>
/// "Minimum benzerlik (%)" kullanici girdisinin validasyonu. Pahali
/// index/search islemlerinden ONCE cagrilmasi amaclanir (bkz. MainWindow
/// SearchButton_Click siralamasi). Turkce virgul ("80,5") ve nokta ("80.5")
/// girdisini guvenle kabul eder; bos, metin, negatif, NaN/Infinity veya
/// 100'den buyuk deger gecersiz sayilir.
///
/// [Arama varsayilanlari - SUPERSEDED] Eskiden "bu sinif bir default
/// URETMEZ" karari gecerliydi (bkz. eski "OPEN PRODUCT DECISION" notu).
/// Yonetici karari degisti: bos/yalnizca-bosluklu girdi icin <see cref="DefaultPercent"/>
/// (80) kullanilir - ama bu davranis <see cref="TryParse"/>'in KATI
/// sozlesmesini DEGISTIRMEZ (o hala bos girdiyi reddeder); ayri, test
/// edilebilir <see cref="ResolveOrDefault"/> adimina eklendi (bkz. MainWindow
/// SearchButton_Click - yalnizca ORADA cagrilir).
/// </summary>
public static class SimilarityThreshold
{
    public const double MinPercent = 0;
    public const double MaxPercent = 100;

    /// <summary>[Arama varsayilanlari] Acilista kutuya yazilan VE arama sirasinda bos/yalnizca-bosluklu girdi icin kullanilan tek ortak varsayilan.</summary>
    public const double DefaultPercent = 80;

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

    /// <summary>
    /// [Arama varsayilanlari] <see cref="TryParse"/>'in KATI sozlesmesine bir
    /// sey EKLEMEZ/DEGISTIRMEZ - yalnizca bos/yalnizca-bosluklu girdiyi
    /// <see cref="DefaultPercent"/>'e (80) cozer, digger HER SEYI (metin,
    /// negatif, 100 ustu, NaN/Infinity) oldugu gibi TryParse'e devreder (yani
    /// gecersiz kalir). "0" GECERLIDIR (varsayilana cevrilmez) - yalnizca
    /// gercekten BOS girdi varsayilan alir.
    /// </summary>
    public static bool ResolveOrDefault(string? input, out double percent)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            percent = DefaultPercent;
            return true;
        }

        return TryParse(input, out percent);
    }
}
