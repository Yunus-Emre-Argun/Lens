# Mimari — Güncel Durum

Bu doküman yalnızca **şu an çalışan** mimariyi anlatır. Tarihsel kararlar,
alternatifler ve gerekçeler için `docs/DECISIONS.md` ve
`docs/ARCHITECTURE_PROPOSAL.md`'ye bakın (burada tekrarlanmaz). MVP/Production
gereksinim ayrımı için `docs/PROJECT_CONTEXT.md` ve `docs/PRODUCTION_REQUIREMENTS.md`.

## Bileşenler

| Proje | Sorumluluk |
|---|---|
| `Lens.Desktop` | WPF UI, kullanıcı etkileşimi, dosya diyalogları, log'a yazma tetikleyicisi |
| `Lens.Core` | İndeksleme, embedding, benzerlik arama, config/cache/log altyapısı — UI'dan bağımsız |
| `Lens.AiProof` | Konsol tabanlı doğrulama/benchmark aracı (`hardeningtest`, `stresstest`, `detectchanges` modları) — shipped app'in parçası değil |

## Uçtan Uca Veri Akışı

```
┌─────────────────────────────────────────────────────────────────────┐
│ 1. Ürün dizini çözümleme                                             │
│    appsettings.json (AdminDefaultProductDirectory)                  │
│    veya %LocalAppData%\Lens\config\ (UserOverrideProductDirectory)  │
│    veya kullanıcının manuel "Ürün Klasörü Seç" seçimi               │
│    → local yol ya da UNC yol (\\sunucu\pay\...)                     │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 2. Dosya sınıflandırma ve tarama — ImageIndex.BuildOrUpdate          │
│    FileClassifier: SupportedImage(.jpg/.jpeg/.png) /                │
│      UnsupportedImageFormat(.tif/.bmp/.webp/.gif) /                 │
│      NonImageFile(.pdf/.zip/.txt/...) / KnownHarmless(kendi .json)  │
│    Değişmeyen dosyalar (size+LastWriteTimeUtc eşleşirse) atlanır    │
│    Yeni/değişen dosyalar embed edilmek üzere ClipEmbedder'a gider   │
│    Geçici hata (kilit/network) → eski kayıt korunur, "removed"      │
│      sayılmaz; gerçek silme → directory snapshot'ında hiç yoksa     │
│      removed sayılır                                                 │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 3. Embedding — ClipEmbedder.Embed                                    │
│    ImageResourceLimits.TryGetPixelCount (header-only hint; artık     │
│      REDDETMEZ) → esik ustundeyse ImagePreprocessor ekonomik         │
│      (decoder-level downsampled) decode kullanir, altindaki          │
│      gorseller ONCEKI ile birebir ayni tam-cozunurluk yolu kullanir  │
│    → ImagePreprocessor (ImageSharp: resize 224 + center crop +      │
│      normalize) → CHW float tensor                                  │
│    → ONNX Runtime inference (CLIP vision encoder)                   │
│    → L2-normalize → float[512]                                      │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 4. Kalıcı SHARED index — ImageIndex.Save / Load / BuildOrUpdateWithLock │
│    <ProductDirectory>/.lens/index.json  (urun dizininin KENDI ICINDE, │
│      UNC olabilir — ARTIK %LocalAppData% DEGIL, bkz. DECISIONS.md #61)│
│    Writer: <ProductDirectory>/.lens/index.lock uzerinde exclusive    │
│      (FileShare.None) OS handle — tek-yazarli lock, load→scan→       │
│      update→save boyunca tutulur (Lens.Core.Indexing.IndexLock)      │
│    Reader kilit ALMAZ; writer calisirken bile stable index.json'u    │
│      okuyabilir                                                       │
│    Atomic write (AtomicFileWriter: benzersiz GUID temp dosya adi +   │
│      replace/move-overwrite fallback; hedef onceden silinmez)        │
│    Load sırasında doğrulama: null/dimension≠512/NaN/Infinity        │
│      → "hepsi ya da hiçbiri" ile reddedilir, güvenli rebuild'e      │
│      düşülür (crash yok)                                             │
│    Eski %LocalAppData%\Lens\cache\<hash>\index.json normal           │
│      operasyonda kullanilmiyor (otomatik silinmiyor da)              │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 5. Arama — SimilaritySearch.SearchWithThreshold                      │
│    Query görseli aynı ClipEmbedder/ImagePreprocessor pipeline'ından  │
│      geçer (aynı ekonomik decode esigi, aynı preprocessing)          │
│    Brute-force dot product (L2-normalize edilmiş vektörler için     │
│      cosine similarity'ye eşdeğer) tüm index üzerinde                │
│    Kullanıcının girdiği "Minimum benzerlik (%)" eşiği inclusive      │
│      filtrelenir (float-epsilon toleranslı), azalan sıra, en fazla   │
│      15 sonuç (bkz. DECISIONS.md #60)                                │
│    "Ara" öncesi (auto-index checkbox açıksa) search-before-refresh:  │
│      DetectChanges (metadata-only, 30 sn TTL) → gerekirse            │
│      BuildOrUpdateWithLock; checkbox kapalıysa hiç scan/write yok    │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 6. UI — MainWindow                                                    │
│    En fazla 15 sonuçluk grid (5x3) + query/seçilen-sonuç             │
│      karşılaştırma alanı (merkez hizalı, dengeli iki sütun)          │
│    Ayrıntılı son-başarılı-tarama sayaçları (yeni/güncellenen/        │
│      değişmeyen/silinen/okunamayan/desteklenmeyen görsel/dosya)      │
│    Sorunlu/Atlanan Dosyalar penceresi (Issues listesi, dosya bazlı)  │
│    Büyük önizleme/zoom penceresi (ImagePreviewWindow) — esik         │
│      ustundeki gorsellerde bounded DecodePixelWidth (reddetmez)     │
└─────────────────────────────────────────────────────────────────────┘
```

## Eşzamanlılık / Thread Modeli

- Ağır işler (dizin tarama, embedding, arama, shared index load/save, kilit
  alma denemesi) `Task.Run` ile arka plan thread'inde çalışır; UI thread'i
  bloklanmaz — bu artık shared/UNC `.lens` I/O'sunu da kapsar (klasör
  seçiminde/açılışta ve arama öncesi).
- Hata durumları (UNC erişilemez, bozuk görsel, aşırı büyük görsel, bozuk
  index, kilit alınamadı, kaydetme başarısız) her katmanda yakalanıp
  kullanıcı dostu bir mesaja çevrilir — hiçbiri uygulamayı çökertmez veya
  "busy" durumunu kilitli bırakmaz. Kaydetme başarısız olursa eski diskteki
  index bozulmaz ve sahte "güncel" mesajı gösterilmez.
- **Çoklu Lens örneği/kullanıcı artık desteklenir** (aynı paylaşılan ürün
  dizinine karşı): tek-yazarlı exclusive dosya kilidi (`IndexLock`,
  `.lens/index.lock`) eşzamanlı yazımı engeller; okuma kilitsizdir. Bu,
  distributed lock/queue/servis GEREKTİRMEYEN minimal bir dosya-tabanlı
  kilit — bkz. `docs/DECISIONS.md` #62.

## Depolama Modeli

- **Vector database / SQLite yok** — embedding'ler düz bir JSON dosyasında
  tutulur, arama brute-force'tur. Bu ölçek (~5000 görsele kadar) için yeterli
  kabul edilmiştir (bkz. `docs/DECISIONS.md` #23, #32).
- Canonical index artık **ürün dizininin kendi içinde**:
  `<ProductDirectory>/.lens/index.json` — dizin yoluna göre türetilen
  LocalAppData hash'li cache DEĞİL (bkz. `docs/DECISIONS.md` #61). Aynı
  paylaşılan dizine bağlanan tüm kullanıcılar/PC'ler AYNI index dosyasını
  görür.
  `Lens.Core.Config.AppPaths.SharedIndexFilePath` yolun kendisini üretmek
  side-effect-free'dir; `.lens` klasörü yalnızca yazan taraf tarafından
  gerektiğinde oluşturulur.
- Config (`appsettings.json`) ve user settings/logs hâlâ
  `%LocalAppData%\Lens\` altında — yalnızca embedding index'i taşındı.
- Cache, model/preprocessing sürümünü etiketlemez (bkz. `docs/MODEL_CARD.md`
  "Model/Preprocessing Değiştiğinde Cache").

### [PİLOT dalı] Profil doğrulamalı, modele özel index

> Bu alt bölüm `feature/dinov2-base-pilot` dalını anlatır. `main`'de yalnızca
> yukarıdaki profilsiz `.lens/index.json` vardır ve o dosya pilot dalda da
> **hiç değiştirilmez**.

Pilot dalda index'in nerede/hangi biçimde saklandığını `IIndexStore`
belirler; `ImageIndex` bunu artık kendisi bilmez. İki uygulama vardır:

| Store | Yol | Biçim | Boyut |
|---|---|---|---|
| `LegacyClipIndexStore` | `<ÜrünDizini>/.lens/index.json` | düz `ImageIndexEntry` dizisi (şema 1) | 512 |
| `ProfiledIndexStore` | `<ÜrünDizini>/.lens/indexes/<profil>/index.json` | `SchemaVersion` + `EmbeddingProfile` + `Entries` (şema 2) | profilden |

Kilit dosyası (`index.lock`) da store'un **kendi klasöründedir** — iki farklı
modelin yazıcıları birbirini bloklamaz ve birbirinin dosyasına dokunmaz.
Yeni yol `.lens` klasörünün **altında** olduğu için mevcut klasör taraması
(`Directory.EnumerateFiles`, yalnızca üst dizin) ürün görseli/sorunlu dosya
sayaçlarına karışmaz.

Yüklemede kayıtlı profil ile çalışan profil tam karşılaştırılır; herhangi bir
alan farklıysa (model kimliği/revision/**model dosyası SHA-256**/ön işleme
sürümü/embedding boyutu/özellik türü/crop stratejisi/normalizasyon/şema
sürümü) embedding'ler kullanılmaz, index tamamen yeniden oluşturulur ve
nedeni kullanıcıya + log'a yazılır. Bu, `docs/DECISIONS.md` #95'in
uygulanmış halidir.

Tarama, geçici hata toleransı (#55), "hepsi ya da hiçbiri" bozuk cache
politikası (#56), atomik yazma (#40, #63) ve tek-yazarlı kilit (#62) mantığı
**tek kopya** olarak `ImageIndex`/`IndexLock` içinde kalır — her model için
yeniden yazılmaz.

### [PİLOT dalı - `feature/clip-pattern-pilot`] Çoklu görünümlü CLIP

Desen pilotu, kayıt başına **6 görünüm** tutar (tam görüntü + %60'lık 5
örtüşen bölge). Bunlar ayrı alanlarda değil, **birleşik tek bir vektörde**
(512'lik bloklar ard arda, 3072 uzunluk) saklanır. Gerekçe: mevcut
index/kilit/atomik-yazma altyapısı kayıt başına tek `float[]` taşır; bloklama
sayesinde o altyapı **kopyalanmadan** yeniden kullanılır.

Normalizasyon **görünüm bazındadır** — birleşik vektörü toplu normalize etmek
görünümleri birbirine bulaştırır ve görünüm-bazlı benzerliğin anlamını bozardı.

Okuma tarafında `Lens.Core.Search.PatternSimilaritySearch` vektörü bloklara
böler, arama anında katalog ortalamasını çıkarıp yeniden normalize eder
(whitening) ve skoru `0,5×global + 0,5×en iyi görünüm çifti` olarak
hesaplar. Eşik/sıralama/en-fazla-sonuç sözleşmesi `SimilaritySearch` ile
birebir aynıdır. Whitening ortalaması **diskte saklanmaz** — katalog
değiştiğinde eskiyeceği için her aramada yeniden hesaplanır.

`%LocalAppData%` veri klasörünün adı artık `LensDataFolder` assembly
metadata'sıyla derleme zamanında değiştirilebilir (bkz.
`Lens.Core.Config.AppPaths`). Bu, bir pilotun kullanıcının asıl kurulumunun
ayarlarını/loglarını ezmemesi içindir; metadata yoksa davranış öncekiyle
birebir aynıdır.

### [PİLOT dalı] Model soyutlaması

`IImageEmbedder` (profil + `Embed`) arayüzünü `ClipEmbedder` ve
`DinoV2Embedder` uygular. Ön işleme sabitleri modele ait
`ImagePreprocessingProfile` içindedir — CLIP ve DINOv2 değerleri aynı statik
sınıfta karışık durmaz. Embedding doğrulaması (yanlış boyut, NaN/Infinity,
sıfır norm) ve karşılaştırılabilirlik kontrolü `EmbeddingVector` içinde tek
yerdedir; `SimilaritySearch` farklı boyutlu vektörler için sessiz skor yerine
açık hata üretir.

## Bilinçli Olarak Basit Tutulan Noktalar

Bunlar eksiklik değil, ölçeğe uygun bilinçli tercihlerdir — büyürse yeniden
değerlendirilir (bkz. `docs/PRODUCTION_CHECKLIST.md`):

- Brute-force cosine similarity (ANN/vector DB yok).
- Dosya kimliği: `RelativePath + FileSizeBytes + LastWriteTimeUtc` (tam
  içerik hash'i değil).
- Merkezi bir servis/veritabanı yok — "shared index" yalnızca paylaşılan
  klasördeki bir JSON dosyası + basit dosya kilidi (Faz 1); gerçek bir
  concurrency altyapısı (queue, distributed lock, DB) bilinçli olarak
  kurulmadı.
