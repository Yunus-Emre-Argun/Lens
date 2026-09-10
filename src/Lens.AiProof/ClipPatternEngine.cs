using Lens.Core.Ai;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lens.AiProof;

/// <summary>
/// [DENEY] CLIP gorunumlerini embedding'e ceviren, sonuclari diskte
/// onbellekleyen motor.
///
/// Onbellek gorunum BAZINDA tutulur (dosya basina degil): ayni "rgb.center"
/// gorunumu hem baseline hem de birlesik stratejilerde kullanildigi icin
/// yeniden hesaplanmaz. Bu olmadan deney turlari saatlerce surerdi.
///
/// URETIM KODU DEGILDIR - yalnizca olcum icindir.
/// </summary>
public sealed class ClipPatternEngine : IDisposable
{
    private readonly InferenceSession _session;
    private readonly string _cacheDirectory;
    private readonly ImagePreprocessingProfile _profile;
    private readonly int _dimension;
    private readonly string _outputName;
    private readonly Dictionary<string, Dictionary<string, float[]>> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _dirtyViews = new(StringComparer.Ordinal);

    public int EmbedCallCount { get; private set; }

    public int CacheHitCount { get; private set; }

    /// <param name="modelPath">Calistirilacak ONNX modeli.</param>
    /// <param name="cacheDirectory">Embedding onbellegi. MODEL BASINA AYRI olmalidir - ayni gorunum kimligi farkli modellerde farkli vektor demektir.</param>
    /// <param name="profile">Modelin resmi on isleme sozlesmesi.</param>
    /// <param name="dimension">Beklenen embedding boyutu (CLIP 512, DINOv2-B 768).</param>
    /// <param name="outputName">ONNX cikti tensorunun adi.</param>
    public ClipPatternEngine(
        string modelPath, string cacheDirectory,
        ImagePreprocessingProfile profile, int dimension, string outputName)
    {
        _cacheDirectory = cacheDirectory;
        _profile = profile;
        _dimension = dimension;
        _outputName = outputName;
        Directory.CreateDirectory(cacheDirectory);

        // Olculmus ayar (bkz. DinoV2Embedder): varsayilan spinning politikasi,
        // decode ile cikarimin siralandigi donguleri belirgin sekilde
        // yavaslatiyor. Deney de ayni desende calistigi icin ayni ayar
        // kullanilir - boylece sureler uretimle karsilastirilabilir kalir.
        using var options = new SessionOptions();
        options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        _session = new InferenceSession(modelPath, options);
    }

    /// <summary>
    /// Verilen gorsel icin istenen gorunumlerin embedding'lerini dondurur.
    /// Onbellekte olmayanlar hesaplanir; gorsel diskten yalnizca eksik
    /// gorunum varsa okunur.
    /// </summary>
    public Dictionary<string, float[]> GetEmbeddings(string imagePath, IReadOnlyList<ClipPatternViews.ViewSpec> views)
    {
        var key = Path.GetFileName(imagePath);
        var result = new Dictionary<string, float[]>(views.Count, StringComparer.Ordinal);
        var missing = new List<ClipPatternViews.ViewSpec>();

        foreach (var view in views)
        {
            var table = GetViewTable(view.ViewId);
            if (table.TryGetValue(key, out var cached))
            {
                result[view.ViewId] = cached;
                CacheHitCount++;
            }
            else if (!missing.Any(m => m.ViewId == view.ViewId))
            {
                missing.Add(view);
            }
        }

        if (missing.Count == 0)
        {
            return result;
        }

        var tensors = ClipPatternViews.BuildTensors(imagePath, missing, _profile);
        foreach (var view in missing)
        {
            var embedding = Embed(tensors[view.ViewId]);
            GetViewTable(view.ViewId)[key] = embedding;
            _dirtyViews.Add(view.ViewId);
            result[view.ViewId] = embedding;
            EmbedCallCount++;
        }

        return result;
    }

    private float[] Embed(float[] chw)
    {
        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, ClipPatternViews.Size, ClipPatternViews.Size });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", tensor),
        };

        using var results = _session.Run(inputs);
        var raw = results.First(r => r.Name == _outputName).AsEnumerable<float>().ToArray();
        return EmbeddingVector.L2NormalizeChecked(raw, _dimension);
    }

    // ---- Onbellek (gorunum basina tek ikili dosya) --------------------------------

    private Dictionary<string, float[]> GetViewTable(string viewId)
    {
        if (_cache.TryGetValue(viewId, out var table))
        {
            return table;
        }

        table = LoadViewTable(viewId);
        _cache[viewId] = table;
        return table;
    }

    private string ViewFilePath(string viewId) =>
        Path.Combine(_cacheDirectory, viewId.Replace('.', '_') + ".bin");

    private Dictionary<string, float[]> LoadViewTable(string viewId)
    {
        var table = new Dictionary<string, float[]>(StringComparer.Ordinal);
        var path = ViewFilePath(viewId);
        if (!File.Exists(path))
        {
            return table;
        }

        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            int count = reader.ReadInt32();
            int dimension = reader.ReadInt32();
            for (int i = 0; i < count; i++)
            {
                var name = reader.ReadString();
                var vector = new float[dimension];
                for (int d = 0; d < dimension; d++)
                {
                    vector[d] = reader.ReadSingle();
                }

                table[name] = vector;
            }
        }
        catch
        {
            // Yarim/bozuk onbellek dosyasi olcumu durdurmaz - bos kabul edilip
            // yeniden hesaplanir (onbellek yalnizca hiz icindir, veri degil).
            return new Dictionary<string, float[]>(StringComparer.Ordinal);
        }

        return table;
    }

    public void Flush()
    {
        foreach (var viewId in _dirtyViews)
        {
            var table = _cache[viewId];
            var path = ViewFilePath(viewId);
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream);
            writer.Write(table.Count);
            writer.Write(_dimension);
            foreach (var (name, vector) in table)
            {
                writer.Write(name);
                foreach (var value in vector)
                {
                    writer.Write(value);
                }
            }
        }

        _dirtyViews.Clear();
    }

    public void Dispose()
    {
        Flush();
        _session.Dispose();
    }
}
