using System.Security.Cryptography;

namespace Lens.Core.Ai;

/// <summary>
/// Model dosyasinin TAM SHA-256'sini hesaplar. Bu, "dosya adi/boyutu ayni ama
/// icerik farkli" durumunu yakalayan tek guvenilir yoldur ve index profilinin
/// (bkz. <see cref="EmbeddingProfile.ModelSha256"/>) zorunlu parcasidir.
///
/// Maliyet: ~330 MB'lik bir model icin diskten tek gecis (saniyenin altinda /
/// birkac yuz ms). Oturum basina BIR KEZ, model yuklemesiyle ayni arka plan
/// isinde hesaplanmalidir - UI thread'inde CAGRILMAMALIDIR.
/// </summary>
public static class ModelFileHash
{
    /// <summary>Dosyanin SHA-256'sini kucuk harfli hex metin olarak dondurur. Dosya akis halinde okunur - tamami bellege ALINMAZ.</summary>
    public static string ComputeSha256(string filePath)
    {
        // Buyuk dosya: tamami bellege alinmaz, akis halinde hash'lenir.
        using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1024 * 1024, useAsync: false);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
}
