# Changelog

Bu doküman [Keep a Changelog](https://keepachangelog.com/) biçimini takip
eder. Aşağıdaki geçmiş girdileri, mevcut git commit geçmişi ve
`docs/ROADMAP.md`'deki faz kayıtlarından **geriye dönük** doldurulmuştur —
bu proje henüz tag tabanlı bir release süreci kullanmadığı için sürüm
numarası yerine faz adı ve tarih kullanılmıştır. Buradan sonrası
`docs/RELEASE_PROCESS.md`'de önerilen tag tabanlı release sürecine göre
güncellenmelidir.

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
