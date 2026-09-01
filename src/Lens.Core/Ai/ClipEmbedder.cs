using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lens.Core.Ai;

/// <summary>
/// CLIP vision encoder'ini (ONNX, CLIPVisionModelWithProjection export'u) ONNX
/// Runtime uzerinden CPU'da calistirir. Cikti, Faz 2 Python benchmarkindaki
/// CLIPModel.get_image_features() ile ayni projected image embedding'dir
/// (dogrulandi: benchmark/export_onnx.py + manuel cross-check, cos sim = 1.0).
/// </summary>
public sealed class ClipEmbedder : IDisposable
{
    public const int EmbeddingDimension = 512;

    private readonly InferenceSession _session;

    public ClipEmbedder(string onnxModelPath)
    {
        if (!File.Exists(onnxModelPath))
        {
            throw new FileNotFoundException($"ONNX model dosyasi bulunamadi: {onnxModelPath}");
        }

        _session = new InferenceSession(onnxModelPath);
    }

    public float[] Embed(string imagePath)
    {
        var chw = ImagePreprocessor.PreprocessToChwTensor(imagePath);
        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, ImagePreprocessor.TargetSize, ImagePreprocessor.TargetSize });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("pixel_values", tensor),
        };

        using var results = _session.Run(inputs);
        var output = results.First(r => r.Name == "image_embeds").AsEnumerable<float>().ToArray();

        return L2Normalize(output);
    }

    private static float[] L2Normalize(float[] vector)
    {
        double sumSquares = 0;
        foreach (var v in vector)
        {
            sumSquares += (double)v * v;
        }

        var norm = (float)Math.Sqrt(sumSquares);
        if (norm == 0)
        {
            return vector;
        }

        var result = new float[vector.Length];
        for (int i = 0; i < vector.Length; i++)
        {
            result[i] = vector[i] / norm;
        }

        return result;
    }

    public void Dispose() => _session.Dispose();
}
