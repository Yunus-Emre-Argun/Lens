# Model Card — Lens CLIP Vision Encoder

Bu doküman, Lens'in görsel embedding üretimi için kullandığı ONNX modelini
tanımlar. Bu, modelin kendisiyle ilgili bir "karar dokümanı" değildir — karar
geçmişi için `docs/DECISIONS.md`'ye bakın; bu yalnızca teknik referanstır.

## Model

- **Kaynak model:** [`openai/clip-vit-base-patch16`](https://huggingface.co/openai/clip-vit-base-patch16)
  (Hugging Face Hub)
- **Kullanılan bileşen:** `CLIPVisionModelWithProjection` (yalnızca vision
  encoder + projection head; text encoder kullanılmaz — Lens metin sorgusu
  yapmaz, yalnızca görsel-görsel benzerlik).
- **Neden bu bileşen:** `image_embeds` çıktısı, Faz 2 Python benchmarkındaki
  `CLIPModel.get_image_features()` çıktısıyla matematiksel olarak aynıdır
  (`vision_model → pooler_output → visual_projection`) — Python ve .NET
  tarafı aynı embedding'i üretir (doğrulandı: `benchmark/export_onnx.py`
  yorumu + manuel cross-check, cosine similarity = 1.0).

## Dosya Adı ve Beklenen Konum

```
models/clip-vision-b16-openai.onnx
```

Bu dosya **repoya commit edilmemiştir** (`.gitignore`, bkz. `docs/DECISIONS.md`
#28) — büyük bir binary olduğu ve model dağıtım/lisans yaklaşımının ayrıca
değerlendirilmesi gerektiği için. Geliştirme ortamında bu dosyanın nasıl elde
edileceği/yerleştirileceği: `docs/DEVELOPMENT_SETUP.md`.

`Lens.Desktop` ve `Lens.AiProof`, build/publish sırasında bu dosyayı
`AppContext.BaseDirectory\models\` altına kopyalar (bkz.
`src/Lens.Desktop/Lens.Desktop.csproj`, `CopyToOutputDirectory`).

## Export Yöntemi

Kaynak: `benchmark/export_onnx.py` (tek seferlik, engineering aracı — Lens
Desktop runtime'ının bir parçası değildir, Lens.exe Python çalıştırmaz).

```python
model = CLIPVisionModelWithProjection.from_pretrained("openai/clip-vit-base-patch16")
torch.onnx.export(
    model, (dummy_input,), OUTPUT_PATH,
    input_names=["pixel_values"], output_names=["image_embeds"],
    dynamic_axes={"pixel_values": {0: "batch"}, "image_embeds": {0: "batch"}},
    opset_version=17, dynamo=False,
)
```

Kullanılan Python bağımlılık sürümleri (export'un tekrarlanabilirliği için):
bkz. `requirements-benchmark.txt` (`torch==2.13.0+cpu`, `transformers==5.16.1`,
`onnx==1.17.0`).

## Input / Output

| | Ad | Şekil | Açıklama |
|---|---|---|---|
| Input | `pixel_values` | `[batch, 3, 224, 224]` | Önişlenmiş RGB tensor (batch ekseni dinamik) |
| Output | `image_embeds` | `[batch, 512]` | Projected image embedding (L2-normalize edilmemiş ham çıktı) |

.NET tarafında (`ClipEmbedder.cs`), çıktı **L2-normalize edilir** —
kaydedilen/karşılaştırılan embedding budur (`EmbeddingDimension = 512`).

## Preprocessing (224×224)

Kaynak: `src/Lens.Core/Ai/ImagePreprocessor.cs`. Hugging Face
`CLIPImageProcessor`'ın (`preprocessor_config.json`) davranışını yeniden
üretir:

1. Shortest-edge = 224 olacak şekilde **bicubic resize** (en-boy oranı korunur).
2. Merkezden **224×224 center crop**.
3. Piksel değerleri `[0,255] → [0,1]` **rescale** (1/255).
4. CLIP mean/std ile **normalize**:
   - mean: `[0.48145466, 0.4578275, 0.40821073]`
   - std: `[0.26862954, 0.26130258, 0.27577711]`
5. CHW (channel-first) tensor'a dönüştürülür.

**Önemli:** ONNX modeli değişirse (farklı bir HF checkpoint, farklı export
ayarları) bu preprocessing adımlarının da o modelin resmi
`preprocessor_config.json`'ı ile eşleştiği doğrulanmalıdır — otomatik bir
uyumluluk kontrolü yoktur.

## Embedding

- Boyut: **512** (`ClipEmbedder.EmbeddingDimension`).
- L2-normalize edilir (`ClipEmbedder.L2Normalize`) — benzerlik hesaplaması
  (`SimilaritySearch`) düz **dot product**'tır (L2-normalize sayesinde cosine
  similarity'ye eşdeğerdir).
- Cache'te (`%LocalAppData%\Lens\cache\...\index.json`) `float[512]` olarak
  saklanır; yükleme sırasında boyut/`NaN`/`Infinity` doğrulaması yapılır
  (bkz. `docs/DECISIONS.md` #56).

## ONNX Runtime Sürümü

- .NET: `Microsoft.ML.OnnxRuntime` **1.20.1** (`src/Lens.Core/Lens.Core.csproj`)
- Python export/doğrulama tarafı: `onnxruntime==1.20.1` (`requirements-benchmark.txt`)

İki taraf aynı sürümde — export sırasında doğrulanan davranışın .NET runtime'da
da geçerli olması beklenir.

## Model Lisansı

**Teyit edilmedi — release öncesi doğrulanmalı.** `openai/clip-vit-base-patch16`
model kartı Hugging Face Hub üzerinde (https://huggingface.co/openai/clip-vit-base-patch16)
kontrol edilip buraya kesin lisans adı/linki eklenmelidir. Bu alan **uydurulmamıştır**
— bilinmeyen bir lisans bilgisiyle production dağıtımı yapılmamalıdır
(bkz. `CLAUDE.md` kural 6).

## SHA-256 Doğrulama Yaklaşımı

**Release sırasında doldurulacak.** Model dosyası commit edilmediği için, farklı
geliştiriciler/ortamlar `export_onnx.py`'yi kendi çalıştırarak veya paylaşılan
bir kopyadan model dosyasını elde edecektir. Önerilen yaklaşım:

1. Resmi/onaylı bir model dosyası üretildiğinde (`export_onnx.py` çalıştırılıp
   çıktı doğrulandığında), o dosyanın `sha256sum models/clip-vision-b16-openai.onnx`
   çıktısı bu bölüme (ve `docs/RELEASE_PROCESS.md`'deki release kaydına) eklenir.
2. Yeni bir geliştirici/ortam, kendi model dosyasının hash'ini bu değerle
   karşılaştırarak doğru/bozulmamış bir kopyaya sahip olduğunu teyit edebilir.
3. Model veya export ayarları değiştiğinde bu hash de güncellenmelidir.

Şu anki repo durumunda resmi bir hash **kaydedilmemiştir** — bu, ileride bir
release hazırlanırken tamamlanmalıdır.

## Benchmark Sonuçları ve Sınırlamaları

| Test | Veri | Top-1 | Top-3 | Top-5 |
|---|---|---:|---:|---:|
| Faz 2 (`benchmark/results/report.md`) | 11 orijinal + 55 sentetik query | %98 | %100 | %100 |
| Faz 3C stress test (`benchmark/results/expanded_stress_test.md`) | 188 aday (11 gerçek + 177 distractor) | %98.2 | %100 | %100 |

**Sınırlamalar (uydurulmadan, olduğu gibi aktarılıyor):**
- Bu sayılar küçük, gösterge niteliğinde ölçümlerdir — istatistiksel genel
  geçerlilik iddiası taşımaz.
- Sentetik varyasyonlar (crop, resize, brightness, contrast, jpeg quality)
  yalnızca geometrik/fotometrik değişimleri simüle eder; gerçek ikinci
  fotoğraflarda olan farklı kamera/ışık/kumaş kırışıklığı/perspektif gibi
  etkenler test edilmemiştir (bkz. `docs/ARCHITECTURE_PROPOSAL.md` §7).
- Gerçek ~5000 görsellik ölçekte doğruluk henüz ölçülmedi (FAZ 4F, bekliyor).

## Production Model Kararının Durumu

CLIP, MVP için **provisional/reversible** olarak seçilmiştir (Tech Lead/CTO
onayı, `docs/DECISIONS.md` #20). Production için final model kararı **hâlâ
açıktır** (`docs/DECISIONS.md` "Not Yet Decided" #1) — SigLIP veya başka bir
model, yeni ölçüm sonuçlarına göre yeniden değerlendirilebilir.

## Model/Preprocessing Değiştiğinde Cache

`ImageIndex` cache'i şu an **model veya preprocessing sürümünü etiketlemez**
— yalnızca embedding boyutunun (512) ve değerlerin (NaN/Infinity olmaması)
geçerliliğini kontrol eder (bkz. `docs/DECISIONS.md` #56). Bu, bilinçli olarak
ertelenmiş bir konudur (bkz. `docs/ROADMAP.md` FAZ 4E "bilerek ertelenenler").

**Sonuç:** Model dosyası değiştirilirse (farklı checkpoint, farklı export
ayarı) veya `ImagePreprocessor` mantığı değişirse, aynı boyutta ama artık
**anlamsal olarak farklı** embedding'ler üretilebilir — mevcut cache bunu
otomatik algılamaz. Böyle bir değişiklikten sonra:

```
%LocalAppData%\Lens\cache\
```

klasörü **elle silinmeli**, sonraki "İndeksi Güncelle" tüm görselleri yeni
modelle yeniden embed edecektir.
