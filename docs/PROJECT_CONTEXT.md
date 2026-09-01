# Proje Bağlamı — Lens

Bu doküman, projenin şu ana kadar konuşulmuş gereksinimlerini ve kısıtlarını
kaydeder. Amaç: gelecekteki çalışma turlarında ve mimari toplantıda ortak bir
referans noktası olmak.

Notasyon: **Confirmed** = kullanıcı tarafından açıkça belirtildi.
**Open Question** = henüz netleşmedi, karar bekliyor.
**Later Phase** = ilk PoC/MVP kapsamı dışında, ileride değerlendirilecek.

---

## 1. Problem Tanımı

**Confirmed**
- Fabrika, daha önce ürettiği nevresim ürünlerine ait görselleri arşivliyor.
- Kullanıcı yeni bir ürün görseli verecek; sistem geçmiş görseller arasında
  aynı veya en benzer ürünleri bulacak.
- Öncelik: **recall**. Doğru ürünü kaçırmak, birkaç fazla aday göstermekten
  daha kötü bir sonuç kabul ediliyor.
- Sistem kesin "bu üründür" kararı vermeyecek; ~5-10 aday listeleyecek.
- Nihai kararı kullanıcı (fabrika çalışanı) verecek.

---

## 2. MVP / PoC Kapsamı

**Confirmed**
- Hedef: 1-2 gün içinde yöneticilere çalışan bir demo göstermek.
- İlk versiyon sadece **görselle arama** içerir.
- Demo, klasörde bulunan mevcut görseller üzerinden çalışabilir
  (gerçek DB entegrasyonu zorunlu değil).
- **[Faz 3, 2026-09-01]** Bugünkü MVP akışı: kullanıcı klasör seçer → uygulama
  klasördeki görselleri gösterir → kullanıcı query görseli seçer → embedding
  çıkarılır → klasördeki görsellerle karşılaştırılır → en yakın 5 ürün (mümkünse
  similarity score ile) gösterilir → son kararı kullanıcı verir.
- **[Faz 3, 2026-09-01]** Bugünkü MVP'de görsel kaynağı yalnızca **local klasör**.
- **[Faz 3, 2026-09-01]** Bugünkü MVP'de **login/yetkilendirme yok**,
  **ürün ekleme/CRUD/DB'ye yazma yok**, **text search yok**.
- **[Faz 3A, 2026-09-01]** İlk ürün index'i: mevcut 11 gerçek nevresim görseli
  (`nevresim/`), .NET/CLIP embedder ile üretilir. Faz 2'nin 55 sentetik
  varyasyonu yalnızca benchmark/test verisidir, ürün index'ine girmez.
- **[Faz 3A, 2026-09-01]** Embedding'ler kalıcı local cache/index olarak
  saklanır (vector DB/SQLite yok); klasör her seçildiğinde yeniden
  hesaplanmaz. İlk indeksleme tüm görselleri embed eder; sonraki
  çalıştırmalarda yalnızca yeni/değişmiş/silinmiş dosyalar işlenir
  (relative path + file size + LastWriteTimeUtc ile tespit). Detay:
  DECISIONS.md #23.
- **[Faz 3A, 2026-09-01]** MVP iş kuralı (geçici): aynı desenin farklı renk
  varyasyonu aynı ürün kabul edilir. Ekstra grayscale/özel algoritma
  eklenmeyecek. Bkz. DECISIONS.md #25.
- **[Faz 3A, 2026-09-01]** Query görselleri MVP'de temiz/katalog tipi olacak;
  karmaşık telefon fotoğrafı, kırışık kumaş, perspektif farkı, karmaşık
  arka plan kapsam dışı. Bkz. DECISIONS.md #26.
- **[Faz 3A, 2026-09-01]** Final MVP self-contained Windows publish olarak
  dağıtılacak (Visual Studio/.NET SDK/Python gerekmeden çalışır). Bkz.
  DECISIONS.md #27.

- **[Faz 4, 2026-09-01 — ikinci yönetici görüşmesi]** Proje artık demo/PoC
  değil, fabrikada gerçekten kullanılacak bir üründür. Yeni gereksinimler
  ayrı bir dokümanda toplanıyor: `docs/PRODUCTION_REQUIREMENTS.md`. Bu
  görüşmede kapsam kalıcı olarak değişti (aşağıya bkz.) — detaylı Confirmed/
  Recommended/Open Question ayrımı için o dokümana bakılmalı, burada tekrar
  edilmiyor.

**Later Phase (Production Hedefi — henüz implement edilmeyecek)**
- Text search.
- Login / kimlik doğrulama (production'da planlı; bkz. DECISIONS.md #16).
- Kullanıcı geçmişi.
- Raporlama.

**Kalıcı olarak kapsam dışı (Later değil — bkz. DECISIONS.md #30, #31)**
- Görsel kaynağı olarak fabrika veritabanı — **SUPERSEDED**, kaynak artık
  yalnızca dosya dizini (`docs/PRODUCTION_REQUIREMENTS.md` §2).
- Yeni ürün girişi / CRUD, Lens uygulaması üzerinden — **SUPERSEDED**, yeni
  ürün ekleme Lens'in sorumluluğu değil (`docs/PRODUCTION_REQUIREMENTS.md` §1).

---

## 3. Mevcut Veri

**Confirmed**
- Şu anda 11 adet nevresim görseli mevcut; her biri farklı bir ürüne ait.
- Görseller temiz, katalog/dijital tasarım görünümünde (telefon fotoğrafı değil).
- Ürünler arası temel ayrım: **desen ve renk**.
- Ölçü (boyut) bilgisi görselden belirgin değil.
- İleride yönetici daha fazla görsel sağlayabilir; görseller klasör
  halinde gelecek (bkz. aşağıdaki güncelleme — DB kaynağı kapsam dışı
  bırakıldı).

**[Faz 4, 2026-09-01] Güncellendi — artık Confirmed**
- Gerçek ölçek: **~5000 desen görseli**, **tek (flat) klasörde**. Önceki
  ~1.000 tahmini **SUPERSEDED** (bkz. DECISIONS.md #32). Recursive alt
  klasör taraması şu an gerekli değil. Detay: `docs/PRODUCTION_REQUIREMENTS.md` §2.

---

## 4. Query (Arama) Görseli

**Confirmed**
- Aranan görsel, kayıtlı görselin birebir aynı dosyası olmak zorunda değil.
- Farklı çözünürlük, crop veya küçük görsel değişiklikler olabilir.

**Open Question**
- Aynı ürüne ait gerçek ikinci fotoğraflar şu anda yok. Bu nedenle PoC
  sırasında test amaçlı kontrollü/sentetik varyasyonlar (crop, resize,
  parlaklık vb.) kullanılması düşünülüyor — ama bu varyasyonların gerçek
  saha koşullarını ne kadar temsil ettiği bilinmiyor (bkz. ARCHITECTURE_PROPOSAL.md).

---

## 5. Platform

**Confirmed**
- Windows masaüstü uygulaması hedefleniyor.
- Sadece fabrika içinde kullanılacak.
- Merkezi fabrika veritabanı mevcut; SQL tabanlı olduğu düşünülüyor ama
  tam DBMS henüz bilinmiyor.
- **[Faz 3, 2026-09-01]** Uygulama dili/platformu: **C# / .NET**. Faz 1/2'de
  yazılan Python benchmark kodu silinmeyecek; model değerlendirme/engineering
  aracı olarak kalacak, ancak Lens masaüstü uygulamasının runtime'ı Python
  değil .NET olacak.

**Open Question**
- İleride birden fazla kullanıcı senaryosu var, ama eşzamanlılık/çoklu
  erişim gereksinimleri netleşmedi. Kısmi cevap: her kullanıcı/PC kendi
  local index/cache'ini tutacak (Recommended, bkz. DECISIONS.md #39).

**Kalıcı olarak kapsam dışı**
- Gerçek DB entegrasyonu — **SUPERSEDED**, görsel kaynağı artık yalnızca
  dosya dizini (bkz. DECISIONS.md #31).

---

## 6. Performans

**Confirmed**
- Arama sonucu tercihen 5 saniyeden uzun sürmemeli.
- Uygulama normal bir ofis bilgisayarında çalışacak; güçlü GPU garanti değil.
- **[Faz 1]** Local-first mimari onaylandı (PoC kapsamında dış AI servisi kullanılmayacak).
- **[Faz 1]** CPU performansı ayrıca ölçülecek (henüz ölçülmedi).

**Open Question**
- Dış AI servislerinin (cloud API vb.) kullanımına izin verilip verilmeyeceği
  yönetimle netleştirilmedi.

---

## 7. AI / Model

**Confirmed**
- Şu ana kadar konuşulan adaylar: CLIP, SigLIP.
- Hiçbir model henüz seçilmedi.
- "Daha yeni olduğu için SigLIP" gibi varsayımlar yapılmayacak; modeller
  gerçek veri üzerinde test edilerek karşılaştırılacak.
- **[Faz 1]** Benchmark'ın asıl amacı, iki modelden hangisinin **desen**
  benzerliğini daha iyi yakaladığını görmek (renk ikincil öncelik).
- **[Faz 1]** ~1.000 görsel ölçeğinde vector database kullanılmayacak;
  brute-force cosine similarity ile Top-5 sonuç dönülecek.

**Open Question**
- Aynı desen, farklı renk varyasyonu "aynı ürün" mü sayılmalı, yoksa farklı
  ürün mü? Bu netleşmediği için Faz 2 benchmark testlerinde **agresif
  renk/hue değişimi yapılmayacak** (yalnızca hafif/kontrollü fotometrik
  varyasyonlar kullanılacak).

**[Faz 2] Benchmark Sonucu (Tamamlandı — 2026-08-31)**
- 11 orijinal görsel + sentetik varyasyonlar (brightness, contrast, crop,
  downscale/upscale, jpeg quality), toplam 55 sorgu.
- Top-1 doğruluk: CLIP (openai/clip-vit-base-patch16) %98, SigLIP
  (google/siglip-base-patch16-224) %100.
- Top-3 ve Top-5 doğruluk: ikisinde de %100.
- CPU sorgu süresi: ~91ms/sorgu, iki modelde de eşit.
- Fark yalnızca `downscale_upscale` varyasyonunda görülüyor (CLIP %91,
  SigLIP %100); diğer tüm varyasyon tiplerinde ikisi de %100.
- Küçük ölçekli, gösterge niteliğinde bir ölçüm — istatistiksel genel
  geçerlilik iddiası yok. Detay: `benchmark/results/report.md`.
- **[Faz 3A, 2026-09-01] MVP model kararı: CLIP** (openai/clip-vit-base-patch16),
  Tech Lead/CTO onayı ile — **provisional/reversible**, production için kesin
  değil. Gerekçe: Top-5 ikisinde de %100, SigLIP avantajı yalnızca tek
  varyasyon tipinde ve küçük; CLIP'in ONNX/.NET entegrasyon riski daha düşük.
  Production için final model seçimi hâlâ açık (bkz. DECISIONS.md
  Not Yet Decided #1).

---

## 8. Geliştirme Ortamı (Bu Makine — Gözlem)

**Confirmed (read-only tespit)**
- İşletim sistemi: Windows 11 Pro (Build 26200), x64.
- Git: 2.55.0 kurulu, proje klasörü artık bir git repo (`git init` yapıldı).
- Python: 3.10.9 kurulu (`C:\Users\win11\AppData\Local\Programs\Python\Python310`).
  `pip` 26.2.1 mevcut.
- Bu geliştirme makinesinde bir NVIDIA GPU (RTX 5060 Ti, ~16 GB VRAM,
  driver 610.74) tespit edildi.
- **[Faz 3A, 2026-09-01]** Kontrol edildi: makinede hiç .NET SDK
  kurulu değildi. Faz 3A önkoşulu olarak `winget` ile **.NET 8 SDK**
  (`Microsoft.DotNet.SDK.8`, sürüm 8.0.424) kuruldu. Gereksiz/ekstra SDK
  sürümü kurulmadı.

**Önemli not**
- Bu makinede GPU bulunması, hedef fabrika/ofis bilgisayarlarında da GPU
  olacağı anlamına gelmez. Gereksinimde "güçlü GPU garanti değil" açıkça
  belirtildiği için, mimari CPU-only senaryoyu esas almalı; GPU varsa bonus
  hız kazancı olarak değerlendirilmeli.
