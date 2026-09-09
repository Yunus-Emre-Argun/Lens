namespace Lens.Core.Search;

/// <summary>
/// [Sayısal giriş filtresi] "Minimum benzerlik (%)" ve "En fazla sonuç" alanları
/// için tuş/yapıştırma seviyesinde harf ve geçersiz karakterleri engelleyen SAF
/// karar mantığı - WPF'ye bağımlı DEĞİL (bkz. Lens.AiProof hardeningtest Grup M).
/// Bu sınıf bir SON DOĞRULAMA DEĞİLDİR - <see cref="SimilarityThreshold"/>/
/// <see cref="MaxResultsPreference"/>'in katı sözleşmesinin (0-100 / 1-999 aralık
/// kontrolü, boş girdi varsayılanı vb.) YERİNE GEÇMEZ; yalnızca kullanıcının harf/
/// geçersiz karakter YAZMASINI/YAPIŞTIRMASINI önceden engeller. Aralık dışı ama
/// karakter olarak geçerli bir sayı (ör. benzerlik alanında "101") burada
/// REDDEDİLMEZ - o tamamen Resolve/TryParse katmanında kalır.
/// </summary>
public static class NumericInputFilter
{
    /// <summary>
    /// [2026-09-07 ek kural] Her iki alanda da (ondalık ayırıcı HARİÇ) en fazla bu
    /// kadar rakam kabul edilir - ör. "80,55" (4 rakam) reddedilir, "9,99" (3 rakam)
    /// kabul edilir. Aralık dışı ama rakam-sayısı-geçerli bir değer (ör. benzerlik
    /// alanında "999" - 3 rakam ama 100 üstü) burada reddedilmez - asıl "aralık dışı"
    /// uyarısı hâlâ Ara sırasında gösterilir. [999-limit] "En fazla sonuç" alanında
    /// MaxDigitCount (3) ile MaxResultsPreference.MaxAllowed (999) artık TAM
    /// hizalı - 1-999 arasındaki HER 3 haneli (veya daha az) pozitif tam sayı zaten
    /// aralık içinde; yalnızca 4+ haneli girdiler (ör. "1000") karakter seviyesinde
    /// yakalanır, tek haneli "0" ise karakter olarak geçerli ama hâlâ aralık dışıdır.
    /// </summary>
    public const int MaxDigitCount = 3;

    /// <summary>
    /// Mevcut metnin [selectionStart, selectionStart+selectionLength) aralığı
    /// <paramref name="insertedText"/> ile değiştirildiğinde ortaya çıkacak
    /// metnin geçerli bir "yazılıyor olabilir" adayı olup olmadığını döner
    /// (WPF PreviewTextInput/DataObject.Pasting'te kullanılır - işlem henüz
    /// UYGULANMADAN önce sorulur).
    /// </summary>
    public static bool IsValidPartialInput(string? currentText, int selectionStart, int selectionLength, string? insertedText, bool allowDecimal)
    {
        var text = currentText ?? string.Empty;
        var inserted = insertedText ?? string.Empty;

        var start = Math.Clamp(selectionStart, 0, text.Length);
        var length = Math.Clamp(selectionLength, 0, text.Length - start);
        var proposed = text.Remove(start, length).Insert(start, inserted);

        return IsValidPartialText(proposed, allowDecimal);
    }

    /// <summary>
    /// Tam bir metnin (yapıştırma sonucu birleşik metin, ya da TextChanged'teki
    /// son-doğrulama taraması için) geçerli bir kısmi/tam sayısal aday olup
    /// olmadığı. Boş metin HER ZAMAN geçerlidir (alan geçici olarak boş
    /// bırakılabilmeli - "Ara" sırasında sözleşmeye göre varsayılan uygulanır).
    /// allowDecimal=true iken TEK bir ',' veya '.' (TR/EN ondalık ayırıcı) kabul
    /// edilir; allowDecimal=false iken (En fazla sonuç) YALNIZCA rakam kabul
    /// edilir. Eksi işareti, harf, boşluk ve başka her karakter HER ZAMAN
    /// reddedilir - iki alan da negatif kabul etmiyor. Yalnızca ASCII 0-9 kabul
    /// edilir (Core katmanındaki double/int.TryParse + InvariantCulture ile
    /// BİREBİR aynı karakter kümesi - filtrenin kabul ettiği her şey Core'un da
    /// PARSE EDEBİLECEĞİ bir karakter kümesidir, aralık dışı kalabilir ama).
    /// Toplam rakam sayısı (ondalık ayırıcı SAYILMAZ, iki taraf birlikte
    /// sayılır) <see cref="MaxDigitCount"/>'u (3) AŞAMAZ - ör. "80,55" (4 rakam)
    /// reddedilir.
    /// </summary>
    public static bool IsValidPartialText(string? text, bool allowDecimal)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var separatorSeen = false;
        var digitCount = 0;
        foreach (var ch in text)
        {
            if (ch is ',' or '.')
            {
                if (!allowDecimal || separatorSeen)
                {
                    return false;
                }

                separatorSeen = true;
                continue;
            }

            if (ch is < '0' or > '9')
            {
                return false;
            }

            digitCount++;
            if (digitCount > MaxDigitCount)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// [Son güvenlik ağı] IME veya PreviewTextInput/DataObject.Pasting'i atlayan
    /// beklenmedik bir yoldan (ör. sürükle-bırak metin) geçersiz karakter alana
    /// girerse, geçersiz karakterleri (harf, eksi işareti, fazla ayırıcı) SESSİZCE
    /// kaldırıp geriye yalnızca geçerli rakam(lar) + (izin veriliyorsa) TEK bir
    /// ondalık ayırıcı bırakır; <see cref="MaxDigitCount"/>'u (3) AŞAN rakamlar da
    /// kırpılır. WPF TextChanged içinde çağrılması amaçlanır - bu, YAPIŞTIRMA
    /// yolundaki "sessizce kesme YERİNE tamamen reddet" kuralından FARKLIDIR
    /// (bkz. IsValidPartialInput/DataObject.Pasting): bu metot yalnızca PreviewTextInput/
    /// Pasting'i hiç GEÇMEMİŞ, olağandışı bir yoldan gelen metin için son çare.
    /// </summary>
    public static string StripInvalidCharacters(string? text, bool allowDecimal)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var separatorSeen = false;
        var digitCount = 0;
        foreach (var ch in text)
        {
            if (ch is ',' or '.')
            {
                if (allowDecimal && !separatorSeen)
                {
                    builder.Append(ch);
                    separatorSeen = true;
                }

                continue;
            }

            if (ch is >= '0' and <= '9' && digitCount < MaxDigitCount)
            {
                builder.Append(ch);
                digitCount++;
            }
        }

        return builder.ToString();
    }
}
