namespace Lens.Core.Ai;

/// <summary>
/// Embedding vektorlerinin ortak, model-bagimsiz sayisal sozlesmesi:
/// dogrulama + L2 normalizasyon + karsilastirilabilirlik kontrolu. Tek yerde
/// tutulur ki her yeni model kendi (muhtemelen eksik) kopyasini yazmasin.
/// </summary>
public static class EmbeddingVector
{
    /// <summary>
    /// Ham model ciktisini dogrular ve L2-normalize eder. Sessiz kurtarma
    /// YOKTUR - beklenen boyutta degilse, NaN/Infinity iceriyorsa veya normu
    /// sifir/normalize edilemeyecek kadar kucukse
    /// <see cref="InvalidEmbeddingException"/> firlatir.
    /// </summary>
    public static float[] L2NormalizeChecked(float[]? raw, int expectedDimension)
    {
        if (raw is null)
        {
            throw new InvalidEmbeddingException("Model boş (null) bir embedding döndürdü.");
        }

        if (raw.Length != expectedDimension)
        {
            throw new InvalidEmbeddingException(
                $"Embedding boyutu beklenenden farklı: {raw.Length} (beklenen {expectedDimension}). "
                + "Model dosyası veya çıktı katmanı sözleşmeye uymuyor.");
        }

        double sumSquares = 0;
        for (int i = 0; i < raw.Length; i++)
        {
            var value = raw[i];
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new InvalidEmbeddingException(
                    $"Embedding geçersiz sayı içeriyor (index {i}: {value}).");
            }

            sumSquares += (double)value * value;
        }

        var norm = Math.Sqrt(sumSquares);

        // Sifir (veya float32'de anlamli sekilde normalize edilemeyecek kadar
        // kucuk) norm: vektorun YONU yok, cosine benzerligi tanimsizdir.
        // Bolme yapip sonsuz/NaN uretmek yerine acik hata veriyoruz.
        if (norm <= 1e-12)
        {
            throw new InvalidEmbeddingException(
                "Embedding normu sıfır - vektör yön bilgisi taşımıyor, benzerlik hesaplanamaz.");
        }

        var result = new float[raw.Length];
        for (int i = 0; i < raw.Length; i++)
        {
            result[i] = (float)(raw[i] / norm);
        }

        return result;
    }

    /// <summary>
    /// Iki vektorun cosine benzerligi icin KARSILASTIRILABILIR olup olmadigini
    /// dogrular. Farkli boyutlar (orn. 768 boyutlu DINOv2 sorgusu ile 512
    /// boyutlu CLIP kaydi) sessizce kismi/yanlis bir skor uretmemeli - bu
    /// durum bir veri butunlugu hatasidir, aciklayici bir exception ile
    /// bildirilir.
    /// </summary>
    public static void EnsureComparable(float[] query, float[] candidate, string candidateName)
    {
        if (query is null || candidate is null)
        {
            throw new InvalidEmbeddingException(
                $"Karşılaştırma için boş (null) embedding: '{candidateName}'.");
        }

        if (query.Length != candidate.Length)
        {
            throw new InvalidEmbeddingException(
                $"Embedding boyutları uyuşmuyor: sorgu {query.Length}, kayıt '{candidateName}' {candidate.Length}. "
                + "Farklı modellerin vektörleri karşılaştırılamaz - index'in aynı model profiliyle "
                + "yeniden oluşturulması gerekir.");
        }
    }
}
