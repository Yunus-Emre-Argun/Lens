# Changelog

Bu doküman [Keep a Changelog](https://keepachangelog.com/) biçimini takip
eder. Aşağıdaki geçmiş girdileri, mevcut git commit geçmişi ve
`docs/ROADMAP.md`'deki faz kayıtlarından **geriye dönük** doldurulmuştur —
bu proje henüz tag tabanlı bir release süreci kullanmadığı için sürüm
numarası yerine faz adı ve tarih kullanılmıştır. Buradan sonrası
`docs/RELEASE_PROCESS.md`'de önerilen tag tabanlı release sürecine göre
güncellenmelidir.

## [DENEY/PİLOT — CLIP Desen Odaklı İyileştirme] — 2026-09-10

> **Durum: deney dalı (`feature/clip-pattern-pilot`), ayrı bir git worktree'de
> yürütüldü.** `main`, DINOv2 pilot dalı ve mevcut ClickOnce paketleri
> DEĞİŞTİRİLMEMİŞTİR (hash karşılaştırmasıyla doğrulandı). **CLIP ağırlıkları
> eğitilmedi**; yalnızca modelin etrafındaki kadraj/renk/skor katmanları
> değişti. Tam ölçüm kaydı: `docs/CLIP_PATTERN_EXPERIMENT.md`.

### Ölçüm (gerçek katalog: 2.007 görsel, 336 sentetik dönüşüm sorgusu)
- CLIP baseline val R@1 **85,1%** → seçilen yöntemle **96,4%**; MRR 0,880 →
  0,976; doğru–rakip ayrımı 0,050 → **0,244** (5×).
- **Elenen yöntemler (negatif bulgu):** 3×3 ızgara, gri tonlama tek başına ve
  sorgu tarafı dönüş çeşitlemesi baseline'ı **iyileştirmedi veya kötüleştirdi**.
- **DINOv2-Base aynı koşullarda ölçüldü**: val R@1 93,5% (1 görünüm).
  Seçilen CLIP yöntemi bunu geçiyor ama **6 görünüm** karşılığında —
  ~3,4× indeksleme süresi, 4× indeks boyutu, ~3× sorgu gecikmesi.
  Elimizdeki tek gerçek çiftte DINOv2 daha iyi ayırıyor (+0,097 vs +0,077).
  **DINOv2'nin yerine önerilmemektedir.**

### Eklendi
- `Lens.Core.Ai.ClipPatternProfile` / `ClipPatternEmbedder`: görsel başına
  6 görünüm (tam görüntü + %60'lık 5 örtüşen bölge), her görünüm **ayrı**
  L2-normalize, birleşik 3072 boyutlu vektör.
- `Lens.Core.Search.PatternSimilaritySearch`: arama anında katalog-ortalaması
  whitening + `0,5×global + 0,5×en iyi görünüm çifti` birleştirme. Eşik/
  sıralama/en-fazla-sonuç sözleşmesi `SimilaritySearch` ile AYNI.
- Profil doğrulamalı ayrı index: `.lens/indexes/clip-pattern-v1/` (şema 3).
- `Lens.AiProof clippattern`: deney harness'ı (görünüm bazında embedding
  önbelleği, dev/val ayrımı, birebir kopya farkındalığı, eşik taraması,
  aynı koşullarda DINOv2 karşılaştırması).
- `docs/CLIP_PATTERN_EXPERIMENT.md`, `ClickOnceClipPattern.pubxml`.
- `Lens.AiProof hardeningtest` Grup O: 46 yeni kontrol.

### Değiştirildi
- Bu dalda aktif yöntem desen odaklı CLIP; paketlenen model
  `clip-vision-b16-openai.onnx` (DINOv2 modeli pakete DAHİL DEĞİL).
- Başlangıç eşiği **%55** — mevcut üretimin (CLIP @ %80) tutulma oranını
  (%96) birebir korur. Geçici pilot değeridir.
- Pencere başlığı: `Lens - Ürün Görsel Arama (CLIP Desen Pilotu)`.
- `AppPaths`: veri klasörü adı derleme zamanında değiştirilebilir hale geldi
  (`LensDataFolder` assembly metadata'sı). Bu dalda `%LocalAppData%\Lens.ClipPattern\`
  kullanılır — kullanıcının DINOv2 test tercihlerini/loglarını EZMEZ.
  Metadata yoksa davranış öncekiyle BİREBİR aynıdır.

### Korunanlar (kanıtlı)
- `publish/ClickOnce/` ve `publish/ClickOnce-dinov2-base/`: **478'er dosyanın
  tamamı bayt bayt aynı** (öncesi/sonrası SHA-256 listesi karşılaştırıldı).
- Kullanıcının `.lens/index.json` ve `.lens/indexes/dinov2-base-v1/`
  indeksleri **değişmedi**.
- İşlem paneli, busy guard, otomatik indeksleme, Yeni Arama temizliği,
  kaydırma, sayısal girişler, varsayılan 20 / azami 999 sonuç, temalar,
  sürükle-bırak, büyütme DEĞİŞMEDİ. Arayüz tasarımına dokunulmadı.

### ClickOnce: bu kez GERÇEK yan yana kurulum
- `AssemblyName` profile özel olarak `Lens.Desktop.ClipPattern` yapıldı →
  manifest kimliği `Lens.Desktop.ClipPattern.application`, mevcut
  `Lens.Desktop.application` kimliğinden **FARKLI**. Manifest düzeyinde
  doğrulandı; DINOv2 pilotunun üzerine kurulmaz.
- **Kurulum yapılmadı** — gerçek yan yana kurulum davranışı kullanıcı
  testiyle doğrulanacak.

### Bilinen sınırlamalar
- Asıl iş senaryosu ("düz desen ↔ ürüne uygulanmış hâli") için etiketli veri
  yok; **doğrulanamadı**. Sentetik dönüşümler bunu kanıtlamaz.
- Tüm R@k/MRR değerleri sentetik sorgulardan gelir; gerçek ground truth tek
  bir çiftle sınırlıdır ve yalnızca tek yönde geçerlidir.
- 5.000 görsel süresi **tahmindir** (~2,1 saat); ölçülen 2.007 görselde
  ~51 dakikadır.
- Whitening sonrası benzemeyen kayıtlar negatif skor alabilir; %0 eşiği artık
  "her şey" demek değildir.

## [PİLOT — DINOv2 ViT-B/14 Entegrasyonu] — 2026-09-10

> **Durum: deney dalı (`feature/dinov2-base-pilot`), çalıştırılabilir pilot.**
> `main` dalı DEĞİŞTİRİLMEMİŞTİR ve production model kararı ALINMAMIŞTIR.
> ClickOnce dalı/paketi bu turda güncellenmemiştir. Kullanıcının görsel
> kabul testi HENÜZ YAPILMAMIŞTIR. Detay: `docs/DECISIONS.md` #96.

### Eklendi
- **DINOv2 ViT-B/14 embedder** (`Lens.Core.Ai.DinoV2Embedder`): resmî
  `facebook/dinov2-base` ağırlıklarından üretilen ONNX modelini CPU'da
  çalıştırır; çıktı son katmanın **CLS token**'ı, 768 boyut, L2-normalize.
- **Model/embedding profili** (`Lens.Core.Ai.EmbeddingProfile`): model
  kimliği, revision, model dosyası SHA-256, ön işleme sürümü, embedding
  boyutu, özellik türü, crop stratejisi, normalizasyon ve index şema sürümü.
- **Model-bağımsız soyutlama** (`Lens.Core.Ai.IImageEmbedder`): indeksleme ve
  arama katmanı artık somut bir model tipine bağlı değil.
- **Ön işleme profilleri** (`Lens.Core.Ai.ImagePreprocessingProfile`): CLIP ve
  DINOv2 sabitleri artık aynı sınıfta karışık durmaz; her model kendi resmî
  değerlerini (DINOv2: kısa kenar 256 → 224 center crop, ImageNet mean/std)
  taşır.
- **Profil doğrulamalı, modele özel index** (`ProfiledIndexStore`):
  `<ÜrünDizini>/.lens/indexes/dinov2-base-v1/index.json` (kilit dosyası da
  aynı klasörde). Belge artık düz kayıt listesi değil; `SchemaVersion` +
  `EmbeddingProfile` + `Entries` zarfına sahip.
- **Embedding doğrulaması** (`Lens.Core.Ai.EmbeddingVector`): yanlış boyut,
  NaN/Infinity ve sıfır norm artık sessizce geçmez, açık hata üretir.
- `benchmark/export_dinov2_onnx.py`: ONNX dışa aktarma + PyTorch↔ONNX sayısal
  doğrulama betiği (yalnızca geliştirme aracı; son kullanıcıda Python
  gerekmez).
- `Lens.AiProof dinosmoke`: dönüş/renk/gri/parlaklık/kısmi crop/ölçek/konum
  dayanıklılığı, iş kuralı sıralaması, eşik etkisi ve hız ölçümlerini gerçek
  üretim yolunda çalıştıran smoke testi.
- `Lens.AiProof ortbench`: ONNX Runtime CPU thread davranışını ölçen tanılama
  modu (aşağıdaki hız bulgusunu kanıta bağlar).
- `Lens.AiProof hardeningtest` Grup N: 80 yeni kontrol (profil karşılaştırması,
  index yolu ayrımı, şema/bozulma reddi, boyut güvenliği, kilit ayrımı,
  atomik kayıt, gerçek model ile embedding sözleşmesi).

### Değiştirildi
- Uygulamanın aktif modeli bu dalda **DINOv2-Base**; `models/dinov2-base.onnx`
  publish çıktısına kopyalanır. **CLIP modeli pakete dahil edilmez** (dosya
  repoda durmaya devam eder, geri dönüş tek satırlık bir csproj değişikliğidir).
- Başlangıç "Minimum benzerlik (%)" değeri bu dalda **%55** (CLIP dönemi
  değeri %80'di) — geçici pilot eşiğidir. Bu eşik kullanıcı ayarlarında
  **kalıcı saklanmadığı** için (kod incelemesiyle doğrulandı) bir ayar göçü
  yazılmasına gerek olmamıştır; kullanıcının elle girdiği geçerli değere
  dokunulmaz.
- `SimilaritySearch` artık sorgu ile kayıt embedding boyutlarının eşitliğini
  doğrular: 768 boyutlu sorgu ile 512 boyutlu eski kayıt karşılaştırılmaya
  çalışılırsa sessiz/yanlış skor yerine açıklayıcı hata üretilir.
- `ImageIndex` artık depolamayı `IIndexStore` üzerinden yapar; tarama, geçici
  hata toleransı, atomik yazma ve kilit mantığı **tek kopya** olarak kalır.
  Store verilmeyen eski çağrılar eski CLIP dosyasını kullanmaya devam eder.

### Korunanlar
- **Eski CLIP index'i (`.lens/index.json`) okunmaz, yazılmaz, silinmez** —
  bayt bayt değişmediği testle doğrulanmıştır (Grup N: N37, N69). CLIP
  sürümüne dönülürse yeniden indeksleme gerekmez.
- İşlem/bekleme paneli, busy/kilit korumaları, otomatik indeks kontrolü,
  "Yeni Arama" temizleme, sürükle-bırak, çift tık büyütme, tema sistemi,
  yerleşim, sonuç listesi/kaydırma, varsayılan 20 / azami 999 sonuç ve
  sayısal giriş doğrulamaları DEĞİŞMEDİ. Arayüz tasarımına dokunulmadı.

### Ölçümler (bu makine, CPU, 20 çekirdek)
- PyTorch ↔ ONNX: 8 gerçek görselde en kötü cosine **0,99999994**, en büyük
  mutlak fark **2,3e-05** → sayısal olarak eşdeğer.
- Dönüşüm dayanıklılığı (5 kaynak × 15 dönüşüm, 193 görsellik katalog):
  **15 dönüşümün tamamında R@1 %100** (dönüş, hue, gri, parlaklık, kontrast,
  sol/sağ/merkez crop, 2× yakınlaştırma, küçültme, köşeye kaydırma dahil).
- İş kuralı: 5/5 kaynakta "aynı motif–farklı renk", "aynı renk–farklı motif"
  rakiplerinin üstünde sıralandı.
- Eşik: %55'te doğru kaynakların %100'ü listede kalıyor; %80'de %74,7'si.
- Hız: model yükleme 464 ms + SHA-256 219 ms; görsel başına 427 ms
  (ön işleme 15 ms + çıkarım); arama 193 kayıtta 0,7 ms.

### Düzeltildi (ölçülmüş performans bulgusu)
- ONNX Runtime'ın varsayılan **spinning** thread politikası, Lens'in
  "decode → çıkarım" sıralı indeksleme döngüsünde ImageSharp ile birbirini aç
  bırakıyordu: görsel başına **1155 ms**. `session.intra_op.allow_spinning=0`
  ile **439 ms** — 5.000 görselde ~96 dakika yerine ~37 dakika. Ayar yalnızca
  thread bekleme politikasıdır, **sayısal çıktıyı değiştirmez** (smoke testi
  skorları birebir aynı kaldı).

### Deneme paketi (ClickOnce)
- `Properties/PublishProfiles/ClickOnceDinoV2.pubxml` → `publish/ClickOnce-dinov2-base/`
  (506 MB): kullanıcının pilotu kurup deneyebilmesi için ayrı bir ClickOnce
  paketi. **Mevcut CLIP profili ve `publish/ClickOnce/` klasörü
  DEĞİŞTİRİLMEMİŞTİR** (dosya tarihleri korundu).
- Görünen ad `Lens (DINOv2 Pilot)`, ClickOnce sürümü `1.1.0.0`. Sürüm bilerek
  `$(FileVersion)`'dan türetilmedi: mevcut CLIP paketi de `1.0.0.0` olduğu için
  aynı sürümle kurulum "zaten kurulu" sayılıp pilot denenemezdi.
  `Lens.Desktop.csproj`'daki sürüm alanları DEĞİŞMEDİ.
- Paket doğrulaması: DINOv2 modeli ve boş `appsettings.json` şablonu var;
  CLIP modeli, `.pdb`, gömülü PDB yolu, yerel geliştirici yolu, gerçek
  kullanıcı ayarı, index, log ve ürün görseli YOK; masaüstü kısayolu
  (`createDesktopShortcut="true"`) manifestte mevcut.

### Bilinen sınırlamalar
- **ClickOnce yan yana kurulum YOK:** her iki paket de aynı uygulama kimliğini
  (`Lens.Desktop.application`) taşıdığı için pilot, kurulu Lens'in ÜZERİNE
  kurulur. Geri dönüş: `publish\ClickOnce\setup.exe`. İndeks açısından risk
  yoktur — DINOv2 kendi klasörünü kullanır, CLIP index'ine dokunmaz.
- Paket kurulmadı/çalıştırılmadı; kurulum davranışının gerçek doğrulaması
  kullanıcıyı bekliyor.
- Saf çıkarım (aynı tensor, ImageSharp araya girmeden) ~115 ms iken indeksleme
  döngüsünde ~422 ms ölçülmektedir; fark tamamen giderilememiştir.
- Hız ölçümleri **bu geliştirme makinesine** aittir. Hedef ofis bilgisayarının
  çekirdek sayısı farklıdır; 5.000 görsel tahmini orada doğrulanmalıdır.
- Dönüşüm testi 193 görsellik bir havuzda ve 5 kaynak görselle yapılmıştır —
  `docs/MODEL_BENCHMARK.md`'deki 2.007 görsellik ölçümün yerine geçmez.
- Gerçek üretim kataloğunda doğrulama YAPILMAMIŞTIR.
- Canlı arayüz açılmamıştır; görsel kabul kullanıcıyı beklemektedir.

## [Araştırma — Geniş Veri Model Benchmarkı] — 2026-09-09

> **Durum: yalnızca araştırma ve dokümantasyon.** Production kaynak kodu,
> model dosyası, index ve ClickOnce paketi bu commit'te **değiştirilmemiştir**.
> Uygulamada hâlen CLIP ViT-B/16 kullanılmaktadır. Detay/gerekçe:
> `docs/DECISIONS.md` #93, #94, #95.

### Eklendi
- `docs/MODEL_BENCHMARK.md`: 2.007 görsellik yerel benchmark veri kümesinde,
  40 kaynak görselden üretilen 720 dönüşüm sorgusuyla (dönüş, eğiklik, kısmi
  crop, ölçek, parlaklık, kontrast, hue, tam gri, perspektif) yapılan model
  karşılaştırmasının tam kaydı.
- Karşılaştırılan modeller: **CLIP ViT-B/16** (mevcut), **DINOv2 ViT-S/14**,
  **DINOv2 ViT-B/14** — her biri kendi resmî ön işlemesiyle, model
  kimliği/revision/SHA-256 kayıtlı.

### Temel sonuç
- Tam veride R@20 / R@100 / MRR / p95 sıra: CLIP %93,5 / %96,3 / 0,849 / 40 —
  DINOv2-S %97,8 / %98,8 / 0,916 / 4 — DINOv2-B %98,8 / %99,3 / 0,927 / 4.
- Dönüş (90°/180°/270°) ve renk testlerinde DINOv2 belirgin üstün: DINOv2-S
  dönüşlerde R@1 %100, tam gri sorguda %98; CLIP sırasıyla %88–95 ve %72.
- **Pilot adayı: DINOv2 ViT-S/14** — Base'e göre R@20'de 1,0 puan geride ama
  1,9× hızlı, 3,9× küçük ve dönüş dayanıklılığında üstün. Bu bir **pilot
  adayıdır, production kararı değildir.**
- Çoklu crop/tile, renk nötrleştirme ve sorgu-zamanı dönüş (TTA) **gerekmedi**;
  tek global embedding kabul hedeflerini karşıladı.
- PyTorch → ONNX dönüşümü 720 sorgunun hiçbirinde sıralamayı değiştirmedi.

### Notlar
- Mevcut **%80 eşiği yeni modele taşınamaz**: DINOv2-S'te doğru eşleşmelerin
  yalnızca %68,2'sini listede bırakıyor. Önerilen başlangıç aralığı %55–60.
- Model değişimi **tam yeniden indeksleme** gerektirir; index'e model kimliği/
  hash/ön işleme sürümü alanları eklenmeden geçiş yapılmamalıdır.
- Ölçümler üretim benzeri ama **gerçek üretim kataloğu olmayan** bir veri
  kümesinde yapılmıştır; gerçek katalogda doğrulama açık maddedir.

## [Deney — Sonuç Sınırı 999] — 2026-09-09

> **Durum: `feature/operation-progress-ui` deney branch'inde eklenmiştir;
> `main`'e merge/push YAPILMADI, publish/ClickOnce paketleri
> güncellenmedi.** Detay/gerekçe: `docs/DECISIONS.md` karar #92 (SUPERSEDES #91).

### Değişti
- Maksimum sonuç sınırı 300'den **999'a** çıkarıldı (varsayılan sonuç sayısı
  **20 olarak değişmedi**). Kullanıcının girebileceği aralık artık 1-999;
  uyarı metinleri ve sayı kutusu artırma/azaltma okları buna göre çalışır.
  Önceden kaydedilmiş 200/300 gibi tercihler aynen geçerli kalmaya devam
  ediyor.

### Notlar
- `NumericInputFilter`in 3 haneli giriş sınırı artık `SimilaritySearch.
  MaxResults` (999) ile tam hizalı — "en fazla sonuç" alanında 3 haneli
  (veya daha az) her pozitif tam sayı zaten aralık içinde, yalnızca 0 ve
  4+ haneli girdiler (karakter seviyesinde zaten engellenir) aralık dışı
  kalıyor. Test yazımı sırasında bir eski test verisinin (`ValidateOrDefault
  (500)`, "aralık dışı" örneği) 999-limit ile artık aralık İÇİNDE kaldığı
  fark edildi ve düzeltildi.
- 999 kartlık sonuç listesinin bellek/kaydırma maliyeti 300'e göre ~3 kat
  artar — mevcut sıralı arka plan thumbnail decode UI thread'i bloklamıyor
  (bir önceki turdaki işlem ilerleme paneli bu senaryoda daha faydalı hale
  geliyor), ama büyük bir virtualization yeniden tasarımı bu görevde
  YAPILMADI, gerçek ekranda ölçülmedi.
- `dotnet build` Debug/Release 0 warning/0 error; `Lens.AiProof
  hardeningtest` 221/221 PASS.

## [Deney — İşlem İlerleme Paneli ve Sonuç Sınırı 300] — 2026-09-09

> **Durum: `feature/operation-progress-ui` deney branch'inde eklenmiştir;
> `main`'e merge/push YAPILMADI, publish/ClickOnce paketleri
> güncellenmedi.** Detay/gerekçe: `docs/DECISIONS.md` karar #90, #91.

### Eklendi
- Arama, model hazırlama ve indeksleme sırasında ana pencerenin İÇİNDE
  çalışan bir işlem ilerleme paneli: karşılaştırma+sonuç bölümünün üzerini
  kaplayan yarı saydam bir katman ve ortalanmış beyaz bir kart (başlık +
  gerekirse açıklama + progress bar + ilerleme metni). Yeni pencere/modal/
  Windows bildirimi DEĞİL. Klasör yolu satırı katmanın dışında, işlem
  boyunca görünür ve seçilip kopyalanabilir kalır.
- İndeksleme (manuel "İndeksi Güncelle" ve arama öncesi otomatik indeksleme
  AYNI kod yolundan) gerçek `Done/Total`'a dayalı belirli ilerleme gösterir:
  "Desenler indeksleniyor…" başlığı, "1.248 / 5.000 — %25" biçiminde
  ilerleme metni. Arama sırasında ("Benzer desenler aranıyor…") belirsiz
  (sürekli hareket eden) ilerleme; sonuç/thumbnail hazırlama aşaması
  ("Sonuçlar hazırlanıyor…") kendi `Done/Total`'ıyla belirli ilerlemeye
  geçer. Tek bir arama içinde otomatik indeksleme gerekirse panel kapanıp
  tekrar açılmadan yalnızca başlık/ilerleme türü değişir.
- Kısa işlemlerde panelin yanıp sönmesini önlemek için 250ms gecikmeli
  açılış: işlem bu süreden kısa sürerse panel hiç görünmez; yeni bir işlem
  başladığında önceki işlemin gecikmiş gösterimi yeni paneli etkilemez.
- Maksimum sonuç sınırı 200'den **300'e** çıkarıldı (varsayılan sonuç
  sayısı **20 olarak değişmedi**). Kullanıcının girebileceği aralık artık
  1-300; "En fazla 300 sonuç listeleyebilirsiniz." ve "Lütfen 1-300
  arasında bir tam sayı girin." uyarıları, sayı kutusu artırma/azaltma
  okları ve sonuç hazırlama sırasındaki ilerleme metni buna göre çalışır.

### Değişti
- Model yükleme (`ClipEmbedder` oluşturma) artık arka planda çalışıyor —
  önceden UI thread'inde senkrondu ve ilk arama/indekslemede fark edilir
  bir donmaya yol açabiliyordu. Aynı anda iki model oluşturulması bir
  kilitle engellenir; başarılı model örneği tekrar kullanılır.
- 5.000 dosyalık bir indekslemede UI mesaj kuyruğunu gereksiz doldurmamak
  için ilerleme güncellemeleri (indeksleme ve sonuç/thumbnail hazırlama)
  yüzde değiştiğinde veya ~100ms'de bir yapılır; son değer (%100 / tüm
  sonuçlar hazır) bu sınırlamadan bağımsız her zaman gösterilir.
- `SimilaritySearch.MaxResults` (tek teknik kaynak) 200'den 300'e çıktı;
  `MaxResultsPreference.MaxAllowed` bundan türemeye devam ettiği için diğer
  hiçbir katmanda ayrı bir "300" sabiti eklenmedi.

### Korundu (değişmedi)
- Mevcut `_busyDepth`/`IsBusy`/`SetBusy` sorgu kilidi mantığı — panel bu
  kilide, onu yeniden yazmadan "biniyor" (arama/indeksleme sırasında sorgu
  görseli değişimi, sürükle-bırak, minimum benzerlik/sonuç sayısı girişleri,
  ikinci bir işlemin başlaması gibi korumalar aynen sürüyor).
- `SetIndexStatus`/`SetSearchStatus`/`RestoreIndexStatus` ayrımı, doğrulama
  hatalarında panelin hiç açılmaması, sonuç bulunamamasının hata sayılmaması,
  başarı/hatada panelin her zaman kapanması (`try/finally`).
- Sonuç sıralaması/eşik filtresi, benzerlik hesaplaması, index formatı,
  tema renkleri, ClickOnce/publish çıktısı.
- `NumericInputFilter`in 3 haneli giriş sınırı (300 zaten 3 hane).

### Notlar
- Bu artış, önceki bir konuşmada saptanan "gerçek ürün kataloğunda
  beklenenden benzer bir desenin arama sonuçlarında hiç çıkmaması"
  sorununu ÇÖZMEZ — o sorunun kök nedeni ayrı; bu değişiklik yalnızca
  hedef sonuç sırası 201-300 arasında olan durumlarda görünürlük sağlar.
- Yeni üçüncü taraf paket/animasyon kütüphanesi eklenmedi. "İptal" düğmesi
  bilerek eklenmedi — güvenli iptal ayrı bir görev olarak bırakıldı.
- `dotnet build` Debug/Release 0 warning/0 error; `Lens.AiProof
  hardeningtest` 217/217 PASS. Canlı GUI kullanıcı izni olmadan açılmadı —
  gerçek ekranda panelin görünümü/DPI ölçeklerindeki taşma riski kullanıcı
  tarafından ayrıca doğrulanmalı.

## [Düzeltme — "Yeni Arama" Eski Sonuç Durumunu Temizlemiyordu] — 2026-09-08

> **Durum: `feature/clickonce-deployment` branch'inde eklenmiştir; `main`'e
> merge beklemektedir.** Detay/gerekçe: `docs/DECISIONS.md` karar #89.

### Düzeltildi
- "Yeni Arama" sonrasında sorgu/sonuçlar temizlense de üst durum alanında
  önceki aramadan kalan `N sonuç gösteriliyor.` / `Sonuç bulunamadı.` /
  `Arama başarısız oldu.` gibi eski mesajlar ekranda kalmaya devam
  ediyordu. Kök neden: `IndexStatusText`, hem gerçek indeks/klasör
  durumu hem aramaya özgü geçici mesajlar için aynı metotla yazılıyordu;
  "Yeni Arama" bu metni hiç sıfırlamıyordu.

### Değişti
- `MainWindow`'a iki ayrı, açık metot eklendi: `SetIndexStatus` (gerçek
  indeks/klasör durumunu hem gösterir hem "baseline" olarak kaydeder) ve
  `SetSearchStatus` (yalnızca aramaya özgü geçici mesajı gösterir,
  baseline'ı değiştirmez). `NewSearchButton_Click` artık `RestoreIndexStatus()`
  çağırarak en son gerçek indeks/klasör durumuna döner — gerçek bir hata/
  uyarı varsa bunu sahte bir "hazır" mesajıyla örtmez.

### Korundu (değişmedi)
- `Son başarılı tarama` istatistikleri (`_lastSuccessfulStats`/
  `DetailedStatsText`) ve `Sorunlu / Atlanan Dosyalar` butonu/listesi
  (`_lastIssues`) — bunlar sorguya değil seçili klasörün son indeks
  taramasına bağlıdır, "Yeni Arama"da hiç dokunulmadı.
- Seçili tarama klasörü, yüklenmiş indeks, ürün sayısı, tema, minimum
  benzerlik, en fazla sonuç, otomatik indeks tercihi, kullanıcı ayarları.
- Arama algoritması, benzerlik hesaplaması, sonuç limiti, index yapısı,
  model, tema renkleri/Lime varsayılanı, ikon, ClickOnce kısayolu.

### Notlar
- Bu değişiklik yalnızca `Lens.Desktop` (WPF) katmanında — `Lens.Core`'a
  dokunulmadı, bu yüzden `Lens.AiProof`'a yeni otomatik test eklenemedi
  (mevcut, belgeli mimari sınır — `ParseTheme` ile aynı durum); 9 kabul
  senaryosu kod incelemesiyle doğrulandı, gerçek ekran kontrolü kullanıcıya
  bırakıldı.
- ClickOnce paketi yeniden üretildi; masaüstü kısayolu, ikon, Lime
  varsayılanı, model, `appsettings.json`, sürüm davranışı (artırılmadı)
  değişmedi.

## [Yeni Kullanıcılarda Varsayılan Tema: Lime] — 2026-09-08

> **Durum: `feature/clickonce-deployment` branch'inde eklenmiştir; `main`'e
> merge beklemektedir.** Detay/gerekçe: `docs/DECISIONS.md` karar #88
> (SUPERSEDES #67, #68).

### Değişti
- Yeni kullanıcılar, ayar dosyası bulunmayanlar ve kayıtlı `Theme` alanı
  boş/bilinmeyen/geçersiz olanlar için güvenli varsayılan tema `Normal`'den
  `Lime`'a değiştirildi:
  - `Lens.Core.Config.UserSettings.Theme` alan varsayılanı.
  - `MainWindow.ParseTheme`'in boş/bilinmeyen/geçersiz girdi için güvenli
    dönüşü.
  - `MainWindow._currentTheme` başlangıç değeri (tutarlılık için).

### Korundu (değişmedi)
- Kullanıcının **önceden açıkça kaydettiği** geçerli bir tema tercihi
  (`Normal`, `Açık`, `Koyu`, `Açık Sepya`, `Koyu Sepya`, `Lime`) — hiçbiri
  yeni varsayılana çevrilmez, olduğu gibi uygulanır.
- 6 temanın kendisi, renkleri (`ThemePalette`), tema menüsü sıralaması.
- Açılışta tercihin yalnızca **uygulandığı**, diske tekrar **yazılmadığı**
  (`persist:false`) davranış.
- Tema seçimi yalnızca ilgili bilgisayarın `%LocalAppData%\Lens\config\
  user-settings.json` dosyasında saklanır — paylaşılan hiçbir yere
  yazılmaz.
- Arama Ayarları panelinin beyaz görünümü, tema menüsü seçenekleri, ClickOnce
  masaüstü kısayolu/ikon/sürüm davranışı.

### Notlar
- XAML'deki statik başlangıç renkleri hâlâ "Normal" değerleriyle yazılı —
  kullanıcı bunları hiç görmez, çünkü `MainWindow` constructor'ı pencere
  gösterilmeden önce gerçek/varsayılan temayı senkron uyguluyor (görünür bir
  "parlama" riski yok, kod incelemesiyle doğrulandı) — bu yüzden gereksiz
  XAML tekrarına girilmedi.
- `Lens.AiProof` Grup I testlerine 3 yeni kontrol eklendi (açıkça kaydedilmiş
  "Normal" korunuyor, boş/bilinmeyen `Theme` Core katmanında bozulmadan
  taşınıyor); mevcut I1/I3 yeni "Lime" varsayılanına göre güncellendi.
- ClickOnce paketi yeniden üretildi; masaüstü kısayolu, ikon, model,
  `appsettings.json`, self-contained, güncelleme/sürüm davranışı
  DEĞİŞMEDİ.
- Canlı arayüz kullanıcı izni olmadan açılmadı.

## [Lens Uygulama ve ClickOnce İkonu] — 2026-09-08

> **Durum: `feature/clickonce-deployment` branch'inde eklenmiştir; `main`'e
> merge beklemektedir.** Detay/gerekçe: `docs/DECISIONS.md` karar #87,
> `docs/CLICKONCE.md` §2c.

### Eklendi
- Lens'in resmi uygulama ikonu: lime yeşili yuvarlatılmış zemin, koyu
  lacivert büyüteç, kırmızı/mercan tonlarında beş yapraklı çiçek (sarı-altın
  merkez, yazı/harf yok). **Kaynak notu:** Görev sırasında kullanıcı final
  tasarımı değiştirdi — ilk sunulan geometrik/mavi-beyaz örgülü varyant
  yerine bu çiçekli varyant final olarak seçildi; geometrik ara çıktılar
  hiçbir yere bağlanmadan silindi.
- `src/Lens.Desktop/Assets/lens-app-icon-master.png` — kullanıcının
  onayladığı (damalı sahte-şeffaflık içeren) kaynak PNG'den, gerçek RGBA
  alpha kanallı, kenar bandı temizlenmiş (gri/beyaz hale yok) ana tasarım
  kaynağı. Yalnızca geliştirme zamanı — derleme/publish çıktısına dahil
  değil.
- `src/Lens.Desktop/Assets/Lens.ico` — 9 çözünürlüklü (16/20/24/32/40/48/64/128/256 px)
  Windows ICO, her boyut teknik olarak doğrulanmış gerçek bitmap karesi.
- `Lens.Desktop.csproj`: `ApplicationIcon` (EXE Win32 ikonu) ve `Resource`
  (WPF pencere ikonu pack-URI'si) eklendi — tek fiziksel dosyadan.
- Tüm pencereler (`MainWindow`, `AlertWindow`, `ImagePreviewWindow`,
  `ProblemFilesWindow`, `SettingsWindow`) `Icon="/Assets/Lens.ico"` ile
  aynı ikonu kullanıyor.

### Notlar
- ClickOnce masaüstü kısayolu (bkz. `[ClickOnce Masaüstü Kısayolu]`
  girdisi) ve Başlat menüsü kaydı, ayrı bir ikon ayarı olmadan aynı EXE
  ikonunu miras alıyor. `setup.exe`/`Launcher.exe` için ayrı ikon
  atanmadı.
- ClickOnce paketi yeniden üretildi (temiz `bin`/`obj` + `/p:DebugType=none`);
  masaüstü kısayolu ayarı, model, `appsettings.json`, self-contained
  win-x64, güncelleme/sürüm davranışı DEĞİŞMEDİ.
- Taşınabilir publish akışı ayrıca doğrulandı, etkilenmedi.
- Pencere düzenine, temalara, arama davranışına dokunulmadı.
- Canlı arayüz/kurulum penceresi kullanıcı izni olmadan açılmadı.

## [Görünen Sürüm Tarihi — İki Haneli Yıl] — 2026-09-08

> **Durum: `feature/clickonce-deployment` branch'inde eklenmiştir; `main`'e
> merge beklemektedir.** Detay/gerekçe: `docs/DECISIONS.md` karar #86,
> `docs/DEPLOYMENT.md` §9.

### Değişti
- Sol alt köşede/Hakkında ekranında gösterilen sürüm metni
  `07.09.2026 — v1.0` → `08.09.26 — v1.0` (yıl dört haneliden iki haneliye).
  Tek değişen yer `Lens.Desktop.csproj` → `InformationalVersion`; görünen
  sürümün TEK okuma kaynağı (`AppVersionInfo.GetDisplayVersion()`)
  DEĞİŞMEDİ, XAML/C# içine sabit metin YAZILMADI. `v1.0` etiketi AYNEN
  korundu.
- `docs/DEPLOYMENT.md` §9: yeni sürüm çıkarma talimatı artık zorunlu biçimi
  açıkça `GG.AA.YY — vX.Y` (iki haneli yıl) olarak belirtiyor.
- `docs/CLICKONCE.md` §4: `InformationalVersion` (görünen metin) ile
  ClickOnce `ApplicationVersion` (4 parçalı sayısal sürüm) arasındaki
  bağımsızlık ayrıca vurgulandı.

### Notlar
- `AssemblyVersion`/`FileVersion` (`1.0.0.0`) ve ClickOnce
  `ApplicationVersion`/güncelleme davranışı bu turda DEĞİŞMEDİ.
- Tarih derleme zamanında otomatik hesaplanmıyor — yönetici her yeni
  sürümde elle güncelliyor (mevcut süreç, değişmedi).
- ClickOnce paketi yeniden üretildi; masaüstü kısayolu ayarı
  (`co.v1:createDesktopShortcut="true"`, önceki tur) ve yeni görünen sürüm
  metni birlikte doğrulandı.

## [ClickOnce Masaüstü Kısayolu] — 2026-09-08

> **Durum: `feature/clickonce-deployment` branch'inde eklenmiştir; `main`'e
> merge beklemektedir.** Detay/gerekçe: `docs/DECISIONS.md` karar #85,
> `docs/CLICKONCE.md` §2b.

### Eklendi
- `ClickOnce.pubxml`'e `<CreateDesktopShortcut>true</CreateDesktopShortcut>`
  eklendi — kurulum tamamlanınca kullanıcının masaüstünde otomatik `Lens`
  kısayolu oluşur (Başlat menüsü kaydı zaten var, değişmedi; görev çubuğuna
  sabitleme yapılmaz). Kısayol `Lens.Desktop.application` deployment
  manifestine işaret eder, doğrudan EXE'ye değil — sürüm güncellemelerinde
  geçerliliğini korur.

### Notlar
- Deployment manifestinde `co.v1:createDesktopShortcut="true"` özniteliği
  yerinde doğrulandı. Mevcut güncelleme/self-contained/model/sürüm/
  kullanıcı ayarları davranışları ve taşınabilir publish akışı DEĞİŞMEDİ.
- Canlı kurulum penceresi kullanıcı izni olmadan açılmadı.

## [ClickOnce Kurulum Hazırlığı] — 2026-09-08

> **Durum: `feature/clickonce-deployment` branch'inde (mevcut
> `codex/query-settings-layout-polish`'in ucundan dallanmıştır) eklenmiştir;
> `main`'e merge beklemektedir.** Bu girdi `main`'de yayınlanmış bir
> değişikliği ANLATMAZ. Detay/gerekçe: `docs/DECISIONS.md` karar #84,
> `docs/CLICKONCE.md`.

### Eklendi
- **ClickOnce publish profili** (`src/Lens.Desktop/Properties/PublishProfiles/ClickOnce.pubxml`,
  yeni): kullanıcı bazlı kurulum (`setup.exe`), self-contained win-x64
  (hedef makinede .NET kurulu olmasına gerek yok), Başlat menüsü kısayolu,
  çevrimdışı çalışma, açılışta otomatik güncelleme kontrolü
  (`UpdateEnabled=true`, `UpdateMode=Foreground`). ClickOnce'ın 4 parçalı
  `ApplicationVersion`'ı ayrı bir sayı DEĞİL, mevcut `FileVersion`'dan
  (`$(FileVersion)`) türetiliyor — tek sürüm kaynağı korunuyor.
  `PublisherName` açık bir placeholder (`[PLACEHOLDER] Yayıncı Adı
  Belirlenmedi`); `InstallUrl`/`UpdateUrl` gerçek adres bilinmediği için
  bilerek boş (yerel/offline staging publish); `SignManifests=false`
  (gerçek sertifika yok, sahte sertifika oluşturulmadı).
- Yeni dokümantasyon: `docs/CLICKONCE.md` — ilk sürüm oluşturma, gerçek
  yayın/güncelleme adresini tanımlama, sürüm artırma, yeni sürüm yayımlama,
  imzalama/sertifika gereksinimi, model/`appsettings.json` paket
  doğrulaması, temiz makinede kurulum+güncelleme kontrol listesi, kaldırma/
  rollback davranışı, açık kararlar.

### Değişti
- `src/Lens.Desktop/Lens.Desktop.csproj`: model (`models/clip-vision-b16-openai.onnx`)
  ve `appsettings.json` öğe tipi `None`'dan `Content`'e taşındı — Visual
  Studio'nun klasik ClickOnce hedeflerinin yalnızca `Content` öğelerini
  manifest'e koşulsuz dahil ettiği yerinde test edilerek keşfedildi. Normal
  `dotnet publish`/mevcut taşınabilir self-contained publish davranışını
  ETKİLEMEDİ (yerinde doğrulandı).
- `docs/DEPLOYMENT.md`, `docs/PRODUCTION_CHECKLIST.md`, `docs/DECISIONS.md`:
  ClickOnce'a çapraz referanslar eklendi; "Installer/MSI" bilinçli-ertelenen
  maddesi ClickOnce ile SUPERSEDED olarak işaretlendi (üretim hâlâ
  tamamlanmamış — bkz. açık kararlar).

### Notlar
- Mevcut taşınabilir publish akışı (`docs/DEPLOYMENT.md` §1) **değişmedi**,
  bu turda ayrıca yeniden doğrulandı.
- ClickOnce publish'i `dotnet publish`/`dotnet msbuild` (.NET Core MSBuild)
  ile ÇALIŞMIYOR (`MSB4803` hatası) — tam .NET Framework MSBuild
  (Visual Studio veya Build Tools) gerekiyor, bkz. `docs/CLICKONCE.md` §1.
- Canlı kurulum/güncelleme penceresi bu turda kullanıcı izni olmadan
  açılmadı — yalnızca komut satırından publish + paket içeriği doğrulandı.

## [DENEY - Arama Paneli Görsel Polish] — 2026-09-08

> **Durum: `codex/query-settings-layout-polish` branch'inde (önceki
> `codex/query-settings-layout-experiment` deneyi TEMEL ALINARAK) deneysel
> olarak uygulanmıştır; canlı görsel kabul ve `main`'e merge beklemektedir.**
> Bu girdi `main`'de yayınlanmış bir değişikliği ANLATMAZ.

### Değişti (deney branch'i - main'e henüz yansımadı)
- **ARAMA AYARLARI paneli renkleri tema başına SABİT hale geldi:** Önceki
  turda panel yüzeyi ana pencere renginden (`BlendToward` ile siyaha %12
  harmanlanarak) türetiliyordu - Lime temada kirli sarı-yeşil bir sonuç
  veriyordu. Artık `MainWindow.GetSettingsPanelColors` her tema için ayrı,
  sabit bir yüzey/yazı/kenarlık seti döndürüyor (Açık/Lime/Normal beyaza
  yakın, Açık Sepya sıcak krem, Koyu/Koyu Sepya ana zeminden ayrışan koyu
  yüzey). Dört yeni kaynak: `SettingsPanelBackgroundBrush`,
  `SettingsPanelForegroundBrush`, `SettingsPanelSecondaryTextBrush`,
  `SettingsPanelBorderBrush`. `AppTheme.cs`/`ThemePalette`'e DOKUNULMADI.
- **Gerçek artırma/azaltma düğmeli sayısal girişler:** `ThresholdTextBox`/
  `MaxResultsTextBox` artık sağlarında iki `RepeatButton` (▲/▼) bulunan bir
  "NumberBox" görünümünde (TextBox + RepeatButton kompozisyonu, yeni NuGet
  paketi YOK). `StepThreshold`/`StepMaxResults` mevcut
  `SimilarityThreshold`/`MaxResultsPreference` sözleşmesini (0-100/1-200,
  virgül/nokta, boş→80/20 varsayılan) kullanır; `NumericInputFilter`
  değiştirilmedi. Yukarı/Aşağı ok tuşları da aynı adımı uygular
  (`NumericTextBox_PreviewKeyDown`).
- **Sorgu boş-durum yazıları büyütüldü:** "Sorgu görselini seçin" 15→18-22
  DIP (responsive), "Tıklayın veya buraya sürükleyin" 11→12-13 DIP;
  opacity 0.55→0.64/0.62.
- **Panel iç boşlukları ferahlatıldı:** Padding 14,10,14,12 → 16 (tüm
  kenarlar); satır araları 8/8/10 → 13/12/14 DIP.
- **Görsel-panel mesafesi (dış boşluk) ve orta sütun genişliği artık
  DOĞRUDAN hedeflenir:** Eski `settingsColumnWidth + gapInner + buttonWidth`
  formülü (dar ~360, geniş ~412 DIP, gereğinden uzun butonlar) kaldırıldı;
  yerine orta sütun için doğrudan 280 (dar) → 320 (geniş) DIP hedefi,
  dış boşluk için 24 (dar) → 50 (geniş) DIP hedefi kullanılıyor.
- **Yatay taşma güvenliği eklendi:** Her `UpdateResponsiveLayout` turunda
  toplam orta grup genişliği `RootGrid.ActualWidth` ile karşılaştırılıyor;
  aşarsa sırayla dış boşluk → orta sütun → görsel (4:3 korunarak, artık
  240 değil 200 DIP'e kadar) küçültülüyor. Önceki turdaki "sonuç alanına
  her zaman 120 DIP ayrılıyor" iddiası, görsel genişliğinin 240'ın altına
  inememesi nedeniyle gerçek anlamda garanti edilmiyordu - düzeltildi.
- Değiştirilmeyenler (görev kapsamı dışı bırakıldı): 4:3 görsel kararı,
  eşit görsel boyutu, tıklama/sürükle-bırak/çift tık, Ara/Yeni Arama
  işlevleri, busy kilidi, benzerlik/eşik/sıralama mantığı, 200 üst sınırı,
  %80/20 varsayılanları, index/model, üst klasör satırı, sonuç kartları,
  kaydırma kuralları, sürüm sistemi, tema adları/ana tema renkleri.

## [DENEY - Sorgu/Arama Ayarları Yerleşimi] — 2026-09-08

> **Durum: Deney branch'inde (`codex/query-settings-layout-experiment`)
> uygulanmıştır; canlı görsel kabul ve `main`'e merge beklemektedir.** Bu
> girdi `main`'de yayınlanmış bir değişikliği ANLATMAZ - yalnızca deney
> branch'indeki değişikliğin kaydıdır. Kullanıcı canlı kabul verip merge
> onaylamadan bu girdi kalıcı/nihai sayılmaz.

### Değişti (deney branch'i - main'e henüz yansımadı)
- **"Sorgu Görseli Seç" butonu kaldırıldı:** Orta sütunun üst kısmına artık
  doğrudan `Ara`/`Yeni Arama` yerleşiyor (eşit genişlikte, yan yana). Boş
  sorgu çerçevesinin kendisi tıklanabilir/klavye-erişilebilir hale geldi
  (tek tık/Enter/Space ile dosya seçme, `MainWindow.OpenQuerySelectDialog`
  - eski `SelectQueryButton_Click` mantığının yeniden adlandırılmış hali,
  kopyalanmadı).
- **Sorgu çerçevesinde boş-durum yönlendirmesi:** "Sorgu görselini seçin" /
  "Tıklayın veya buraya sürükleyin" iki satırlık, yarı saydam, tıklamayı
  engellemeyen (`IsHitTestVisible=False`) bir watermark katmanı eklendi.
  Çerçeve altındaki kalıcı "Görsel seçin veya buraya sürükleyin • çift tık:
  büyüt" bilgisi KORUNDU (kullanıcı talebi - "tekrar" gerekçesiyle
  kaldırılmadı).
- **Seçilen Sonuç çerçevesinde de benzer bir "Henüz sonuç seçilmedi"
  placeholder'ı eklendi.**
- **Sorgu/sonuç görsel çerçeveleri kareden (300×300) 4:3 yatay dikdörtgene
  geçti** (geniş pencere hedefi ~320×240 DIP, dar pencerede ~240×180 DIP,
  oran her zaman 4:3). `MainWindow.UpdateResponsiveLayout` artık yalnızca
  pencere genişliğine değil, kullanılabilir yüksekliğe de bakıyor.
- **"ARAMA AYARLARI" paneli:** Minimum benzerlik / en fazla sonuç / otomatik
  indeksleme kutucuğu artık tema-türetilmiş (ana pencere renginden
  hesaplanan, `AppTheme.cs`/tema paletlerine DOKUNULMADAN) ince kenarlıklı
  bir panel içinde toplandı.
- **Sayısal giriş kutuları (`ThresholdTextBox`/`MaxResultsTextBox`)
  daraltıldı:** 84 DIP → 56 DIP (sabit, pencere genişliğiyle artık
  değişmiyor). `NumericInputFilter` DEĞİŞTİRİLMEDİ.
- **Sonuç alanına daha fazla dikey alan:** Orta blok üst/alt margin'i
  (32/20 → 20/12) ve görsellerin kare→4:3 geçişiyle kazanılan yükseklik
  sayesinde `EN BENZER SONUÇLAR` alanı normal pencerede gözle görülür
  biçimde daha fazla dikey alan kullanıyor.
- Değiştirilmeyen alanlar (bilinçli olarak dokunulmadı): CLIP modeli,
  embedding/benzerlik/eşik mantığı, sonuç sıralaması, 200 üst sınırı,
  %80/20 varsayılanları, index formatı, otomatik index mantığı, kullanıcı
  ayar dosyası, klasör seçme/varsayılan klasör davranışları, üst klasör
  satırı yerleşimi, tema paletleri, alt bilgi sürüm sistemi, sonuç
  kartlarının veri modeli, busy kilidi, arama sonrası kaydırma kuralları.

## [Küçük Arayüz Düzeltmeleri ve 3-Rakam Sınırı] — 2026-09-07

### Değişti
- **"Lime (Deneme)" → "Lime":** Tema alt menüsündeki etiket sadeleştirildi
  (`MainWindow.xaml` `MenuItem.Header` ve tutarlılık için `AppTheme.
  ThemePalette.DisplayName` eşlemesi - ikincisi şu an hiçbir yerden
  çağrılmıyor ama ileride kullanılırsa aynı metni versin diye güncellendi).
  Tema rengi/paleti/davranışı DEĞİŞMEDİ.
- **"Ayarlar" → "Bilgilendirme":** ⋮ menüsündeki menü öğesi ve açılan
  penceresinin başlığı (`Title`) kullanıcıya görünen her iki yerde de
  tutarlı şekilde güncellendi. `SettingsWindow` sınıf/dosya adı ve
  `SettingsMenuItem_Click` metot adı BİLEREK değiştirilmedi (talimat: kod
  tarafı zorunlu değil, gereksiz yeniden adlandırma yapılmadı).
- **Bilgilendirme ekranına "Versiyonlama" geliştirici notu:** Varsayılan
  kapalı "Teknik ayrıntılar" bölümüne, sürümün nereden geldiğini
  (`Lens.Desktop.csproj` → `AssemblyVersion`/`FileVersion`/
  `InformationalVersion`) ve önerilen `MAJOR.MINOR.PATCH` artırma kuralını
  (MAJOR: geriye dönük uyumsuz büyük değişiklik, MINOR: geriye uyumlu yeni
  özellik, PATCH: geriye uyumlu hata düzeltmesi) kısaca anlatan, yalnızca
  geliştiricilere yönelik bir paragraf eklendi; yayın öncesi sürüm
  güncelleyip yeniden derlemenin gerekliliği vurgulanıyor. Salt bilgi -
  hiçbir komut/eylem içermiyor.
- **Sayısal alanlarda en fazla 3 rakam:** "Minimum benzerlik (%)" ve "En
  fazla sonuç" alanlarına artık en fazla 3 rakam (ondalık ayırıcı hariç,
  ayırıcının iki tarafı BİRLİKTE sayılır - ör. "80,55" 4 rakam olduğu için
  reddedilir) girilebiliyor. `Lens.Core.Search.NumericInputFilter`'a yeni
  `MaxDigitCount=3` sabiti eklendi; hem klavye/üzerine-yazma/yapıştırma
  reddi (`IsValidPartialText`) hem son güvenlik ağı (`StripInvalidCharacters`,
  yalnızca IME/beklenmedik giriş yolu için) bu sınırı uyguluyor. 4+ rakamlı
  bir yapıştırma **kesilip kısaltılmıyor, tamamen reddediliyor** ("1000"
  yapıştırılırsa hiçbir şey uygulanmaz). "201"/"9999" gibi 3 rakamlı bir
  değer hâlâ YAZILABİLİYOR (karakter olarak geçerli) - aralık dışı olduğu
  "Ara"ya basıldığında mevcut "En fazla 200 sonuç listeleyebilirsiniz."
  uyarısıyla ayrıca bildiriliyor, bu davranış DEĞİŞMEDİ.
- Benzerlik algoritması, arama/indeksleme mantığı, 80/20 varsayılanları,
  tema renk paleti, orta form/üst satır hizası ve diğer tüm onaylı tasarım
  DEĞİŞMEDİ - bu tur yalnızca yukarıdaki dört küçük, belirtilen alana
  müdahale etti.

### Test
- `dotnet build Lens.sln -c Release`: **0 warning / 0 error** (Debug,
  kullanıcının açık `Lens.Desktop.exe`'si tarafından kilitliydi -
  müdahale edilmedi, Release ayrı çıktı yoluyla temiz derlendi).
- `Lens.AiProof hardeningtest`: **204/204 PASS** (önceki 192 + Grup M'e
  eklenen 12 yeni kontrol: M35-M46, 3-rakam sınırının klavye/üzerine-yazma/
  yapıştırma/son-temizleme davranışı).
- Canlı görsel doğrulama YAPILMADI (uygulama açılmadı) - kullanıcının kendi
  ekranında Lime etiketi, Bilgilendirme başlığı/notu ve 3-rakam sınırının
  kontrolü bekleniyor. Publish paketi bu kayıtla GÜNCELLENMEDİ. Detay:
  `docs/DECISIONS.md` #81.

## [Assembly Sürüm Gösterimi ve Sayısal Giriş Sınırlaması] — 2026-09-07

### Değişti
- **Sürüm bilgisi (sol alt köşe + Hakkında):** Ana pencerenin sol alt
  köşesinde `Sürüm: 07.09.2026 — v1.0` biçiminde bir sürüm etiketi eklendi.
  Bu metin XAML/C# içinde SABİT bir string DEĞİL — tek kaynağı derlenen
  `Lens.Desktop.exe`'nin Assembly metadata'sı (`AssemblyInformationalVersion`,
  `Lens.Desktop.csproj`'daki `InformationalVersion` MSBuild özelliğinden SDK
  tarafından otomatik üretilir). Yeni ortak `Lens.Desktop.AppVersionInfo.
  GetDisplayVersion()`, hem alt bilgi satırını hem de Hakkında ekranını
  besler — ikisi ARTIK AYNI kaynaktan okur, farklı sürüm gösteremezler (eski
  Hakkında ekranı yalnızca sayısal `AssemblyVersion`/`GetName().Version`
  kullanıyordu, bu ayrım kaldırıldı). Assembly metadata okunamazsa/boşsa
  uygulama ÇÖKMEZ — güvenli bir yedek metne düşer. `AssemblyVersion`/
  `FileVersion` standart sayısal `1.0.0.0` biçiminde ayrı tutuldu (duplicate
  assembly attribute riski yok — `AssemblyInfo.cs`'deki mevcut WPF
  `ThemeInfo` tanımına dokunulmadı). `IncludeSourceRevisionInInformationalVersion=
  false` ile git commit hash'inin sürüm metnine otomatik eklenmesi engellendi.
  Alt bilgi satırı artık 2 sütunlu responsive bir `Grid` (sol: sürüm, sağ:
  mevcut "Sistem kesin eşleşme belirtmez…" açıklaması, değişmeden) - 860 DIP
  minimum genişlikte metinler kesilmez/üst üste binmez (sağ metne savunma
  amaçlı `TextWrapping="Wrap"` eklendi). Sürüm güncelleme süreci
  `docs/DEPLOYMENT.md` §9'da belgelendi.
- **Sayısal giriş sınırlaması ("Minimum benzerlik (%)" / "En fazla sonuç"):**
  Bu iki alana artık harf/geçersiz karakter ne yazılabiliyor ne de
  yapıştırılabiliyor. Yeni, WPF'ye bağımlı OLMAYAN `Lens.Core.Search.
  NumericInputFilter` (test edilebilir, bkz. Test) karakter düzeyinde karar
  verir; `MainWindow`'daki üç paylaşılan olay işleyicisi (`PreviewTextInput`,
  `DataObject.Pasting`, `TextChanged` son güvenlik ağı) bu mantığı her iki
  alana da uygular — kod tekrarı yok. Minimum benzerlik alanı TR virgülü
  (`80,5`) ve İngilizce nokta (`80.5`) ile ondalık kabul eder; en fazla
  sonuç alanı YALNIZCA tam sayı kabul eder (ayırıcı dahi reddedilir). Her
  iki alanda da eksi işareti ve harf HER ZAMAN reddedilir, birden fazla
  ondalık ayırıcı reddedilir. **Aralık/varsayılan doğrulaması
  (`SimilarityThreshold`/`MaxResultsPreference`, 0-100 / 1-200, boş
  girdide 80/20 varsayılanı, "200'ü aşan" uyarı mesajı) HİÇ DEĞİŞMEDİ** —
  yeni filtre yalnızca karakter düzeyinde çalışır, aralık dışı ama
  karakter-olarak-geçerli bir sayı (ör. "101", "201") buradan geçer, asıl
  doğrulama/uyarı katmanında olduğu gibi ele alınmaya devam eder. Backspace/
  Delete/yön tuşları/Tab/Ctrl+A/Ctrl+C/Ctrl+V/seçili metnin üzerine yazma
  etkilenmedi (bunlar zaten `PreviewTextInput`'a hiç girmiyor ya da
  `DataObject.Pasting` üzerinden ayrıca ele alınıyor). Mevcut 860 DIP
  minimum genişlikte alan boyutları/hizası DEĞİŞMEDİ (spinner/ok eklenmedi).
- Benzerlik algoritması, sıralama, index formatı/konumu, otomatik index
  güncelleme, 200 sonuç üst sınırı, 80/20 varsayılanları, busy/kilit
  davranışı, sürükle-bırak, Yeni Arama, kaydırma sıfırlama, tema sistemi,
  sonuç kartları, üst klasör satırı ve orta form hizası DEĞİŞMEDİ.

### Test
- `dotnet build Lens.sln -c Debug`/`-c Release`: **0 warning / 0 error**
  (her ikisi de).
- `Lens.AiProof hardeningtest`: **192/192 PASS** (önceki 158 + yeni Grup M:
  `NumericInputFilter` karakter-düzeyi doğrulaması, 34 kontrol - boş/geçerli/
  harf/eksi-işareti/çoklu-ayırıcı/ekleme-yapıştırma senaryoları/son-temizleme).
- Assembly metadata reflection ile doğrulandı (derlenen DLL'in Win32
  `ProductVersion`/`FileVersion` kaynaklarından): `InformationalVersion`
  em-tire (—, U+2014) dahil BİREBİR korunuyor, `FileVersion`/`AssemblyVersion`
  `1.0.0.0`.
- Canlı görsel doğrulama YAPILMADI (uygulama açılmadı) — kullanıcının kendi
  ekranında sol alt köşe/Hakkında ekranı/sayısal alan davranışını
  doğrulaması bekleniyor. Publish paketi bu kayıtla GÜNCELLENMEDİ (bilinçli -
  görsel kabul öncesi). Detay: `docs/DECISIONS.md` #80, `docs/DEPLOYMENT.md` §9.

## [Ekran Uyumu — Pencere Ölçüsünün Çalışma Alanına Sığdırılması] — 2026-09-07

### Analiz
- Görsel tasarım DEĞİŞTİRİLMEDEN, farklı ekran çözünürlüğü/Windows
  ölçeklendirme kombinasyonlarında (1920×1080 %100/%125/%150, 1366×768
  %100/%125/%150) sabit `Window.MinHeight="680"`/`MinWidth="860"` ve
  başlangıç `Height="840"`/`Width="1060"` değerlerinin gerçek çalışma
  alanına (görev çubuğu hariç, DIP cinsinden) sığıp sığmadığı hesaplandı.
- **Tespit:** 1366×768 ekranda **%125** (~1093×574 DIP çalışma alanı) ve
  **%150**'de (~911×472 DIP) sabit `MinHeight=680` çalışma alanından
  **büyük** kalıyordu — WPF interaktif yeniden boyutlandırmada
  `MinHeight`/`MinWidth` altına asla izin vermediğinden kullanıcı
  pencereyi hiçbir şekilde küçültüp ekrana sığdıramıyor, alt kısım
  (sonuçlar/alt bilgi) kalıcı olarak erişilemez kalıyordu. Ayrıca
  1366×768 %100'de ve 1920×1080 %150'de varsayılan başlangıç yüksekliği
  (840 DIP) çalışma alanını (sırasıyla ~728 ve ~680 DIP) aşıyordu — ilk
  açılışta pencerenin alt kenarı ekran dışında/görev çubuğunun altında
  kalabiliyordu (kullanıcı manuel küçültmeden fark etmeyebilir).
  1920×1080 %100/%125 SAFE (bolca pay var).
- **Karar:** Bu ölçülebilir taşma riski, görevin "düzeltme gerekli"
  ölçütünü (`%125`/`%150`'de pencerenin altına erişilemiyor +
  `MinHeight` çalışma alanından büyük kalıyor) açıkça karşıladığı için
  küçük bir düzeltme uygulandı.

### Değişti
- Yeni `MainWindow.ClampWindowToWorkArea()`, constructor'da
  `InitializeComponent()`'ten hemen sonra çağrılır: `SystemParameters.
  WorkArea` (başlangıç anındaki mevcut ekranın DIP cinsinden çalışma
  alanı) gerçek `MinWidth`/`MinHeight`/`Width`/`Height`'ten KÜÇÜKSE
  bunları `Math.Min`/`Math.Clamp` ile YALNIZCA gerektiği kadar küçültür
  — hiçbir zaman BÜYÜTMEZ. Normal/geniş ekranlarda (çalışma alanı zaten
  860×680'den büyük) hiçbir şey değişmez, onaylanmış 860×680 minimum /
  1060×840 başlangıç ölçüsü BİREBİR korunur.
- `WindowStartupLocation="CenterScreen"` eklendi — küçültülen pencere
  ekranın rastgele bir köşesinde değil, ortalanmış açılır.
- `UpdateResponsiveLayout`, orta bölüm görsel/sütun ölçüleri, üst satır
  yerleşimi, tema sistemi, arama/indeksleme mantığı ve tüm onaylı
  tasarım DEĞİŞMEDİ — bu düzeltme yalnızca pencere geometrisinin
  başlangıç değerlerine dokunur, `UpdateResponsiveLayout` zaten yalnızca
  `RootGrid.ActualWidth`'e göre çalıştığından pencere ne kadar
  küçültülürse küçültülsün aynı şekilde tepki vermeye devam eder.
- DPI-awareness manifestosu (PerMonitorV2 vb.) BİLEREK EKLENMEDİ —
  incelenen 6 senaryonun tümü "tek ekranda başlangıç" durumudur;
  varsayılan (manifestosuz) WPF davranışı bu senaryolarda zaten doğru
  DIP hesaplaması yapıyor, yalnızca "uygulama açıkken farklı DPI'lı bir
  monitöre sürükleme" durumunda (kapsam dışı) bulanıklaşma riski taşır.

### Test
- `dotnet build Lens.sln -c Debug`/`-c Release`: **0 warning / 0 error**
  (her ikisi de).
- `Lens.AiProof hardeningtest`: **158/158 PASS** (bu değişiklik arama/
  indeksleme/doğrulama mantığına dokunmadığı için beklenen sonuç).
- Canlı ekran testi YAPILMADI (uygulama açılmadı) — hesap kaynak kod
  üzerinden `SystemParameters.WorkArea` mantığı ve DIP aritmetiğiyle
  yapıldı; kullanıcının gerçek 1366×768/1920×1080 donanımda görsel
  doğrulaması önerilir. Publish/ZIP/Drive paketi bu kayıtla
  GÜNCELLENMEDİ. Detay: `docs/DECISIONS.md` #79.

## [Arama Formu ve Üst Klasör Satırı Hizaları] — 2026-09-07

### Değişti
- **Orta arama formu:** Minimum benzerlik ve en fazla sonuç satırlarındaki
  iki bağımsız yatay `StackPanel` kaldırıldı. İki etiket artık ortak bir
  etiket sütununu paylaşıyor; `ThresholdTextBox`/`MaxResultsTextBox` aynı
  84 DIP giriş sütununda (`SettingsInputColumn`), `SearchButton`/
  `NewSearchButton` aynı işlem sütununda — sol/sağ kenarlar ve genişlikler
  artık tahmini `Margin` değil, ortak Grid sütun sınırıyla garanti hizalı.
  "Sorgu Görseli Seç" butonu ve otomatik indeksleme kutusu aynı ayar
  alanını (etiket+giriş sütunları toplamı) kaplıyor. Mevcut responsive
  genişlik hesabı (`MainWindow.UpdateResponsiveLayout`) yeni sütun
  yapısına uyarlandı, ferah dikey boşluklar korundu.
- **Üst klasör satırı:** `TopAreaGrid` 7 sütundan 8 sütuna çıkarıldı;
  `MenuButton` son sütuna alındı ve menüden önce gerçek, bağımsız bir
  `MenuSpacerColumn Width="*"` eklendi — "⋮" menüsü artık varsayılan
  klasör buton grubundan (Bu Klasörü Varsayılan Yap / Varsayılanı
  Temizle) ayrı, pencerenin en sağında sabit kalıyor; pencere
  genişledikçe sağ kenardan uzaklaşmıyor. `SetDefaultButton` ile
  `ProblemFilesButton` aynı sütunda/aynı sol kenar hizasında kalmaya
  devam ediyor; `ClearDefaultButton` kendi `Auto` sütununda. Klasör yolu
  kutusunun (`FolderPathTextBox`) eski sabit `MaxWidth="360"` sınırı
  kaldırıldı — kutu artık pencere genişliğine göre dar görünümde 180
  DIP'ten başlayıp geniş görünümde yaklaşık 530 DIP'e kadar responsive
  büyüyor (XAML güvenlik sınırları: `MinWidth=180`, `MaxWidth=560`).
- **Responsive sınır düzeltmeleri (860×680 minimum genişlik taşma riski):**
  `UpdateResponsiveLayout` içinde aynı çağrıda yeni `FolderPathColumn.Width`
  atandıktan hemen sonra eski (bir önceki layout turuna ait)
  `FolderPathColumn.ActualWidth` okunması düzeltildi — `leftContentWidth`
  artık aynı turda hesaplanan, kolonun gerçek `MinWidth`/`MaxWidth`
  sınırlarına göre clamp edilmiş `folderPathWidth` değerini kullanıyor;
  bu, yeniden boyutlandırmada varsayılan buton grubunun bir kare geriden
  "sıçramasını" önlüyor. Dar hedef `FolderPathColumn` için 200'den 180
  DIP'e (kolonun gerçek tabanı) indirildi ve yeni `ProductInfoPanel`
  (ürün sayısı/kaynak metni `WrapPanel`'i) adlandırılıp responsive bir
  `MaxWidth` aldı (dar görünümde ~160 DIP — gerçek metinler
  `FormattedText` ile ölçülerek bu sınırın altında kaldığı doğrulandı,
  `DirectorySourceText` gerekirse alt satıra sarar; geniş görünümde ~600
  DIP, pratikte sınırsız, mevcut yan-yana görünüm korunur). Bu iki
  düzeltme, 860 DIP minimum pencerede `Varsayılanı Temizle` görünürken
  üst satırın `MenuButton`'u dışarı itme riskini ölçülebilir şekilde
  (~ölçülen 118 DIP taşmadan ~35-45 DIP güvenlik payına) azalttı.
- Arama/indeksleme mantığı, tema sistemi ve 80/20 varsayılanları
  DEĞİŞMEDİ — bu tur yalnızca `MainWindow.xaml`/`MainWindow.xaml.cs`
  içindeki yerleşimle sınırlı.

### Test
- `dotnet build src/Lens.Desktop/Lens.Desktop.csproj -c Release`:
  **0 warning / 0 error**.
- Canlı görsel doğrulama bu turda YAPILMADI (uygulama açılmadı, kullanıcı
  ekranına müdahale edilmedi) — kullanıcının kendi ekranında onayı
  bekleniyor, özellikle 860×680 minimum pencerede `Varsayılanı Temizle`
  görünürken üst satırda kesilme olmadığı. Publish/ZIP/Drive paketi bu
  kayıtla GÜNCELLENMEDİ. Detay: `docs/DECISIONS.md` #78.

## [Kaynak Kod Kontrol Noktası — Yerleşim, Ayarlar ve 80/20] — 2026-09-04

### Değişti
- Kullanıcının sonraki açık talimatıyla, `d4866f1` dokümantasyon kaydında
  anlatılan kaynak değişiklikleri de commit kapsamına alındı: ana pencere
  yerleşimi, yeni Ayarlar penceresi, düzenlenebilir klasör adresi, 80/20
  varsayılanları ve bunların testleri (kararlar #75-77).
- Claude'un mevcut kaynak kodu değiştirilmeden kaydedildi. Aşağıdaki
  önceki turlardaki "kaynak commit'i bekliyor / yalnızca doküman" ifadeleri
  o kontrol noktasının tarihsel durumudur; bu kaynak kaydıyla güncellendi.

### Test
- Bu kaynak üzerinde yeniden `dotnet build Lens.sln -c Release --no-restore`
  çalıştırıldı: **0 warning / 0 error**.
- `Lens.AiProof hardeningtest` yeniden çalıştırıldı: **158 PASS / 0 FAIL**.
  İlk deneme test önbelleğine yazma izni nedeniyle sandbox'ta durdu;
  gerekli erişimle tekrar çalıştırıldığında tamamlandı.
- Debug uygulaması açık olduğu için kapatılmadı ve Debug yeniden
  derlenmedi; canlı UI/görsel kabul yapılmadı. #75'te kayıtlı ertelenen
  sorunlar çözülmüş sayılmaz. Yeni publish/ZIP/Drive yüklemesi yok.

## [Onaylı Taslağa Göre Görsel Yerleşim Düzeltmesi] — 2026-09-04

**Durum:** Claude'un kaynak kod düzenlemesi tamamlandı raporu; görsel
onay, Debug doğrulaması ve kaynak commit/publish bekliyor. Bu doküman
commit'ine uygulama kaynakları dahil değildir.

### Değişti
- **Orta blok tek dengeli grup:** Sorgu görseli → arama ayarları → Ara/Yeni
  Arama → seçilen sonuç artık tek bir grup olarak pencerede ortalanıyor.
  Arama ayarları ve işlem butonları artık İKİ bağımsız sütun/StackPanel
  değil, ortak satırlara sahip TEK bir alt `Grid` (`SettingsButtonsGrid`) —
  "Ara" ile "Minimum benzerlik (%)" satırı, "Yeni Arama" ile "En fazla
  sonuç" satırı AYNI paylaşılan Grid satırında, dikey merkezleri pencere/
  yazı tipi/doğrulama mesajı durumundan bağımsız olarak hizalı kalıyor
  (tahmini `Margin` değil).
- **İki karşılaştırma görseli eşit kare oldu** (geniş pencere hedefi
  300×300, önceki turdaki 260 yerine) — `Stretch="Uniform"` korunuyor,
  kırpma/esnetme yok. Boyut artık sabit değil; `MainWindow.
  UpdateResponsiveLayout` pencere genişliğine göre (200–300 DIP arası) tek
  bir hesapla ayarlıyor — bir `RenderTransform`/`Viewbox` KULLANILMADI ki
  sürükle-bırak ve çift-tık büyütme davranışı etkilenmesin.
- **Ayar sütunu artık ferah:** "Sorgu Görseli Seç" üstten (~28 DIP payla)
  başlıyor, eşik/sonuç-limiti satırları arasında ~20 DIP, ikinci satır ile
  otomatik indeks kutucuğu arasında ~24 DIP boşluk var. Sayısal kutular
  ~84×36 DIP, sol/sağ kenarları hizalı. Doğrulama uyarıları (Threshold/
  MaxResultsValidationText) gizliyken sıfır yer kaplıyor, görünürken
  yalnızca KENDİ satırından sonrasını aşağı itiyor — "Ara" hiçbir zaman
  yanlış satırla hizalanmıyor.
- **"Ürün Klasörü Seç" → "Tarama Klasörünü Seç"** (buton metni, dialog
  başlığı ve Ayarlar'daki doğrudan atıf). Kontrolün adı/olay işleyicisi ve
  işlevi değişmedi; ilgisiz "ürün" ifadeleri (genel uyarı mesajları vb.)
  toplu değiştirilmedi.
- **Üst iki satır artık TEK bir Grid** (`TopAreaGrid`) — "Bu Klasörü
  Varsayılan Yap" ve "Sorunlu / Atlanan Dosyalar" ARTIK ortak bir sütunu
  paylaşıyor, bu yüzden sol kenarları kesin olarak aynı (iki bağımsız
  Grid'de "ikisi de sağda" yaklaşımıyla garanti edilemeyecek bir şart).
  Varsayılan buton grubunun sol başlangıcı, karşılaştırma satırındaki sağ
  görselin sol kenarı civarında tutuluyor (ölçülen bir boşluk sütunu ile,
  pencerenin ham sağ köşesine yapışmıyor).
- **Dar pencerede birlikte uyarlanan tek bir hesap:** gap'ler (40→16 DIP),
  görsel boyutu (300→200 DIP), ayar sütunu (260→240 DIP) ve buton genişliği
  (120→104 DIP) AYNI pencere-genişliği oranına göre lineer değişiyor —
  hedef 860×680 minimuma sığması ve 1060×840 başlangıçta ferah görünmesi;
  bu iki boyutta canlı kabul henüz yapılmadı.
  Giriş yükseklikleri (36/42/34 DIP) sabit kalıyor, daraldıkça tekrar ince
  kutulara dönüşmüyor. Sonuç listesi kartları/küçük resimler/kart seçim
  stilleri değişmedi.

### Not
Bu tur yalnızca kod/yerleşim düzenlemesidir — canlı ekranda görsel
doğrulama yapılmadı (kullanıcı bilgisayarı aktif kullanıyordu, uygulama
açılmadı). Ertelenmiş konular (Ayarlar'daki "Teknik ayrıntılar", adres
normalizasyonu, klasör geçişindeki durum koruma) bu turda ELE ALINMADI.

### Test
Claude raporu: `dotnet build` Release **0 warning/0 error** (Debug, kullanıcının açık
uygulaması tarafından kilitliydi, tamamlanamadı). `Lens.AiProof
hardeningtest` **158/158 PASS** (bu tur Lens.Core'a dokunmadı, etkilenmedi
— yalnızca WPF yerleşimi/görsel doğrulama gerektirir, headless test
kapsamı dışındadır). Detay: `docs/DECISIONS.md` #77.

## [Dokümantasyon Kontrol Noktası — Tasarım Görsel Onayda] — 2026-09-04

### Eklendi
- `docs/PROJECT_CONTEXT.md` başına güncel devir notu: Git'teki son uygulama
  değişikliği (`c1f15ab`), henüz kaynak commit'i yapılmamış işler, son
  tasarım hedefleri ve kullanıcı kararıyla ertelenen sorunlar ayrıldı.
- Karar #77: büyük/eşit kare görseller, ferah ve birlikte ortalanmış orta
  grup, ortak satır/sütun hizaları, "Tarama Klasörünü Seç" adı ve dar
  pencere uyumu. Bunlar onaylı hedef; canlı kabul henüz tamamlanmadı.

### Düzeltildi
- Minimum benzerlik varsayılanı hakkındaki eski açık soru #76 ile kapatıldı.
  #75'in önceki kayıtlarında kesin ifade edilen adres/yükleme/kopyalama
  davranışları, mevcut doğrulamanın sınırlarına göre düzeltildi.

### Not
- **Bu tur yalnızca doküman commit/push işlemidir.** Bu tarihteki 80/20,
  Ayarlar/adres ve iki yerleşim çalışması çalışma kopyasındaki ilerlemeyi
  anlatır; uygulama kaynakları bu commit'e dahil değildir. Claude'un yeni
  tasarım çalışması yedeklenmiş veya yayımlanmış sayılmaz.
- İlk yerleşim turunda genel karşılaştırma başlığı kaldırıldı, adres
  kutusu kısaltıldı, sorgu/ayarlar/eylemler/sonuç birlikte ortalandı ve
  görseller 160-260 DIP kare olacak şekilde düzenlendi. Başarılı tarama
  yokken istatistik satırını gizleyen mevcut düzeltme korundu. Kullanıcı
  bu yerleşimi görsel hedefe yeterince yakın bulmadığı için #77 turu yapıldı.
  Bu kayıt hazırlanırken Claude kod düzenlemesini bitirdiğini raporladı;
  görsel kabul hâlâ bekliyor.
- Aşağıdaki **158/158 PASS / Debug-Release 0 warning-0 error** bilgisi,
  Claude'un ikinci tasarım düzenlemesinden önceki test raporudur; bu
  dokümantasyon turunda uygulama testleri yeniden çalıştırılmadı. Son
  tasarımın Release/158 test raporu yukarıda ayrıca kaydedildi; o turdaki
  Debug doğrulaması ve kullanıcı görsel kontrolü bekliyor.
- Yeni publish/ZIP üretilmedi; Drive'a yükleme yapılmadı. Uygulama ekranına
  veya Claude'un üzerinde çalıştığı kaynak dosyalara müdahale edilmedi.

## [Arama Varsayılanları — 80/20] — 2026-09-04

**Durum:** Kaynak commit'i bekleyen çalışma kopyası; yukarıdaki kontrol
noktası ve `docs/PROJECT_CONTEXT.md` devir notuyla birlikte okunmalıdır.

### Eklendi
- **Açılışta "Minimum benzerlik (%)" 80; "En fazla sonuç", geçerli kayıtlı
  tercih varsa o değer, yoksa 20 ile dolu geliyor** (önceki 15 varsayılanı
  yerine). Bir alan boş/yalnızca boşluk bırakılıp "Ara"ya
  basılırsa o alan için AYNI varsayılan kullanılır — ama mevcut sıkı
  doğrulama kuralları değişmedi: metin, negatif, 100 üstü, NaN/Infinity
  benzerlikte hâlâ reddedilir; metin, ondalık, 0, negatif, 200 üstü sonuç
  sayısında hâlâ reddedilir (benzerlikte 0 geçerlidir, sonuç sayısında 0
  geçersizdir — bu ayrım korunuyor). Yeni `SimilarityThreshold.
  ResolveOrDefault` / `MaxResultsPreference.ResolveOrDefault`, mevcut katı
  `TryParse`'ları DEĞİŞTİRMEDEN yalnızca gerçekten boş girdiyi varsayılana
  çevirir. Doğrulama sonrası kullanılan değer, yalnızca kutu BOŞSA geri
  yazılır — kullanıcının kendi geçerli girdisi (ör. "65", "80,5") asla
  dokunulmaz/yeniden biçimlendirilmez. Boş girdiden çözülen "en fazla
  sonuç" değeri de diğer geçerli değerler gibi kalıcı tercihe kaydedilir.
  Mevcut geçerli kayıtlı tercihler (15, 50, 200 vb.) yeni varsayılana
  ÇEVRİLMEZ — yalnızca "tercih hiç yok/geçersiz/boş girdi" durumunda
  devreye girer. Benzerlik için kalıcı bir ayar alanı eklenmedi. "Yeni
  Arama", tema değişimi ve klasör değişimi bu iki kutuya dokunmuyor.
  Otomatik indeksleme varsayılanı zaten `true` idi, değiştirilmedi; mevcut
  kayıtlı `false` tercihi de korunur. Detay:
  `docs/DECISIONS.md` #76.

### Test
- `Lens.AiProof hardeningtest` Grup K (26 kontrol) eklendi; Grup J'deki
  yalnızca eski varsayılanı (15) sınayan senaryolar 20'ye güncellendi,
  geçerli kayıtlı 15'i sınayan kontrol bilerek değiştirilmedi, kayıtlı
  15/200'ün 20'ye topluca çevrilmediğini doğrulayan 2 yeni kontrol eklendi
  — toplam **158/158 PASS**. `dotnet build` Debug/Release **0 warning/0
  error**. **Canlı doğrulanamayan:** açılış görünümü ve "Ara" sonrası
  kutulara yazılan 80/20 — kullanıcı ekranı aktif kullandığı için yalnızca
  kod incelemesiyle doğrulandı.

## [Ayarlar Sadeleştirme ve Klasör Adresini Elle Girme] — 2026-09-04

**Durum:** Kaynak commit'i bekleyen çalışma kopyası. Aşağıdaki uygulama
kaydı, tüm kullanıcı beklentilerinin karşılandığı anlamına GELMEZ; açık
uyuşmazlıklar karar #75 ve `docs/PROJECT_CONTEXT.md` devir notunda izlenir.

### Değişti
- **Ayarlar penceresi sadeleştirildi.** Eski salt-metin "Ayarlar" mesajı
  (ürün klasörünün tam yolu + yönetici config/kullanıcı ayar dosyası
  yollarını serbestçe gösteriyordu) kaldırıldı; yerine yeni `SettingsWindow`
  geldi. Klasörün tam yolu artık burada tekrar gösterilmiyor (zaten ana
  ekranda var) — yalnızca kısa bir durum cümlesi ("Geçici seçim/Kullanıcı
  varsayılanı/Yönetici varsayılanı/Klasör seçilmedi"). Teknik dosya yolları
  (yönetici config, kullanıcı ayarları, log klasörü, önbellek klasörü,
  model dosyası) varsayılan olarak **kapalı** bir "Teknik ayrıntılar"
  bölümünde — hiçbir silme/temizleme/klasör açma komutu yok, yalnızca
  kopyalanabilir salt-okunur metin. Klasör işlemleri hâlâ ana ekrandaki
  mevcut butonlarla, tema hâlâ ⋮ → "Arka Plan" ile yönetiliyor. Ayarlar
  açmak/kapatmak hiçbir tercihi değiştirmez/kaydetmez. **Açık uyuşmazlık:**
  teknik ayrıntı bölümünün tamamen kaldırılması beklentisi henüz
  karşılanmadı; kullanıcı düzeltmeyi tasarım sonrasına erteledi.
- **"Log Klasörünü Aç" menü seçeneği kaldırıldı.** Normal kullanıcıya
  log/indeks/cache/model açma/silme/temizleme komutu sunulmuyor. Normal
  loglama, indeksleme (manuel ve otomatik) ve "Sorunlu / Atlanan Dosyalar"
  davranışı değişmedi.
- **Ürün klasörü adres kutusu artık düzenlenebilir.** Kullanıcı adresi
  yazabilir/yapıştırabilir; fareyle seçim, imleç, Ctrl+A/C/X/V/Z,
  Delete/Backspace, sağ-tık metin menüsü ve yatay gezinme için normal
  düzenlenebilir TextBox kullanılır; gerçek ekranda doğrulama bekleniyor.
  Yazılan adres bir **taslaktır** — her tuşta aktif klasör
  değişmez, disk kontrolü/indeksleme başlamaz. **Enter**: adres biçim
  olarak doğrulanır (boş ve testlerde kapsanan göreli/sözdizimsel geçersiz
  girdiler reddedilir; tam adres denetimi ve kök adresin korunması için
  açık düzeltme vardır). Dış çift tırnak/baş-son boşluk temizliği, yoldaki
  boşluk ve Türkçe karakterlerin korunması testlerde kapsanır. Ardından
  arka planda dosya/klasör kontrolü
  yapılır; başarılıysa uygulanır. Aynı Enter aramayı **başlatmaz**. **Esc**:
  taslağı iptal edip aktif klasöre döner. Odak kaybı tek başına hiçbir şeyi
  uygulamaz. Kutunun altında kısa bir ipucu ("Adresi uygulamak için Enter,
  iptal etmek için Esc.") ve gerektiğinde anlaşılır hata mesajları
  gösterilir. Mevcut kompakt/esnek genişlik korundu, "Ürün Klasörü Seç"
  butonu alternatif olarak çalışmaya devam ediyor.
- **Taslak/aktif klasör tutarlılığı.** Adres kutusunda uygulanmamış bir
  değişiklik varken "Ara", "İndeksi Güncelle" veya "Bu Klasörü Varsayılan
  Yap" artık durur ve kullanıcıyı Enter/Esc'e yönlendirir (yalnızca buton
  görünümüne değil, olay işleyicisinin kendisine dayanan bir kontrol).
  İlk biçim/varlık doğrulamasında reddedilen giriş uygulanmaz. Ancak
  yükleme başarısızlığında eski aktif klasör/index/sonuçların korunması
  ve aynı klasör yeniden uygulandığında varsayılan kaynağının değişmemesi
  henüz sağlanmış kabul edilemez; bu iki durum için düzeltme ertelendi.
  Farklı klasöre geçiş akışında eski sonuç/istatistik/sorunlu dosya listesi
  temizlenir; bunun yükleme başarısıyla tutarlı yapılması ayrıca ele alınacak.
  Hem "Ürün Klasörü Seç" (dialog) hem elle adres uygulama artık AYNI paylaşılan
  geçiş mantığını (`ApplyNewProductFolderAsync`) kullanıyor. Yeni klasör
  seçimi/adres oturumluktur — kalıcılaştırma yalnızca "Bu Klasörü Varsayılan
  Yap" ile olur.
- **Başlangıç yüklemesine yarış durumu kontrolleri eklendi.** Gecikmiş
  varsayılan klasör yüklemesinin kullanıcının daha yeni seçimini/taslağını
  ezmesini önlemek amaçlanıyor. Tüm zamanlama ve başarısızlık senaryoları
  uçtan uca doğrulanmadı; "yarış durumu tamamen çözüldü" kabulü yapılmamalı.
- Arama/indeksleme/klasör yükleme sırasında adres kutusu artık `IsEnabled`
  ile değil `IsReadOnly` ile kilitleniyor — böylece meşgulken de mevcut
  adresi seçmek ve kopyalamak mümkün kalıyor (disabled bir WPF kutusunda
  seçim/kopyalama çalışmaz, salt-okunur kutuda çalışmaya devam eder).

### Not
Kullanıcının daha önce bildirdiği "adresi kopyalayamıyorum" şikâyetinin
kesin nedeni doğrulanamadı — `IsReadOnly` tek başına WPF'te metin
seçimini/kopyalamayı engellemez, bu yüzden başka bir etken de olabilir.
Kutunun artık tam düzenlenebilir olması istenen düzenleme davranışını
sağlamayı amaçlıyor; ilk kopyalama şikâyetinin giderildiği kullanıcının
gerçek ekran kontrolünden önce kesinleştirilemez.

### Test
- `Lens.AiProof hardeningtest` Grup L (13 kontrol: tam yerel/UNC yol, boşluk,
  Türkçe karakter, dış tırnak, baş/son boşluk, sondaki ayırıcı, boş/null/
  yalnızca-boşluk reddi, göreli yol reddi, geçersiz sözdizimi reddi) eklendi
  — toplam **158/158 PASS**. `dotnet build` Debug/Release **0 warning/0
  error**. **Canlı doğrulanamayan:** taslak/uygula/iptal/kilit/yarış-durumu
  akışının ve yeni Ayarlar penceresinin gerçek ekranda görünümü/davranışı —
  kullanıcı ekranı aktif kullandığı için yalnızca kod incelemesi yapıldı;
  disk erişimi gerektiren senaryolar (dosyaya işaret eden/
  erişilemeyen adres) headless test edilmedi (`Directory.Exists`/
  `File.Exists`'in kendisi değil, MainWindow'daki çağıran kod test edilmedi).

## [Başlıkta Gerçek Sonuç Sayısı] — 2026-09-04

### Eklendi
- **"EN BENZER SONUÇLAR" başlığı artık parantez içinde ekranda gerçekten
  listelenen kart sayısını gösteriyor** — ör. "EN BENZER SONUÇLAR (32)".
  Sayı doğrudan `_results.Count`'tan gelir: eşik VE kullanıcının "en fazla
  sonuç" limiti (bkz. bir önceki girdi) zaten uygulanmış, ekrandaki gerçek
  kart sayısıdır (`IndexStatusText`'teki "N sonuç gösteriliyor." ile AYNI
  kaynak) — ör. 80 eşleşmeden limit nedeniyle 15'i listeleniyorsa "(15)"
  yazar, toplam eşleşme sayısı gibi sunulmaz. İlk açılışta ve liste
  temizlendiğinde ("Yeni Arama", yeni sorgu görseli, ürün klasörü değişimi,
  yeni aramanın BAŞLANGICI) "(0)" gösterilir; arama tamamlandığında
  güncellenir. Geçersiz girdide (threshold veya "en fazla sonuç" hatalı)
  liste zaten dokunulmadığı için başlıktaki sayı da AYNEN kalır. Tema, kart
  seçimi ve kaydırma davranışına dokunulmadı.

## [Sorgu Kilidi ve Kullanıcının Sonuç Sayısını Belirlemesi] — 2026-09-04

### Eklendi
- **"En fazla sonuç" girişi** — "Minimum benzerlik (%)" alanının yanına eklendi.
  Varsayılan 15, izin verilen aralık 1-200 tam sayı (sabit teknik üst sınır
  200 olarak kalıyor, bkz. bir önceki "Sonuç Sınırı 15 → 200" girişi). Alanda
  yazma/yapıştırma sırasında bir kısıtlama YOK — doğrulama yalnızca "Ara"ya
  basıldığında yapılır ve duruma göre İKİ FARKLI mesaj gösterilir:
  - Değer geçerli bir tam sayı ama 200'ü aşıyorsa: **"En fazla 200 sonuç
    listeleyebilirsiniz."**
  - Boş, 0, negatif veya tam sayı değilse (harf, ondalık nokta/virgül dahil):
    **"Lütfen 1-200 arasında bir tam sayı girin."**

  Her iki durumda da uyarı alanın altında gösterilir, arama BAŞLATILMAZ ve
  mevcut sonuçlar/karşılaştırma/kaydırma AYNEN korunur (threshold'un geçersiz
  girdi davranışıyla birebir aynı desen — bkz. "Eski Sonuçların Erken
  Temizlenmesi" girdisi). Kullanıcı geçerli bir değer girip tekrar aradığında
  uyarı otomatik kalkar. Geçerli tercih yalnızca arama başarıyla başlatıldığında
  kullanıcının kendi `user-settings.json` dosyasında (`PreferredMaxResults`)
  kalıcı olur — tema/otomatik indeksleme/klasör tercihini etkilemez. Eski
  (alanı içermeyen) veya bozuk/aralık dışı kayıtlı bir değer güvenle 15'e
  döner. Alan, arama/indeksleme sürerken diğer kontrollerle birlikte devre
  dışı kalır. Arama sırası aynen korundu: eşik → azalan sıra → kullanıcının
  istediği sayı kadar al; yetersiz eşleşmede düşük benzerlikli ürünlerle
  DOLDURMA yapılmaz. Başlık "EN BENZER SONUÇLAR" olarak kaldı, arayüzde sabit
  "en fazla 200" gibi bir ifade eklenmedi. Detay: `docs/DECISIONS.md` #73.
- **Çekirdek katmanda ikinci doğrulama** — `SimilaritySearch.SearchWithThreshold`
  artık `maxResults` 1-200 aralığı dışındaysa `ArgumentOutOfRangeException`
  fırlatır (sessizce başka bir sayıya çevrilmez). UI zaten aramadan önce
  doğruladığı için normal kullanımda tetiklenmez — bu, bir programlama hatasına
  karşı ikinci savunma katmanıdır.

### Düzeltildi
- **Arama/indeks hazırlığı sürerken sorgu artık değiştirilemiyor.** Önceden
  "Yeni Arama" butonu ve sorgu görseli sürükle-bırak alanı `SetBusy` akışına
  dahil DEĞİLDİ — bir arama sürerken bu ikisi hâlâ etkindi, teorik olarak
  sorguyu arama bitmeden değiştirebiliyordu. Artık ikisi de `SetBusy` ile
  devre dışı bırakılıyor VE yalnızca görsel devre dışı bırakmakla
  yetinilmiyor: `NewSearchButton_Click` ve
  `QueryDropZone_DragEnter/DragOver/Drop` olay işleyicilerinin her biri
  ayrıca bağımsız bir "işlem sürüyor mu" kontrolü (`IsBusy`) yapıyor —
  buton/alan zaten devre dışıyken bir olay yine de tetiklenirse (ör. klavye/
  otomasyon kaynaklı) sorgu yine değişmez. Meşgulken sürüklenen bir görsel
  kabul edilmez VE "kabul edilebilir" sürükleme vurgusu (accent border/
  önizleme) hiç gösterilmez. `SetBusy`, olası bir iç içe (nested) çağrı
  ihtimaline karşı artık bir derinlik sayacına (`_busyDepth`) dayanıyor —
  içteki bir işlem bitse bile dıştaki arama bitmeden koruma kalkmaz. Sorgu
  yolu, eşik ve yeni "en fazla sonuç" değeri zaten arama başlangıcında local
  değişkenlere sabitleniyordu (bkz. önceki girişler) — bu değişiklik buna
  ikinci bir savunma katmanı (alanların ayrıca devre dışı bırakılması) ekledi.
  Mevcut "eski sonuçları arama başlar başlamaz temizle" düzeltmesi korundu.

### Test
- `Lens.AiProof hardeningtest` Grup J eklendi (39 kontrol): "en fazla sonuç"
  girdi validasyonu (1/15/50/200 sınırları, 0/201/negatif/ondalık/boş/null
  reddi), iki-mesaj ayrımı (`IsAboveMaxAllowed`), `UserSettings.
  PreferredMaxResults` JSON sözleşmesi (varsayılan, geriye uyumluluk, alan
  korunumu), `SimilaritySearch.SearchWithThreshold` limit=1/15/50/200 +
  yetersiz eşleşmede doldurmama + çekirdek katman `ArgumentOutOfRangeException`.
  Toplam **117/117 PASS**. `dotnet build` Debug/Release **0 warning/0 error**.
  **Canlı doğrulanamayan:** sorgu kilidinin (arama sürerken "Yeni Arama"/
  sürükle-bırakın gerçekten pasif kaldığı, hata sonrası tekrar aktif olduğu)
  ve yeni "En fazla sonuç" alanının minimum pencere boyutunda diğer
  kontrolleri taşırmadığı gerçek ekranda test edilmedi — kullanıcı ekranı
  aktif kullandığı için canlı UI otomasyonu/masaüstü müdahalesi yapılmadı,
  yalnızca kod incelemesi ve headless testlerle doğrulandı.

## [Sonuç Sınırı 15 → 200 ve Temiz Teslim Paketi] — 2026-09-04

### Değişti
- **Sabit maksimum sonuç sayısı 15'ten 200'e çıkarıldı**
  (`SimilaritySearch.MaxResults`). Kullanıcıya sonuç sayısını seçebileceği
  yeni bir giriş alanı EKLENMEDİ — bu, önceki "kullanıcıdan sonuç sayısı
  al / 1-100 seç" isteğinin yerine geçen bir yönetici kararıdır. Mevcut
  minimum benzerlik eşiği aynen korunuyor: eşiği karşılayan sonuçlar
  benzerlik skoruna göre azalan sırada getirilir; 200'den fazlaysa yalnızca
  en iyi 200'ü gösterilir (fazlası sessizce elenir, hata değildir). Eşiği
  karşılayan sonuç 200'den azsa listeyi doldurmak için düşük benzerlikli
  ürün EKLENMEZ (doldurma/padding yok) — benzerlik hesaplaması ve threshold
  sözleşmesi değişmedi. Detay: `docs/DECISIONS.md` #72.
- **"EN BENZER SONUÇLAR (EN FAZLA 15)" başlığı → "EN BENZER SONUÇLAR"**
  oldu. Arayüzde "en fazla 200" veya başka bir sabit üst sınır açıklaması
  BİLEREK gösterilmiyor.
- **Durum mesajı artık gerçekten listelenen sayıyı söylüyor:** "N sonuç
  bulundu." → "N sonuç gösteriliyor." (`N` = `_results.Count`, yani
  ekranda GERÇEKTEN görünen kart sayısı). Kesilmiş bir listede bu sayı
  toplam eşleşme sayısı gibi sunulmuyor — toplam eşleşme sayısı zaten ayrı
  bir yerde izlenmiyor/gösterilmiyor.
- Tema, kart boyutu, ortalanmış sorgu/karşılaştırma görselleri, kaydırma
  düzeni ve "yeni aramada eski sonuçları hemen temizle" düzeltmesi
  (bkz. yukarıdaki "Eski Sonuçların Erken Temizlenmesi" girdisi)
  DEĞİŞMEDEN korundu.

### Performans
- **200 sonuca kadar thumbnail decode'u artık arka plan thread'inde**
  (`SearchButton_Click` içindeki mevcut `Task.Run`'a taşındı) — önceden
  UI thread'de, arama sonucu döndükten SONRA sırayla yapılıyordu; 15
  sonuçta gözle görülür bir donma yaratmıyordu ama 200 sonuçta art arda
  200 JPEG decode'u fark edilir bir arayüz kilitlenmesi riski
  taşıyordu. `BitmapImage.Freeze()` (mevcut `LoadPreview` zaten bunu
  yapıyordu) sayesinde arka planda oluşturulan görsel thread-safe şekilde
  UI'ya taşınabiliyor. Yükleme BİLEREK sıralı (sıralı/tek thread) bırakıldı
  — sabit bir donma riskini ortadan kaldırmak yeterliydi, sınırsız paralel
  decode/bellek/CPU baskısı eklenmedi.

### Test
- `Lens.AiProof hardeningtest` Grup F ("arama sözleşmesi"), yeni 200
  sınırına göre güncellendi: 0, 1, 6, 15 (yeni sınırın ALTINDA — artık
  kesilmiyor), tam 200 (sınırda kesilme yok) ve 250 (200'ü aşan, fazlası
  atılıyor, doldurma yok) eşleşme senaryoları; her birinde azalan sıra ve
  (200-üstü durumda) en iyi 200'ün seçildiği ayrı ayrı doğrulandı. Tüm
  78 kontrol PASS (0 FAIL). `dotnet build` Debug/Release'de 0 warning/0
  error. **Canlı doğrulanamayan:** 200 sonuçlu gerçek bir aramanın
  arayüzde akıcı kaydığı/donmadığı — kullanıcı bilgisayarı kullanırken
  arayüz otomasyonu/uzaktan tıklama yapılmadı (bkz. proje talimatı);
  gerekirse kullanıcı kendi ortamında kısa bir manuel kontrolle
  doğrulayabilir.

### Temiz teslim paketi
- `publish/Lens.Desktop-win-x64-themes/` aynı kaynak sürümünden, temiz
  (`dotnet clean` sonrası) `dotnet publish -p:DebugType=None` ile yeniden
  üretildi. Önceki turda yalnızca `.pdb` dosyalarının silinmesinin YETERLİ
  OLMADIĞI netleşti — `.dll`/`.exe` dosyalarının debug directory'si de
  PDB'nin yerel derleme yolunu (`C:\Users\...`) taşıyordu. `DebugType=None`
  ile PDB hiç ÜRETİLMİYOR, dolayısıyla `.dll`/`.exe` içinde ona işaret eden
  bir yol da kalmıyor. Doğrulandı: pakette `.pdb` yok; `Lens.Desktop.dll`,
  `Lens.Core.dll`, `Lens.Desktop.exe` içinde yerel kullanıcı adı/yol dizesi
  yok (ikili tarama ile); `appsettings.json` boş dağıtım şablonu
  (`AdminDefaultProductDirectory: ""`); CLIP ONNX modeli ve tüm çalışma
  zamanı bağımlılıkları pakette mevcut; gerçek kullanıcı ayarı/ürün
  görseli/index/log/test dosyası YOK. Diğer üç publish paketi
  (`-win-x64`, `-manager`, `-manager-shared-index`) dokunulmadan korundu.
  Detay: `docs/DECISIONS.md` #72.

## [Düzeltme — Eski Sonuçların Erken Temizlenmesi] — 2026-09-04

### Düzeltildi
- **Bir önceki aramanın sonuçları artık yeni bir arama BAŞLAR BAŞLAMAZ
  temizleniyor** (index hazırlığı/otomatik indeksleme/model yükleme dahil
  uzun süren işlemlerden ÖNCE), sadece yeni arama BAŞARIYLA bittiğinde
  değil. Önceki davranışta, girdi geçerliyse ama arama index hazırlığı
  sırasında hata verirse veya embed adımında istisna oluşursa (ör. sorgu
  görseli aramalar arasında silinmiş/taşınmışsa), önceki aramaya ait
  sonuç kartları/karşılaştırma paneli/kaydırma konumu ekranda kalıp yeni
  sorgunun sonucuymuş gibi görünebiliyordu — bir önceki turun scroll-reset
  düzeltmesi (bkz. aşağıdaki "Kaydırma Düzeltmesi" girdisi, `#69`) bu
  senaryoyu KAPSAMIYORDU çünkü temizleme kodu hâlâ aramanın SONUNDA
  çalışıyordu. Artık `SearchButton_Click`'te klasör/görsel/threshold
  validasyonu başarılı olur olmaz eski sonuçlar/karşılaştırma/kaydırma
  temizlenir ve durum metni "Aranıyor..." olur; sorgu görseli, threshold
  girdisi, tema ve kullanıcı ayarları bundan ETKİLENMEZ. Geçersiz/eksik
  girdide (klasör/görsel seçilmemiş, threshold 0-100 dışı veya sayısal
  değil) bu temizleme koduna hiç ulaşılmadığından mevcut ekran ve
  kaydırma konumu AYNEN korunur — kullanıcı hatalı bir değeri düzeltip
  tekrar deneyebilir. Model yüklenemezse veya arama sırasında bir istisna
  oluşursa (ör. silinmiş sorgu dosyası) artık zaten boşaltılmış ekranda
  açıklayıcı bir durum mesajı ("Model yüklenemedi, arama yapılamadı." /
  "Arama başarısız oldu.") gösterilir. Detay: `docs/DECISIONS.md` #71.

### Doğrulama
Gerçek (derlenmiş Debug) uygulama, UI Automation (`System.Windows.Automation`)
ile canlı çalışırken uçtan uca test edildi — kod incelemesiyle sınırlı
kalınmadı:
- Sonuç listesi aşağı kaydırıldıktan sonra aynı sorgu görseliyle farklı bir
  eşikle tekrar arandığında, YENİ sonuç kümesinin de (15 sonuç, görünür
  alanın ~%43'ü) gerçekten kaydırma gerektirdiği doğrulandı ve kaydırma
  konumunun başa döndüğü ölçüldü (`ScrollPattern.VerticalScrollPercent`
  60 → 0) — tek başına `ScrollPercent=-1` (kaydırılacak içerik yok)
  görülmesiyle YETİNİLMEDİ.
- Başarılı bir aramanın ardından sorgu görseli diskten silinip tekrar
  "Ara"ya basıldığında: önceki 15 sonuç/karşılaştırma panelinin arama
  butonuna tıklandıktan ~100ms içinde (sonuç TAMAMLANMADAN) zaten
  temizlendiği, ardından gerçek bir `FileNotFoundException`'ın log
  dosyasına ve kullanıcıya gösterilen hata penceresine yansıdığı
  doğrulandı.
- Geçerli 15 sonuçlu bir arama ekranı kaydırılmış haldeyken geçersiz bir
  eşik ("150") girilip "Ara"ya basıldığında: sonuç sayısının, karşılaştırma
  panelinin VE kaydırma konumunun (ör. %45) DEĞİŞMEDİĞİ, yalnızca
  threshold doğrulama mesajının göründüğü doğrulandı.
- "Yeni Arama", yeni bir sorgu görseli seçimi ve ürün klasörü değişiminin
  her biri sonuçları/karşılaştırmayı temizlediği ve (içerik varsa) kaydırmayı
  başa döndürdüğü doğrulandı.
- Tema değişiminin ve halihazırda GÖRÜNÜR olan bir sonuç kartına
  tıklamanın kaydırma konumunu bozmadığı doğrulandı (ekran dışındaki bir
  karta programatik tıklamanın WPF'in doğal "odaklanılan öğeyi görünür
  kıl" davranışıyla kaydırmayı hareket ettirdiği de ayrıca gözlemlendi —
  bu, gerçek bir fare tıklamasıyla oluşamayacak bir test artefaktıdır,
  koddaki bir regresyon değildir).
- Sonuç/sorgu dosya adı kutularından birinde metin seçilip gerçek bir
  Ctrl+C (pencere gerçekten ön plana alınıp `SendKeys` ile) denendi;
  pano içeriğinin ekrandaki dosya adıyla birebir eşleştiği doğrulandı.

**Build/test:** `dotnet build` Debug ve Release'de 0 warning/0 error.
Bu tur için ayrı bir otomatik/birim test eklenmedi (mevcut kapsam UI
davranışı; `Lens.AiProof` konsol test aracının kapsamı değişmedi).
**Canlı doğrulanamayan noktalar:** gerçek OS sürükle-bırak ile yeni sorgu
görseli seçiminin kaydırmayı sıfırlaması (bu turda dosya diyaloğu üzerinden
test edildi, sürükle-bırak yolu ayrıca denenmedi — kod yolu aynı
`LoadQueryImage` metodunu kullanıyor); çoklu monitör/DPI kombinasyonlarında
kaydırma/tema davranışı.

## [Görsel Güncelleme — Tema Seçenekleri, Kart Yerleşimi, Kaydırma Düzeltmesi] — 2026-09-04

### Eklendi
- **Arka plan teması menüsü** (⋮ → Arka Plan): Açık (`#F5F6F8`), Normal
  (`#64748B`, varsayılan), Koyu (`#334155`), Açık Sepya (`#E8DCC8`), Koyu
  Sepya (`#705C46`), Lime — Deneme (`#D4E157`). Seçili tema menüde
  işaretli görünür, seçim anında uygulanır (yeniden başlatma gerekmez) ve
  kullanıcının kendi `user-settings.json` dosyasında (`Theme` alanı)
  kalıcı olur — shared index/ürün klasörüne yazılmaz, diğer kullanıcıları
  etkilemez. Bilinmeyen/eski (alanı içermeyen) değer güvenle "Normal"e
  döner. Tema değişimi query/sonuçlar/seçili kart/threshold/tarama
  istatistiklerine dokunmaz, indeksleme tetiklemez.
- Sorgu/seçilen sonuç görsel çifti artık pencerede **tek bir grup olarak
  birlikte ortalanır** (aralarında sabit ~40 DIP boşluk), önceden iki
  bağımsız `*` sütunda ayrı ayrı ortalanıp pencere büyüdükçe birbirinden
  uzaklaşıyorlardı.

### Düzeltildi
- **Az sonuçlu aramada seçili kartın mavi çerçevesi artık sonuç alanının
  sonuna kadar boşuna uzamıyor** — kök neden: `UniformGrid`, kendisine
  `ScrollViewer` tarafından verilen (içerikten daha büyük olabilen)
  ARRANGE boyutunu satır sayısına bölerek hücre yüksekliğini hesaplıyordu;
  `ResultsItemsControl`'e `VerticalAlignment="Top"` eklenerek liste artık
  yalnızca kendi doğal içerik yüksekliğini kaplıyor. Thumbnail boyutu
  1-15 sonuç arasında tutarlı kalır, uzun dosya adları kesilmez.
- **Yeni arama sonuç viewport'unu artık her zaman en baştan gösteriyor** —
  önceden bir aramanın sonuçları aşağı kaydırıldıktan sonra yeni bir arama
  (aynı sorgu + farklı eşik dahil), yeni sorgu görseli (dosya seçimi veya
  sürükle-bırak), "Yeni Arama" veya ürün klasörü değişimi eski kaydırma
  konumunu koruyordu; ilk satır/kart bazen kırpılmış görünüyordu. Adı artık
  `ResultsScrollViewer` olan viewport, bu dört durumda `Dispatcher.Loaded`
  önceliğiyle tek seferlik `ScrollToTop()` ile sıfırlanır (sürekli bir
  olay aboneliği DEĞİLDİR — kullanıcının sonradan yaptığı manuel kaydırmayı
  geri almaz). Geçersiz girdi (klasör/görsel/threshold eksik) veya arama
  sırasında hata oluşması mevcut ekranı/kaydırmayı etkilemez; kart seçimi,
  önizleme, tema değişimi ve pencere yeniden boyutlandırma kaydırmayı
  sıfırlamaz.

### Değişti (mimari, salt UI)
- Ana pencerenin boş çalışma alanları (üst kontrol/durum/sayaç şeridi,
  sorgu-eşik çubuğu, sonuç viewport'unun boş kısımları, alt bilgi) artık
  ayrı sabit açık panel blokları DEĞİL — doğrudan seçili temanın zemin
  rengiyle bütünleşir. Sonuç kartları, thumbnail yüzeyleri ve sorgu/
  seçilen-sonuç görsel kutuları her temada nötr beyaz kalır. Renk
  kaynakları (`NeutralTextBrush`, `SectionHeaderBrush`, `SecondaryTextBrush`,
  `SuccessBrush`, `WarningBrush`, `MainBackgroundBrush`) artık tema
  değişiminde `DynamicResource` ile canlı güncellenir; mavi vurgu (`AccentBrush`)
  ve sorgu panelinin nötr/koyu çerçevesi (`NeutralBorderBrush`) temadan
  bağımsız sabit kalır. Yeni `Lens.Desktop.AppTheme`/`ThemePalette` (yalnızca
  UI katmanı) ve `UserSettings.Theme` (Lens.Core, varsayılan `"Normal"`)
  eklendi.

Detay: `docs/DECISIONS.md` #67-#69.

## [Görsel Güncelleme — Zemin Rengi Kontrastı] — 2026-09-04

### Değişti
- Ana pencere arka planı `#F5F6F8` yerine koyu slate `#64748B`; beyaz/açık
  renkli desenlerin (özellikle nevresim gibi açık tonlu ürünler) zeminden
  belirgin şekilde ayrışması amaçlanmıştır. Desen/sonuç kartları, sorgu ve
  seçilen sonuç görsel kutuları saf beyaz (`#FFFFFF`) olarak korunmuştur.
- Üst kontrol (klasör seçimi), durum/sayaç satırları ve sorgu/eşik çubuğu
  (Satır 0-3) tek parça açık renkli bir panel (`#F5F6F8`, `LightPanelBrush`)
  üzerine alındı; alt bilgi metni de aynı panel içine taşındı — okunabilirlik
  koyu zeminde bozulmasın diye.
- Koyu zemin üzerinde kalan öğeler (karşılaştırma alanı başlıkları/dosya
  adları/ipucu metni, sonuç listesi başlığı, %100 eşleşme vurgusu) için
  yeni `OnDarkTextBrush`/`OnDarkSecondaryTextBrush`/`OnDarkSuccessBrush`
  kaynakları eklendi; `MainWindow.xaml.cs` içindeki `ComparisonScoreText`
  renk ataması bu yeni kaynaklara güncellendi (önceki `NeutralTextBrush`/
  `SuccessBrush` koyu zeminde okunmuyordu).
- Sorgu görselinin nötr/koyu çerçevesi, seçilen sonucun ve seçili Top-15
  kartının mavi vurgusu değişmedi. Arama/threshold/indeksleme/kilit iş
  mantığına dokunulmadı — yalnızca XAML renk/arka plan ve ilgili 3 satırlık
  `Foreground` ataması değişti.

Detay: `docs/DECISIONS.md` #66. Kapsam: yalnızca `MainWindow.xaml` ve
`MainWindow.xaml.cs` (renk atamaları).

## [Faz 1 — Manager Requirement Paketi] — 2026-09-03

### Eklendi
- Minimum benzerlik (%) eşiği (inclusive, 0-100, TR virgül destekli) +
  en fazla 15 sonuç, azalan sırada (`SimilaritySearch.SearchWithThreshold`,
  `SimilarityThreshold`). Sabit Top-10/Top-5 sözleşmesi kaldırıldı.
- No-result artık hata değil: önceki sonuç/seçim temizlenir, query görseli
  ve threshold girdisi korunur, modal gösterilmez.
- "Arama öncesi indeksi otomatik kontrol et ve güncelle" checkbox'ı
  (varsayılan açık, `user-settings.json`'da kalıcı, geriye uyumlu).
- **Shared (paylaşılan) index**: canonical index artık
  `<ProductDirectory>/.lens/index.json` — `%LocalAppData%` DEĞİL. Eski
  local cache dosyaları otomatik silinmiyor ama normal operasyonda
  kullanılmıyor.
- Tek-yazarlı exclusive dosya kilidi (`Lens.Core.Indexing.IndexLock`,
  `.lens/index.lock`, `FileShare.None`) — eşzamanlı yazımı engeller,
  okuma kilitsizdir.
- `AtomicFileWriter` UNC/paylaşılan klasörler için güçlendirildi (benzersiz
  temp dosya adı, `File.Replace` → `Move(overwrite:true)` fallback).
- Ana UI'da ayrıntılı son-başarılı-tarama sayaçları (yeni/güncellenen/
  değişmeyen/silinen/okunamayan/desteklenmeyen görsel/dosya — sıfırlar dahil).
- Sonuç grid'i için bounded `ScrollViewer` düzeltmesi (tüm sonuçlara mouse
  wheel ile erişim); query/seçilen-sonuç alanı merkez hizalı iki sütun;
  arka plan `#F5F6F8`.
- `Lens.AiProof hardeningtest`'e 41 yeni test (shared index/lock, arama
  sözleşmesi, threshold validasyonu, auto-index persistence) — toplam 70.

### Kaldırıldı
- Sabit ~50 MB dosya boyutu / ~50 MP çözünürlük reddi (`ImageTooLargeException`
  ve ilgili guard tamamen kaldırıldı). Eşiğin üstündeki gerçek görseller artık
  reddedilmez, ekonomik (decoder-level downsampled) decode ile işlenir.

Detay: `docs/DECISIONS.md` #60-65, `docs/ARCHITECTURE.md`,
`docs/PRODUCTION_REQUIREMENTS.md` §5/§9/§15/§16.

## [Faz 4E] — 2026-09-02 — Reliability Hardening

### Eklendi
- Geçici dosya/network hatasında eski sağlam index kaydının korunması.
- Bozuk/uyumsuz cache dosyası için güvenli recovery (crash yok).
- UNC/network operasyonlarında UI freeze/crash riskinin azaltılması.
- Büyük/aşırı çözünürlüklü görsel için resource guard
  (`Lens.Core.Ai.ImageResourceLimits`, ~50MB/~50MP).
- PDF/ZIP/TXT gibi görsel olmayan dosyalar için "Desteklenmeyen dosya türü"
  görünürlüğü.
- `AlertWindow` (native `MessageBox` yerine tutarlı özel uyarı penceresi).
- `Lens.AiProof hardeningtest` modu — 29 fonksiyonel doğrulama testi.

Detay: `docs/ROADMAP.md` FAZ 4E, `docs/DECISIONS.md` #55-59.

## [Faz 4D] — 2026-09-01/02 — UI/UX Polish

### Eklendi
- Top-5 → Top-10 sonuç gösterimi.
- Query/karşılaştırma alanı, "Yeni Arama" butonu, minimal "⋮" menü.
- Büyük görsel önizleme/zoom penceresi (`ImagePreviewWindow`).
- Sürükle-bırak (drag & drop) query seçimi, drag preview, görsel geri
  bildirim (accent border, %100 eşleşme vurgusu, kopyalanabilir dosya adları).

Detay: `docs/ROADMAP.md` FAZ 4D, `docs/DECISIONS.md` #47-48, #52-54.

## [Faz 4B/4C] — 2026-09-01 — Robust Indexing & Logging

### Eklendi
- Dosya sınıflandırması (`SupportedImage`/`UnsupportedImageFormat`/`NonImage`),
  `IndexUpdateStats`/`IndexFileIssue` veri modeli.
- Search-before-refresh: 30 saniyelik freshness TTL ile otomatik incremental
  güncelleme.
- Kendi kodu ile dosya tabanlı logging (`ILensLogger`/`FileLogger`,
  `%LocalAppData%\Lens\logs\`, 30 gün retention).

Detay: `docs/ROADMAP.md` FAZ 4B, FAZ 4C.

## [Faz 4A] — 2026-09-01 — Configuration & Storage Architecture

### Eklendi
- Index/cache'in ürün klasöründen `%LocalAppData%\Lens\cache\<hash>\`'e
  taşınması, atomic write.
- Admin default (`appsettings.json`) / kullanıcı override (`%LocalAppData%\Lens\config\`)
  ayrımı.

Detay: `docs/ROADMAP.md` FAZ 4A, `docs/DECISIONS.md` #39-45.

## [Faz 3] — 2026-09-01 — C#/.NET WPF MVP

### Eklendi
- Lens Desktop uygulamasının ilk sürümü: klasör seçme, CLIP ONNX embedding
  (.NET/ONNX Runtime), persistent JSON index, Top-5 sonuç gösterimi
  (sonradan Faz 4D'de Top-10'a çıkarıldı).
- İlk demo dağıtım rehberi (`docs/DEMO_DEPLOYMENT_GUIDE.md`).

## [Faz 1-2] — 2026-08-31 — Model Değerlendirme (Python Benchmark)

### Eklendi
- CLIP vs SigLIP karşılaştırma aracı (Python), 11 gerçek ürün görseli + 55
  sentetik varyasyon üzerinde benchmark. Bu araç, uygulamanın runtime'ı
  değildir — yalnızca model seçim kararını desteklemek için kullanılmıştır.
- İlk proje/mimari hazırlık dokümanları (`docs/PROJECT_CONTEXT.md`,
  `docs/ARCHITECTURE_PROPOSAL.md`, `CLAUDE.md`).

---

## [Unreleased]

Bu bölüm, repo handover/release-hazırlığı çalışmasının (dokümantasyon,
`LICENSE`/`CONTRIBUTING`/`SECURITY`/`THIRD_PARTY_NOTICES`, `docs/ARCHITECTURE.md`,
`docs/MODEL_CARD.md` vb.) eklendiği bu turu kapsar — uygulama davranışında
bir değişiklik yoktur.
