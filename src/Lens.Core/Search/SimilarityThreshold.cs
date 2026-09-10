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

    /// <summary>
    /// [Arama varsayilanlari - CLIP donemi] CLIP dagilimina gore belirlenmis
    /// varsayilan. Bu deger MODELE BAGLIDIR ve modeller arasi TASINAMAZ:
    /// tam veri olcumunde %80, DINOv2'de dogru eslesmelerin yalnizca ~%68'ini
    /// listede birakiyordu (CLIP'te ~%93) - bkz. docs/DECISIONS.md #95.
    ///
    /// Bu yuzden aktif model kendi baslangic esigini tasir (bkz.
    /// <see cref="Lens.Core.Ai.DinoV2BaseProfile.DefaultThresholdPercent"/>) ve
    /// UI o degeri <see cref="ResolveOrDefault(string?, double, out double)"/>
    /// asiri yuklemesine gecirir. Buradaki sabit, CLIP'e ait tarihsel deger
    /// olarak ve profilsiz eski cagrilarin (AiProof Grup G/K) sozlesmesini
    /// KORUMAK icin degistirilmeden birakildi.
    ///
    /// ONEMLI: bu esik kullanici ayarlarinda KALICI OLARAK SAKLANMAZ
    /// (UserSettings icinde boyle bir alan bilerek YOKTUR) - dolayisiyla model
    /// degisiminde tasinacak/goc ettirilecek kayitli bir kullanici degeri de
    /// yoktur; her acilista aktif modelin varsayilani yazilir.
    /// </summary>
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
    public static bool ResolveOrDefault(string? input, out double percent) =>
        ResolveOrDefault(input, DefaultPercent, out percent);

    /// <summary>
    /// [Model-spesifik varsayilan] Yukaridakinin, bos girdide kullanilacak
    /// varsayilani CAGIRANIN belirlediği hali - aktif modelin profilinden gelen
    /// deger gecirilir. Dogrulama sozlesmesi (0-100 arasi, metin/negatif/
    /// NaN/Infinity reddi, "0" gecerli) HIC DEGISMEZ; yalnizca BOS girdinin
    /// hangi sayiya cozuldugu degisir.
    /// </summary>
    public static bool ResolveOrDefault(string? input, double defaultPercent, out double percent)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            percent = defaultPercent;
            return true;
        }

        return TryParse(input, out percent);
    }
}
