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
│    ImageResourceLimits.EnsureWithinLimits (dosya boyutu/piksel       │
│      sayısı guard, tam decode'dan ÖNCE)                              │
│    → ImagePreprocessor (ImageSharp: resize 224 + center crop +      │
│      normalize) → CHW float tensor                                  │
│    → ONNX Runtime inference (CLIP vision encoder)                   │
│    → L2-normalize → float[512]                                      │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 4. Kalıcı cache — ImageIndex.Save / Load                             │
│    %LocalAppData%\Lens\cache\<path-hash>\index.json                 │
│    Atomic write (AtomicFileWriter: temp dosya + replace)             │
│    Load sırasında doğrulama: null/dimension≠512/NaN/Infinity        │
│      → "hepsi ya da hiçbiri" ile reddedilir, güvenli rebuild'e      │
│      düşülür (crash yok)                                             │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 5. Arama — SimilaritySearch.TopK                                     │
│    Query görseli aynı ClipEmbedder/ImagePreprocessor pipeline'ından  │
│      geçer (aynı guard, aynı preprocessing)                          │
│    Brute-force dot product (L2-normalize edilmiş vektörler için     │
│      cosine similarity'ye eşdeğer) tüm cache üzerinde                │
│    "Ara" öncesi search-before-refresh: DetectChanges (metadata-only, │
│      30 saniye TTL) — gerekirse otomatik incremental BuildOrUpdate   │
└─────────────────────────────────────────────────────────────────────┘
                                │
                                ▼
┌─────────────────────────────────────────────────────────────────────┐
│ 6. UI — MainWindow                                                    │
│    Top-10 sonuç grid'i + query/seçilen-sonuç karşılaştırma alanı     │
│    Sorunlu/Atlanan Dosyalar penceresi (Issues listesi)               │
│    Büyük önizleme/zoom penceresi (ImagePreviewWindow) — aynı         │
│      ImageResourceLimits guard'ı burada da devrede                  │
└─────────────────────────────────────────────────────────────────────┘
```

## Eşzamanlılık / Thread Modeli

- Ağır işler (dizin tarama, embedding, arama) `Task.Run` ile arka plan
  thread'inde çalışır; UI thread'i bloklanmaz.
- Hata durumları (UNC erişilemez, bozuk görsel, aşırı büyük görsel, bozuk
  cache) her katmanda yakalanıp kullanıcı dostu bir mesaja çevrilir —
  hiçbiri uygulamayı çökertmez veya "busy" durumunu kilitli bırakmaz.
- Tek Lens örneği/tek kullanıcı varsayımı ile tasarlanmıştır; çoklu eşzamanlı
  yazım koruması (mutex/lock) yoktur (bilinçli olarak ertelenmiş, bkz.
  `docs/PRODUCTION_CHECKLIST.md`).

## Depolama Modeli

- **Vector database / SQLite yok** — embedding'ler düz bir JSON dosyasında
  tutulur, arama brute-force'tur. Bu ölçek (~5000 görsele kadar) için yeterli
  kabul edilmiştir (bkz. `docs/DECISIONS.md` #23, #32).
- Her ürün dizini kendi cache dosyasına sahiptir (dizin yolundan türetilen
  hash ile anahtarlanır) — aynı dizine dönüldüğünde cache yeniden kullanılır,
  farklı dizinler birbirini etkilemez.
- Cache, model/preprocessing sürümünü etiketlemez (bkz. `docs/MODEL_CARD.md`
  "Model/Preprocessing Değiştiğinde Cache").

## Bilinçli Olarak Basit Tutulan Noktalar

Bunlar eksiklik değil, ölçeğe uygun bilinçli tercihlerdir — büyürse yeniden
değerlendirilir (bkz. `docs/PRODUCTION_CHECKLIST.md`):

- Brute-force cosine similarity (ANN/vector DB yok).
- Dosya kimliği: `RelativePath + FileSizeBytes + LastWriteTimeUtc` (tam
  içerik hash'i değil).
- Tek makine/tek kullanıcı; merkezi bir index/servis yok.
