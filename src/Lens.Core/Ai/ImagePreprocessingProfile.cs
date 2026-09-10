namespace Lens.Core.Ai;

/// <summary>
/// Bir modelin resmi goruntu on isleme sozlesmesi. CLIP ve DINOv2 sabitleri
/// BILEREK ayni statik sinifta karisik durmaz - her model kendi profilini
/// tasir, <see cref="ImagePreprocessor"/> profile gore calisir. Boylece
/// "yanlislikla CLIP mean/std ile DINOv2 besleme" hatasi yapisal olarak
/// zorlasir ve her profil tek basina test edilebilir.
///
/// Degerler modelin KENDI preprocessor_config.json dosyasindan alinir -
/// birinden digerine tasinmaz.
/// </summary>
/// <param name="Version">On isleme sozlesmesinin kimligi; index profiline (bkz. <see cref="EmbeddingProfile.PreprocessingVersion"/>) AYNEN yazilir.</param>
/// <param name="ResizeShortestEdge">Bicubic resize sonrasi kisa kenarin hedef uzunlugu.</param>
/// <param name="CropSize">Merkezden alinacak kare crop kenari (model girdi boyutu).</param>
/// <param name="Mean">Kanal bazli (R,G,B) ortalama - rescale (1/255) SONRASI cikarilir.</param>
/// <param name="Std">Kanal bazli (R,G,B) standart sapma - ortalama cikarildiktan SONRA bolunur.</param>
/// <param name="CropStrategy">Crop/tile stratejisinin kimligi; index profiline AYNEN yazilir.</param>
public sealed record ImagePreprocessingProfile(
    string Version,
    int ResizeShortestEdge,
    int CropSize,
    float[] Mean,
    float[] Std,
    string CropStrategy)
{
    /// <summary>
    /// CLIP (openai/clip-vit-base-patch16) profili - HF CLIPImageProcessor
    /// degerleri. Kisa kenar ve crop AYNI (224) oldugu icin bu profil, profil
    /// mimarisi eklenmeden ONCEKI davranisla BIREBIR ayni tensoru uretir.
    /// </summary>
    public static readonly ImagePreprocessingProfile Clip = new(
        Version: "clip-shortest224-crop224-openai-v1",
        ResizeShortestEdge: 224,
        CropSize: 224,
        Mean: new[] { 0.48145466f, 0.4578275f, 0.40821073f },
        Std: new[] { 0.26862954f, 0.26130258f, 0.27577711f },
        CropStrategy: "SingleCenterCrop224");

    /// <summary>
    /// DINOv2 (facebook/dinov2-base ve -small) profili - modelin kendi
    /// preprocessor_config.json'indan: shortest_edge 256, center crop 224,
    /// resample 3 (bicubic), rescale 1/255, ImageNet mean/std. CLIP'in
    /// "kisa kenar 224" akisi DINOv2 icin GECERLI DEGILDIR.
    /// </summary>
    public static readonly ImagePreprocessingProfile DinoV2 = new(
        Version: "dinov2-shortest256-crop224-imagenet-v1",
        ResizeShortestEdge: 256,
        CropSize: 224,
        Mean: new[] { 0.485f, 0.456f, 0.406f },
        Std: new[] { 0.229f, 0.224f, 0.225f },
        CropStrategy: "SingleCenterCrop224");
}
