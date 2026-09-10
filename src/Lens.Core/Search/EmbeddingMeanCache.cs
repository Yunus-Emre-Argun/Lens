using Lens.Core.Indexing;

namespace Lens.Core.Search;

/// <summary>
/// Katalog ortalamasinin bellek onbellegi (merkezleme icin).
///
/// GECERSIZLESTIRME TASARIMI: onbellek, hesaplandigi <b>kayit listesi
/// nesnesinin kimligine</b> (<see cref="object.ReferenceEquals"/>) ve kayit
/// sayisina baglidir. Uygulama, index'i her yeniden yukledigi veya
/// guncelledigi noktada <c>_indexEntries</c> alanina <b>YENI bir liste</b>
/// atar; dolayisiyla su durumlarin TAMAMINDA onbellek kendiliginden
/// gecersizlesir - ayri ayri elle temizleme cagrisi gerekmez ve unutulamaz:
///
///   • katalog klasoru degisince      (yeni liste yuklenir)
///   • aktif model degisince          (o profilin index'i yuklenir)
///   • renkli/gri profili degisince   (o profilin index'i yuklenir)
///   • index guncellenince            (BuildOrUpdate yeni liste dondurur)
///   • gorsel eklenince/silinince     (ayni sekilde yeni liste)
///   • index yeniden yuklenince       (yeni liste)
///
/// Elle <see cref="Invalidate"/> de cagrilabilir; bu, yukaridaki yapisal
/// garantiye EK bir savunma katmanidir.
/// </summary>
public sealed class EmbeddingMeanCache
{
    private IReadOnlyList<ImageIndexEntry>? _source;
    private int _sourceCount;
    private float[]? _mean;

    /// <summary>Onbellek su an dolu mu (tanilama/test icin).</summary>
    public bool HasValue => _mean is not null;

    /// <summary>Onbellegi acikca bosaltir.</summary>
    public void Invalidate()
    {
        _source = null;
        _mean = null;
        _sourceCount = 0;
    }

    /// <summary>
    /// Verilen kayit listesi icin ortalamayi dondurur; ayni liste nesnesi
    /// icin yeniden hesaplamaz. Hesaplanamazsa (bos/tutarsiz) null doner.
    /// </summary>
    public float[]? GetOrCompute(IReadOnlyList<ImageIndexEntry> entries)
    {
        // Hem referans hem sayi kontrol edilir: ayni liste NESNESI yerinde
        // degistirilmis olsa bile (savunmaci) yakalanir.
        if (_mean is not null && ReferenceEquals(_source, entries) && _sourceCount == entries.Count)
        {
            return _mean;
        }

        var mean = Compute(entries);
        _source = entries;
        _sourceCount = entries.Count;
        _mean = mean;
        return mean;
    }

    /// <summary>
    /// Kayitlarin ortalamasini hesaplar. Bos katalog, tutarsiz boyut veya
    /// sonlu olmayan deger durumunda null doner - cagiran taraf bunu
    /// "merkezleme uygulanamadi" olarak ele alir, SESSIZCE ham skora DONMEZ.
    /// </summary>
    public static float[]? Compute(IReadOnlyList<ImageIndexEntry> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var first = entries[0].Embedding;
        if (first is null || first.Length == 0)
        {
            return null;
        }

        int dimension = first.Length;
        var sum = new double[dimension];
        long count = 0;

        foreach (var entry in entries)
        {
            var embedding = entry.Embedding;
            if (embedding is null || embedding.Length != dimension)
            {
                // Tutarsiz boyut: ortalamayi bozar. Bu bir veri butunlugu
                // sorunudur ve sessizce kismi ortalama uretmek yerine
                // merkezleme tamamen uygulanmaz.
                return null;
            }

            for (int i = 0; i < dimension; i++)
            {
                var value = embedding[i];
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    return null;
                }

                sum[i] += value;
            }

            count++;
        }

        if (count == 0)
        {
            return null;
        }

        var mean = new float[dimension];
        for (int i = 0; i < dimension; i++)
        {
            var value = sum[i] / count;
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return null;
            }

            mean[i] = (float)value;
        }

        return mean;
    }
}
