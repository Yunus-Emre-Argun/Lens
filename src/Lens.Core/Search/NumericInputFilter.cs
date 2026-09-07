namespace Lens.Core.Search;

/// <summary>
/// [Sayısal giriş filtresi] "Minimum benzerlik (%)" ve "En fazla sonuç" alanları
/// için tuş/yapıştırma seviyesinde harf ve geçersiz karakterleri engelleyen SAF
/// karar mantığı - WPF'ye bağımlı DEĞİL (bkz. Lens.AiProof hardeningtest Grup M).
/// Bu sınıf bir SON DOĞRULAMA DEĞİLDİR - <see cref="SimilarityThreshold"/>/
/// <see cref="MaxResultsPreference"/>'in katı sözleşmesinin (0-100 / 1-200 aralık
/// kontrolü, boş girdi varsayılanı vb.) YERİNE GEÇMEZ; yalnızca kullanıcının harf/
/// geçersiz karakter YAZMASINI/YAPIŞTIRMASINI önceden engeller. Aralık dışı ama
/// karakter olarak geçerli bir sayı (ör. "101", "201") burada REDDEDİLMEZ - o
/// tamamen Resolve/TryParse katmanında kalır.
/// </summary>
public static class NumericInputFilter
{
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
    /// </summary>
    public static bool IsValidPartialText(string? text, bool allowDecimal)
    {
        if (string.IsNullOrEmpty(text))
        {
            return true;
        }

        var separatorSeen = false;
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
        }

        return true;
    }

    /// <summary>
    /// [Son güvenlik ağı] IME veya PreviewTextInput/DataObject.Pasting'i atlayan
    /// beklenmedik bir yoldan (ör. sürükle-bırak metin) geçersiz karakter alana
    /// girerse, geçersiz karakterleri (harf, eksi işareti, fazla ayırıcı) SESSİZCE
    /// kaldırıp geriye yalnızca geçerli rakam(lar) + (izin veriliyorsa) TEK bir
    /// ondalık ayırıcı bırakır. WPF TextChanged içinde çağrılması amaçlanır.
    /// </summary>
    public static string StripInvalidCharacters(string? text, bool allowDecimal)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var separatorSeen = false;
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

            if (ch is >= '0' and <= '9')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }
}
