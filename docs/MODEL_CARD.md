# Model Card — Lens Görsel Encoder

> `main` dalının production modeli **CLIP ViT-B/16**'dır (aşağıdaki ilk bölümler).
> İki ayrı pilot dalı vardır:
> - `feature/dinov2-base-pilot` → **DINOv2 ViT-B/14** (bkz. "PİLOT ENTEGRASYON")
> - `feature/clip-pattern-pilot` → **desen odaklı CLIP** — aynı ağırlıklar,
>   değişen kadraj/renk/skor katmanı (bkz. "PİLOT 2" ve
>   `docs/CLIP_PATTERN_EXPERIMENT.md`)

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
- **[Faz 1]** Shared index'te (`<ProductDirectory>/.lens/index.json` —
  artık `%LocalAppData%` değil, bkz. `docs/DECISIONS.md` #61) `float[512]`
  olarak saklanır; yükleme sırasında boyut/`NaN`/`Infinity` doğrulaması
  yapılır (bkz. `docs/DECISIONS.md` #56).

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

## PİLOT 2 — Desen Odaklı CLIP (`feature/clip-pattern-pilot`)

> **CLIP ağırlıkları DEĞİŞTİRİLMEMİŞTİR** — aşağıdaki dosyanın aynısı
> kullanılır. Değişen yalnızca modelin ETRAFINDAKİ katmanlardır. Bu bölüm
> `main`'i anlatmaz.

| | Değer |
|---|---|
| Model | `openai/clip-vit-base-patch16` (değişmedi) |
| Revision | `57c216476eefef5ab752ec549e440a49ae4ae5f3` |
| ONNX dosyası | `models/clip-vision-b16-openai.onnx` (329 MB) |
| **ONNX SHA-256** | `b75f9ea71a29fe3ad98406d63986ad99a2714ae18fcbddcc48a664d151f40126` |
| Görünüm/görsel | **6** — tam görüntü + kenarın %60'ı boyutunda 5 örtüşen bölge |
| Embedding boyutu | **3072** (6 × 512, birleşik tek vektör) |
| Normalizasyon | Görünüm başına L2 + arama anında katalog-ortalaması whitening |
| Skor | `0,5 × global + 0,5 × en iyi görünüm çifti` |
| Ön işleme sürümü | `clip-pattern-center-overlap5-v1` |
| Index şeması | 3 — `.lens/indexes/clip-pattern-v1/` |
| Başlangıç eşiği | **%55** (geçici; mevcut üretimin %96 tutulma oranını korur) |

**Ölçülen kazanç** (gerçek katalog, 2.007 görsel, 336 sentetik sorgu):
val R@1 85,1% → **96,4%**, MRR 0,880 → 0,976, doğru–rakip ayrımı 5×.

**Maliyet:** görsel başına 6 embedding → ~3,4× indeksleme süresi, 4× indeks
boyutu, ~3× sorgu gecikmesi (DINOv2-Base'e göre).

**DINOv2'nin yerine önerilmemektedir.** Aynı koşullarda DINOv2-Base val R@1
93,5% (tek görünümle) ve tek gerçek örnekte daha iyi ayırıyor. Ayrıntı ve
doğrulanamayanlar: `docs/CLIP_PATTERN_EXPERIMENT.md`.

## SHA-256 Doğrulama Yaklaşımı

> **[2026-09-10 güncellemesi]** CLIP ONNX dosyasının resmî SHA-256'sı artık
> kayıtlıdır: `b75f9ea71a29fe3ad98406d63986ad99a2714ae18fcbddcc48a664d151f40126`.
> Desen pilotu bu değeri index profiline yazar ve her açılışta doğrular —
> dosya adı/boyutu aynı kalsa bile içerik değişmişse index geçersiz sayılır.

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
açıktır** (`docs/DECISIONS.md` "Not Yet Decided" #1).

**Yukarıda tanımlanan CLIP modeli, uygulamada şu anda GERÇEKTEN kullanılan
production modelidir.** Aşağıdaki pilot adayı henüz uygulamaya entegre
edilmemiştir.

---

## PİLOT ENTEGRASYON — DINOv2 ViT-B/14 (`feature/dinov2-base-pilot`)

> **Bu bölüm `main` dalını ANLATMAZ.** `main`'deki uygulama hâlâ CLIP
> kullanmaktadır. Aşağıdaki model, `feature/dinov2-base-pilot` dalında
> çalıştırılabilir bir **pilot** olarak entegre edilmiştir; **production
> model kararı alınmamıştır** (bkz. `docs/DECISIONS.md` #96 ve Not Yet
> Decided #12, #13, #15).

| | Değer |
|---|---|
| Resmî kaynak | [`facebook/dinov2-base`](https://huggingface.co/facebook/dinov2-base) (Hugging Face, Meta AI) |
| Revision (pinlenmiş) | `f9e44c814b77203eaa57a6bdbbd535f21ede1415` |
| Kod lisansı | Apache-2.0 |
| Ağırlık lisansı | Apache-2.0 (modelin kendi kartı meta verisinden okundu; **şirket/hukuk onayı ayrı bir adımdır** — Not Yet Decided #13) |
| Ağırlık dosyası | `model.safetensors`, 330,3 MB (yerel HF önbelleği) |
| **ONNX dosyası** | `models/dinov2-base.onnx` |
| **ONNX boyutu** | **330,5 MB** (346.532.005 bayt) |
| **ONNX SHA-256** | `51014b029a9feaec58825836b0fa42b3b4aa86ae92dd35dd4db5d928dbff263d` |
| Giriş | `pixel_values`, float32 `[batch, 3, 224, 224]` |
| Çıkış | `image_embeds`, float32 `[batch, 768]` |
| ONNX opset | 17, dinamik batch |
| Özellik türü | **CLS token** (`last_hidden_state[:, 0]`) — DINOv2'de projection head YOKTUR |
| Embedding boyutu | **768** |
| Normalizasyon | L2 — **grafiğe dahil değildir**, çalışma zamanında .NET tarafında yapılır |

### Ön işleme sözleşmesi (CLIP'ten FARKLI)

Değerler modelin kendi `preprocessor_config.json` dosyasındandır; CLIP'in
sabitleri buraya **taşınmamıştır**.

| Adım | Değer |
|---|---|
| Renk | RGB'ye dönüştür |
| Resize | bicubic, **kısa kenar 256** (CLIP'te 224) |
| Crop | merkezden 224 × 224 |
| Rescale | 1/255 |
| mean | `[0.485, 0.456, 0.406]` (ImageNet) |
| std | `[0.229, 0.224, 0.225]` (ImageNet) |
| Ön işleme sürümü | `dinov2-shortest256-crop224-imagenet-v1` |
| Crop stratejisi | `SingleCenterCrop224` — tek global embedding, çoklu crop/tile YOK |

Kaynak: `Lens.Core.Ai.ImagePreprocessingProfile.DinoV2`. CLIP ve DINOv2
sabitleri artık aynı statik sınıfta karışık **durmaz**.

### Nasıl yeniden üretilir

```
python benchmark/export_dinov2_onnx.py
```

Betik resmî ağırlıkları **pinlenmiş revision** ile yerel Hugging Face
önbelleğinden yükler, CLS token döndüren minimal bir sarmalayıcıyı ONNX'e
aktarır, dosyanın SHA-256'sını hesaplar ve PyTorch ile ONNX çıktısını gerçek
görseller üzerinde karşılaştırır. Bu **yalnızca bir geliştirme aracıdır** —
son kullanıcı makinesinde Python gerekmez, uygulama çalışma zamanında
internetten model indirmez.

**Doğrulama sonucu (8 gerçek görsel):** en kötü cosine **0,99999994**, en
büyük mutlak fark **2,3e-05** → PyTorch ve ONNX sayısal olarak eşdeğerdir.
Betik bu eşiklerin dışında bir sonuçta hata verir ve modeli onaylamaz.

### Model dosyasının konumu ve doğrulanması

Uygulama modeli `AppContext.BaseDirectory\models\dinov2-base.onnx`
altında arar; bulunamazsa geliştirme ortamı için repo kökündeki `models/`
klasörüne bakar. Dosya **git'e commit edilmez** (bkz. `docs/DECISIONS.md`
#28).

Model dosyasının **tam SHA-256'sı her oturumda bir kez hesaplanır** ve
embedding profilinin parçası olarak index'e yazılır. Böylece dosya adı ve
boyutu aynı kalsa bile içerik değişmişse index otomatik olarak geçersiz
sayılır — bu, `docs/MODEL_CARD.md`'de daha önce açık bırakılan "SHA-256
doğrulama yaklaşımı" maddesinin bu dal için **kapatılmış** halidir.

### Index profili (bu modele geçiş TAM YENİDEN İNDEKSLEME gerektirir)

Bu modelin index'i **ayrı bir dosyadadır**:

```
<ProductDirectory>\.lens\indexes\dinov2-base-v1\index.json   (kilit: index.lock, aynı klasörde)
```

Eski CLIP index'i (`<ProductDirectory>\.lens\index.json`) **okunmaz,
yazılmaz, silinmez** — CLIP sürümüne dönülürse yeniden indeksleme gerekmez.

Belge düz bir kayıt listesi değildir; `SchemaVersion` + `EmbeddingProfile` +
`Entries` zarfına sahiptir. Yüklemede kayıtlı profil ile çalışan profil tam
karşılaştırılır. Şu alanlardan **herhangi biri** farklıysa embedding'ler
kullanılmaz ve index **tamamen** yeniden oluşturulur (kısmi/karışık index
kabul edilmez):

model kimliği · model revision · **model dosyası SHA-256** · ön işleme sürümü ·
embedding boyutu · özellik türü · crop stratejisi · normalizasyon ·
index şema sürümü

Nedeni kullanıcıya durum satırında ve log dosyasında (`IndexProfileReset`)
gösterilir — sessiz bir tam yeniden tarama yapılmaz.

### Eşik (GEÇİCİ pilot değeri: %55)

CLIP dönemi varsayılanı **%80 bu modele taşınamaz**. Bu dalda başlangıç
değeri **%55**'tir (`Lens.Core.Ai.DinoV2BaseProfile.DefaultThresholdPercent`).

Küçük ölçekli smoke ölçümünde (193 görsellik havuz, 75 dönüşüm sorgusu) doğru
kaynakların listede kalma oranı:

| Eşik | %40 | %50 | **%55** | %60 | %70 | %80 |
|---|---|---|---|---|---|---|
| Kalan | %100 | %100 | **%100** | %98,7 | %89,3 | %74,7 |

Bu bir **kalibrasyon değildir** — kabul edilebilir yanlış pozitif oranı
gerçek katalogda ölçülmeden kesin değer belirlenemez (Not Yet Decided #16).

**Not:** Bu eşik kullanıcı ayarlarında (`UserSettings`) **kalıcı olarak
saklanmaz**; dolayısıyla model değişiminde taşınacak/göç ettirilecek kayıtlı
bir kullanıcı değeri yoktur. Kullanıcının o oturumda elle girdiği geçerli
değere dokunulmaz.

### Ölçülen davranış (smoke testi — büyük benchmarkın YERİNE GEÇMEZ)

`Lens.AiProof dinosmoke` ile, gerçek üretim yolunda (.NET + ONNX +
profil-ayrımlı index + `SimilaritySearch`), 193 görsellik bir havuzda,
5 kaynak görselden üretilen 15 dönüşümle:

- **15 dönüşümün tamamında R@1 %100** — 90°/180°/270° dönüş, renk tonu (hue),
  tam gri, doygunluk azaltma, parlaklık, kontrast, sol/sağ/merkez kısmi crop,
  2× yakınlaştırma, küçültme ve köşeye kaydırma (konum değişikliği).
- Medyan skorlar: dönüşlerde %70–91, kısmi crop'ta %76–83, hue'da %96,5,
  gri'de %94,9.
- **İş kuralı** (`docs/DECISIONS.md` #93): renk tonu değiştirilmiş sorguda
  doğru kaynak 5/5 durumda, renk olarak en yakın diğer katalog görsellerinin
  **üstünde** sıralandı (rakip sıraları 3–27).

**Sınırlama:** bu 193 görsellik bir havuzdur ve `docs/MODEL_BENCHMARK.md`'deki
2.007 görsellik / 720 sorguluk ölçümün yerine **geçmez**. DINOv2-S ile
DINOv2-B'yi aynı koşulda karşılaştırmaz.

### Hız (bu geliştirme makinesi, CPU, 20 çekirdek)

| | Değer |
|---|---|
| Model dosyası SHA-256 hesabı | 219 ms (oturum başına bir kez) |
| ONNX oturumu oluşturma | 464 ms |
| Ön işleme (ImageSharp) | 15 ms/görsel |
| Görsel başına toplam (indeksleme) | **427 ms** |
| 193 görsel indeksleme | 82,4 sn |
| 5.000 görsel tahmini | **~36 dakika** (doğrusal ölçekleme varsayımı) |
| Arama (193 kayıt, brute-force) | 0,7 ms |

**Ölçülmüş performans ayarı:** ONNX Runtime'ın varsayılan *spinning* thread
politikası, Lens'in sıralı "decode → çıkarım" indeksleme döngüsünde ImageSharp
ile birbirini aç bırakıyordu (görsel başına 1155 ms). `DinoV2Embedder` artık
`session.intra_op.allow_spinning=0` ile oturum açar → 439 ms. Bu ayar yalnızca
thread bekleme politikasıdır ve **sayısal çıktıyı değiştirmez** (smoke testi
skorları birebir aynı kaldı). `IntraOpNumThreads` bilerek ayarlanmamıştır:
sabit bir değer bu makinede ek kazanç sağlamadı ve çekirdek sayısı bilinmeyen
ofis bilgisayarında kötü bir tahmin olma riski taşır.

**Açık sınırlama:** saf çıkarım (aynı tensör, ImageSharp araya girmeden)
~115 ms iken indeksleme döngüsünde ~422 ms ölçülmektedir; fark tamamen
giderilememiştir. Ayrıca tüm bu ölçümler **bu geliştirme makinesine** aittir —
hedef ofis bilgisayarında yeniden ölçülmelidir (`Lens.AiProof ortbench`
tanılama modu bunun için vardır).

---

## Pilot Adayı (henüz entegre EDİLMEDİ) — DINOv2 ViT-S/14

2026-09-09 tarihli geniş veri benchmarkı (2.007 görsel, 720 dönüşüm sorgusu;
bkz. `docs/MODEL_BENCHMARK.md`) sonucunda **pilot adayı** olarak seçilmiştir.
Bu bir **production kararı değildir**; yönetici onayı ve entegrasyon işleri
beklemektedir (`docs/DECISIONS.md` #93).

| | Değer |
|---|---|
| Resmî kaynak | [`facebook/dinov2-small`](https://huggingface.co/facebook/dinov2-small) (Hugging Face, Meta AI) |
| Revision | `ed25f3a31f01632728cabb09d1542f84ab7b0056` |
| Ağırlık SHA-256 | `ae1e99fcefd534ed978cdeb8326f08030c96e28b7a81ffcbc98a857c84d14be1` |
| Ağırlık boyutu | 88,2 MB (`model.safetensors`) |
| Kod lisansı | Apache-2.0 |
| Ağırlık lisansı | Apache-2.0 (model kartı meta verisinden okundu; **şirket/hukuk onayı ayrı bir adımdır**) |
| Giriş | 224 × 224 (kısa kenar **256** → 224 center crop) |
| Normalizasyon | ImageNet mean `[0.485, 0.456, 0.406]`, std `[0.229, 0.224, 0.225]` |
| Embedding türü | **CLS token**, L2-normalize |
| Embedding boyutu | **384** |
| ONNX (benchmark için üretildi, repoda yok) | 88,4 MB, SHA-256 `fac422cc5359e13740a393d2ba8a7f52…` |

**Ön işleme uyarısı:** DINOv2'nin normalizasyon sabitleri ve resize adımı
CLIP'inkinden **farklıdır**; CLIP değerleri bu modele taşınamaz.
`ImagePreprocessor` şu an CLIP değerlerine sabittir.

### Bilinen sınırlamalar (ölçülmüş)

- **Kısmi crop ve güçlü ölçek değişiminde zayıf:** `crop_right` R@1 %65
  (p95 sıra 140), `scale2x` R@1 %57 (p95 sıra 173). DINOv2 ViT-B/14 bu iki
  senaryoda belirgin daha iyidir (p95 24–38) ve gerçek kullanımda sorgular
  ağırlıkla desenin küçük bir parçasıysa yeniden değerlendirilmelidir.
- **Hafif eğiklik (±15°)** R@1 %88–90 — ±30°'de %82.
- **Ayna (mirror) görüntüyü %100 aynı desen sayar.** Bunun istenen davranış
  olup olmadığı iş kuralı olarak **kararlaştırılmamıştır**.
- Ölçümler sentetik dönüşümlerle yapılmıştır; gerçek ikinci fotoğraf verisi
  (kırışık kumaş, karma ışık, perspektif) test edilmemiştir.

### Bu modele geçiş TAM YENİDEN İNDEKSLEME gerektirir

Embedding boyutu (512 → 384), ön işleme ve özellik türü değiştiği için mevcut
CLIP index'i **geçersizdir**. CLIP ve DINOv2 embedding'leri aynı index
dosyasında **karışmamalıdır**. Index'e model kimliği, model SHA-256, ön işleme
sürümü, embedding boyutu, özellik türü ve şema sürümü alanları eklenmeden
geçiş yapılmamalıdır (bkz. `docs/DECISIONS.md` #94).

### Eşik uyarısı

Mevcut varsayılan **%80** eşiği bu modele **taşınamaz**: tam veri ölçümünde
%80 eşiği DINOv2-S'te doğru eşleşmelerin yalnızca **%68,2'sini** listede
bırakır (CLIP'te %92,9). Önerilen başlangıç aralığı **%55–60**; kesin değer
gerçek katalogda kalibre edilmelidir.

## Benzerlik Skorunun Anlamı

Kullanıcıya gösterilen "Benzerlik: %XX" değeri **bir olasılık değildir**.
L2-normalize edilmiş iki embedding'in nokta çarpımının (kosinüs benzerliği)
yüzdeye çevrilmiş halidir; "bu görsellerin aynı ürün olma ihtimali %XX"
anlamına **gelmez**. Ayrıca bu değerin ölçeği **modele bağlıdır** — model
değişirse aynı yüzde farklı bir yakınlığı ifade eder.

## İş Kuralı: Desen Kimliği Renkten Önceliklidir

Lens'in hedefi, sorgulanan desen/motifin ürün görselinin herhangi bir
konumunda uygulanıp uygulanmadığını bulmaktır. Renk, sıralamayı belirleyen
ölçüt **değildir**: aynı desen farklı renkte veya farklı yönde (90°/180°/270°)
bulunduğunda eşleşme sayılmalı; aynı renkte fakat farklı motif taşıyan ürünler
üst sıralara çıkmamalıdır (bkz. `docs/DECISIONS.md` #93).

## Model/Preprocessing Değiştiğinde Cache

`ImageIndex` cache'i şu an **model veya preprocessing sürümünü etiketlemez**
— yalnızca embedding boyutunun (512) ve değerlerin (NaN/Infinity olmaması)
geçerliliğini kontrol eder (bkz. `docs/DECISIONS.md` #56). Bu, bilinçli olarak
ertelenmiş bir konudur (bkz. `docs/ROADMAP.md` FAZ 4E "bilerek ertelenenler").

**Sonuç:** Model dosyası değiştirilirse (farklı checkpoint, farklı export
ayarı) veya `ImagePreprocessor` mantığı değişirse, aynı boyutta ama artık
**anlamsal olarak farklı** embedding'ler üretilebilir — mevcut index bunu
otomatik algılamaz. Böyle bir değişiklikten sonra **[Faz 1 güncellemesi]**:

```
<ProductDirectory>\.lens\index.json
```

dosyası **elle silinmeli** (artık `%LocalAppData%\Lens\cache\` DEĞİL —
paylaşılan ürün dizininin içinde, bkz. `docs/ARCHITECTURE.md`), sonraki
"İndeksi Güncelle" tüm görselleri yeni modelle yeniden embed edecektir.
Bu paylaşılan bir dosya olduğu için silme işlemi **tüm kullanıcıları**
etkiler — koordinasyon gerekir (tek bir istasyonun local cache'i değil).
