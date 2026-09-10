using System.Diagnostics;
using Lens.Core.Ai;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;

namespace Lens.AiProof;

/// <summary>
/// [Tanilama] Gozlemlenen su celiskiyi kanita baglar: AYNI ONNX dosyasi tek
/// basina calistirildiginda ~140 ms, gercek indeksleme dongusunde (her adimda
/// FARKLI bir gorsel ImageSharp ile decode edilirken) ~1100 ms suruyor.
///
/// Neden onemli: 5.000 gorsellik katalogda bu fark ~12 dakika ile ~90 dakika
/// arasindaki farktir. Bir yapilandirmayi "duzeltme" olarak uygulamadan once
/// hangi ayarin gercekten etkili oldugunu OLCMEK gerekir.
///
/// KULLANIM: her yapilandirma AYRI BIR SURECTE olculmelidir - ayni surecte
/// birden fazla InferenceSession olusturmak sonuclari kirletir (thread
/// havuzlari birikir ve sonraki olcumleri yavaslatir):
///     Lens.AiProof ortbench default
///     Lens.AiProof ortbench spin0
///     Lens.AiProof ortbench intra4
///     Lens.AiProof ortbench intra8
///     Lens.AiProof ortbench intra8spin0
///     Lens.AiProof ortbench seqimagesharp
///
/// Uretim yolunun parcasi DEGILDIR.
/// </summary>
public static class OrtThreadProbe
{
    public static void Run(string repoRoot, string config)
    {
        var modelPath = Path.Combine(repoRoot, "models", DinoV2BaseProfile.ModelFileName);
        if (!File.Exists(modelPath))
        {
            Console.WriteLine($"[HATA] Model bulunamadi: {modelPath}");
            Environment.ExitCode = 1;
            return;
        }

        var images = CollectImages(repoRoot, 24);
        if (images.Count < 8)
        {
            Console.WriteLine("[HATA] Yeterli ornek gorsel bulunamadi.");
            Environment.ExitCode = 1;
            return;
        }

        using var options = new SessionOptions();
        switch (config)
        {
            case "default":
                break;
            case "spin0":
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                break;
            case "intra4":
                options.IntraOpNumThreads = 4;
                break;
            case "intra8":
                options.IntraOpNumThreads = 8;
                break;
            case "intra8spin0":
                options.IntraOpNumThreads = 8;
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                break;
            case "seqimagesharp":
                Configuration.Default.MaxDegreeOfParallelism = 1;
                break;
            case "intra4spin0":
                options.IntraOpNumThreads = 4;
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                break;
            case "intra4spin0seq":
                options.IntraOpNumThreads = 4;
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                Configuration.Default.MaxDegreeOfParallelism = 1;
                break;
            case "spin0seq":
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                Configuration.Default.MaxDegreeOfParallelism = 1;
                break;
            case "intra6spin0":
                options.IntraOpNumThreads = 6;
                options.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
                break;
            default:
                Console.WriteLine($"[HATA] Bilinmeyen yapilandirma: {config}");
                Environment.ExitCode = 1;
                return;
        }

        using var session = new InferenceSession(modelPath, options);

        // Isinma: ilk cagri JIT/allocator/arena kurulum maliyeti tasir.
        Infer(session, ImagePreprocessor.PreprocessToChwTensor(images[0], ImagePreprocessingProfile.DinoV2));

        // (1) SAF cikarim: ayni, onceden hazirlanmis tensor tekrar tekrar.
        //     ImageSharp hic devreye girmez.
        var warmTensor = ImagePreprocessor.PreprocessToChwTensor(images[0], ImagePreprocessingProfile.DinoV2);
        var pureTimes = new List<double>();
        for (int i = 0; i < 10; i++)
        {
            var w = Stopwatch.StartNew();
            Infer(session, warmTensor);
            w.Stop();
            pureTimes.Add(w.Elapsed.TotalMilliseconds);
        }

        // (2) GERCEK indeksleme deseni: her adimda FARKLI bir gorsel decode
        //     edilir, sonra cikarim yapilir.
        var loopTimes = new List<double>();
        var decodeTimes = new List<double>();
        foreach (var image in images)
        {
            var dw = Stopwatch.StartNew();
            var tensor = ImagePreprocessor.PreprocessToChwTensor(image, ImagePreprocessingProfile.DinoV2);
            dw.Stop();
            decodeTimes.Add(dw.Elapsed.TotalMilliseconds);

            var iw = Stopwatch.StartNew();
            Infer(session, tensor);
            iw.Stop();
            loopTimes.Add(iw.Elapsed.TotalMilliseconds);
        }

        pureTimes.Sort();
        loopTimes.Sort();
        decodeTimes.Sort();

        var loopMedian = loopTimes[loopTimes.Count / 2];
        var decodeMedian = decodeTimes[decodeTimes.Count / 2];

        Console.WriteLine($"yapilandirma={config,-14} cekirdek={Environment.ProcessorCount} "
            + $"imagesharp_par={Configuration.Default.MaxDegreeOfParallelism}");
        Console.WriteLine($"  (1) saf cikarim (ayni tensor)      medyan {pureTimes[pureTimes.Count / 2],7:F0} ms  min {pureTimes[0],7:F0} ms");
        Console.WriteLine($"  (2) indeksleme dongusu cikarimi    medyan {loopMedian,7:F0} ms  min {loopTimes[0],7:F0} ms");
        Console.WriteLine($"      on isleme (ayni dongude)       medyan {decodeMedian,7:F0} ms");
        Console.WriteLine($"      => gorsel basina TOPLAM        medyan {loopMedian + decodeMedian,7:F0} ms"
            + $"   -> 5.000 gorsel ~{(loopMedian + decodeMedian) * 5000 / 1000 / 60:F0} dk");
    }

    private static void Infer(InferenceSession session, float[] chw)
    {
        var tensor = new DenseTensor<float>(chw, new[] { 1, 3, 224, 224 });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(DinoV2BaseProfile.OnnxInputName, tensor),
        };

        using var results = session.Run(inputs);
        _ = results.First(r => r.Name == DinoV2BaseProfile.OnnxOutputName).AsEnumerable<float>().ToArray();
    }

    private static List<string> CollectImages(string repoRoot, int count)
    {
        var dir = Path.Combine(repoRoot, "benchmark", "data", "distractors");
        if (!Directory.Exists(dir))
        {
            return new List<string>();
        }

        return Directory.EnumerateFiles(dir)
            .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.Ordinal)
            .Take(count)
            .ToList();
    }
}
