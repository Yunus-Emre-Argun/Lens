namespace Lens.Core.DesenCodes;

/// <summary>
/// Desen kodunun ARAYUZDE nasil gorunecegi. Tek kaynak olmasinin nedeni,
/// ayni bicimin hem sonuc kartlarinda hem "Secilen Sonuc" alaninda
/// kullanilmasi ve ikisinin birbirinden ayri sapmamasidir.
///
/// KRITIK: buradaki <see cref="MissingCode"/> yer tutucusu YALNIZCA
/// gorunumdur. Metadata'ya asla yazilmaz - "00000", tire veya baska bir
/// sahte kod kaydedilmez (bkz. <see cref="DesenCodeRefresh.Apply"/>).
/// </summary>
public static class DesenCodeDisplay
{
    /// <summary>
    /// Kodu alinamamis urun icin gosterilen yer tutucu. Rakam DEGILDIR ve
    /// gercek bir kodla karistirilamaz: em dash (U+2014), sifir dizisi degil.
    /// </summary>
    public const string MissingCode = "—";

    /// <summary>Yer tutucunun uzerine gelindiginde gosterilen aciklama.</summary>
    public const string MissingCodeToolTip = "Desen kodu henüz alınamadı";

    /// <summary>
    /// Dosya adinin yaninda parantez icinde gosterilecek metin. Kod varsa
    /// "(00123)" - bastaki sifirlar string oldugu icin AYNEN korunur; kod
    /// yoksa "(—)". Bos string ASLA donmez: kutu her iki durumda da ayni
    /// yerde durur, kart yerlesimi kod gelince kaymaz.
    /// </summary>
    public static string Format(string? code) =>
        string.IsNullOrWhiteSpace(code) ? $"({MissingCode})" : $"({code})";

    /// <summary>Gercek kod var mi - yer tutucu bu sorunun cevabini DEGISTIRMEZ.</summary>
    public static bool HasCode(string? code) => !string.IsNullOrWhiteSpace(code);

    /// <summary>
    /// Kod ogesinin araci ipucu. Kod yoksa nedeni acikca soylenir; varsa
    /// tam dosya yolu ile birlikte gosterilir (dar kartta ad kirpilmis
    /// olabilir).
    /// </summary>
    public static string ToolTip(string? relativePath, string? code) =>
        HasCode(code)
            ? $"{relativePath} — desen kodu {code}"
            : $"{relativePath} — {MissingCodeToolTip}";

    /// <summary>
    /// Dar kartta gosterilecek kisa ad: goreli yolun YALNIZCA son parcasi
    /// ("alt/klasor/desen.jpg" -> "desen.jpg"). Tam yol araci ipucunda
    /// kalir; boylece uzun bir yol kod sutununu ekrandan itemez.
    /// Ayrac olarak hem '/' hem '\\' kabul edilir - index anahtari '/'
    /// ayracli uretilir, ama disaridan gelen bir deger de dogru kisalir.
    /// </summary>
    public static string ShortenFileName(string? relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            return string.Empty;
        }

        var lastSeparator = relativePath.LastIndexOfAny(new[] { '/', '\\' });
        return lastSeparator >= 0 && lastSeparator < relativePath.Length - 1
            ? relativePath[(lastSeparator + 1)..]
            : relativePath;
    }
}
