namespace Lens.Core.Ai;

/// <summary>
/// Bir gorseli tek bir global, L2-normalize embedding vektorune ceviren
/// model sarmalayicisi. Indeksleme/arama katmani artik somut bir model
/// tipine (eskiden yalnizca <see cref="ClipEmbedder"/>) DEGIL bu arayuze
/// baglidir; boylece pilot bir model, index ayrimi ve profil dogrulamasi
/// mevcut guvenilirlik mantigi KOPYALANMADAN devreye girebilir.
///
/// Uygulamalar bir ONNX oturumu tutar: olusturmasi pahalidir, tekrar
/// kullanilmalidir ve <see cref="IDisposable"/> ile birakilmalidir.
/// </summary>
public interface IImageEmbedder : IDisposable
{
    /// <summary>Bu embedder'in urettigi vektorlerin tam kimligi - index dosyasina yazilan/karsilastirilan profil (bkz. <see cref="EmbeddingProfile"/>).</summary>
    EmbeddingProfile Profile { get; }

    /// <summary>Verilen gorsel icin L2-normalize embedding dondurur. Basarisiz decode/gecersiz cikti durumunda exception firlatir - sessizce bozuk vektor DONDURMEZ.</summary>
    float[] Embed(string imagePath);
}
