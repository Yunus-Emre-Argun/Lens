# Proje Bağlamı — Lens

Bu doküman, projenin şu ana kadar konuşulmuş gereksinimlerini ve kısıtlarını
kaydeder. Amaç: gelecekteki çalışma turlarında ve mimari toplantıda ortak bir
referans noktası olmak.

Notasyon: **Confirmed** = kullanıcı tarafından açıkça belirtildi.
**Open Question** = henüz netleşmedi, karar bekliyor.
**Later Phase** = ilk PoC/MVP kapsamı dışında, ileride değerlendirilecek.

## Güncel Devir Notu — 2026-09-04 (Dokümantasyon Kontrol Noktası)

**Kapsam:** Kullanıcının açık talimatıyla bu tur yalnızca dokümantasyon
commit/push işlemidir. Claude bu kayıt hazırlanırken tasarım kodunu bitirip
görsel onaya geçti; aşağıdaki yeni kaynak kod değişiklikleri bu doküman commit'ine
DAHİL DEĞİLDİR. Bu kayıt, kod yedeği veya yeni bir teslim paketi değildir.
Sonraki tur başlamadan `git status` ve son commit geçmişi tekrar okunmalıdır.

### Tamamlanan / devam eden iş ayrımı

- **Git geçmişindeki son uygulama değişikliği:** `c1f15ab` — sonuç başlığında
  gerçekten listelenen kart sayısı. Önceki turlarda altı tema, içeriğe göre
  kart yüksekliği, yeni sorguda kaydırmanın başa dönmesi, eski sonuçların
  erken temizlenmesi, arama sırasında sorgu kilidi ve kullanıcı tarafından
  seçilen 1-200 sonuç sınırı eklendi (kararlar #67-74).
- **Çalışma kopyasında, henüz kaynak kod commit'i yapılmamış:** ilk yerleşim
  düzenlemesi; başarılı tarama yokken istatistik satırının gizlenmesi;
  yeni Ayarlar penceresi; adres kutusunda taslak/Enter/Esc akışı; arama
  varsayılanlarının 80/20 olması (kararlar #75-76). Bunlar bu dokümanların
  GitHub'a gönderilmesiyle uygulama kaynağının parçası hâline GELMEZ.
- **Görsel onay bekliyor:** ilk yerleşim kullanıcı taslağını yeterince
  karşılamadığı için yapılan ikinci tasarım düzenlemesini Claude tamamladığını
  raporladı (karar #77). Aşağıdaki ölçüler onaylı HEDEFTİR; bu turda
  uygulamanın bunlara görsel olarak uyduğu bağımsız doğrulanmadı.
- **Doğrulama:** Claude'un ikinci tasarım turundan ÖNCEKİ raporunda
  `Lens.AiProof hardeningtest` **158/158 PASS**, Debug/Release **0 warning /
  0 error**. Son tasarım raporunda ayrıca Release **0 warning / 0 error**
  ve **158/158 PASS** bildirildi; Debug açık uygulama nedeniyle kilitli,
  henüz doğrulanamadı. Bu sonuçlar görsel kabulü veya aşağıdaki ertelenen
  sorunların çözüldüğünü kanıtlamaz. Bu dokümantasyon turunda yeni
  uygulama derlemesi, model testi veya canlı ekran otomasyonu yapılmadı.

### Confirmed — son tasarım hedefi (karar #77)

- Yalnızca yerleşim düzenlenecek. Referans çizimde kısa yazılmış buton
  adları yeni gereksinim değildir. Tek açık isim değişikliği:
  **"Ürün Klasörü Seç" → "Tarama Klasörünü Seç"**; Ayarlar'daki aynı butona
  yönlendiren metin de tutarlı olacak. "SORGU VE KARŞILAŞTIRMA" başlığı yok.
- Üst alan iki satır: klasör seçimi + yaklaşık 340 DIP adres kutusu
  (en fazla 360) + ürün sayısı/kaynak; altında indeksleme ve durum bilgisi.
  Varsayılan klasör kontrolleri pencerenin en sağ ucuna itilmeden dengeli
  yerleşecek. "Sorunlu / Atlanan Dosyalar", "Bu Klasörü Varsayılan Yap"
  butonunun doğrudan altında, aynı SOL kenar hizasında olacak; koşullu
  görünürlük korunacak. Ortak sütunlarla hizalama tercih edilecek.
- Orta alan TEK grup olarak ortalanacak: **sorgulanan görsel → dikey arama
  ayarları → Ara/Yeni Arama → seçilen sonuç**. İki görsel ne birbirine
  yapışacak ne de pencerenin iki ucuna gidecek. Fazla genişlik dış yanlara
  dengeli dağılacak; orta ayarlar rahat okunur ve ferah olacak.
- İki çerçeve eşit ve **KARE**, geniş pencerede yaklaşık **300×300 DIP**.
  Taslaktaki yatay görünüm, yatay çerçeve uygulama talimatı DEĞİLDİR.
  Görselin kendi en-boy oranı korunacak (`Uniform`); kırpma, esnetme,
  döndürme yok. Beyaz görsel yüzeyleri, seçim vurgusu, sürükle-bırak ve
  çift tıklayarak büyütme davranışı korunacak.
- Geniş pencere için hedefler: ayar sütunu yaklaşık 260 DIP, girişler
  84×36, sorgu seçme butonu 260×42, Ara/Yeni Arama 120×42, form yazıları
  yaklaşık 14 DIP. Gruplar arası boşluklar yaklaşık 40 / 32 / 40 DIP.
  Sorgu seçimi görsellerin üst kenarına yakın başlayacak; **Ara minimum
  benzerlik satırıyla, Yeni Arama en fazla sonuç satırıyla hizalanacak**.
  Ayarlar ve eylemler ortak satırları paylaşacak.
- Üst alan ile karşılaştırma arasında yaklaşık 28-36 DIP, karşılaştırma
  ile sonuç listesi arasında 16-24 DIP boşluk hedefleniyor. Durum metinleri
  12-13 DIP civarında okunabilir kalacak. Bunlar geniş pencere hedefleri;
  dar pencerede taşmaya sebep olacak sabit zorlamalar yapılmayacak.
- Açılış boyutu 1060×840 ve minimum 860×680 korunacak. Dar görünümde
  görseller yaklaşık 200-220 DIP'e, boşluklar 16-24 DIP'e küçülebilecek;
  ayar sütunu yaklaşık 240, eylem sütunu 104 DIP olacak. Kontroller
  kesilmeyecek, sonuçların kaydırılabilir alanı kullanılabilir kalacak.
- Tema renkleri, beş sütunlu sonuç kartları, başlıktaki gerçek sayı,
  arama/indeksleme mantığı, kilitler, doğrulama ve kaydırma sözleşmeleri
  değiştirilmeyecek. Minimum benzerlik varsayılanı 80; sonuç tercihi yoksa
  20, mevcut geçerli kayıtlı tercih varsa o değer; boş alanlarda 80/20.
  Otomatik indeksleme yeni/eksik ayarda açık; kayıtlı kapalı tercih korunur.

### Kullanıcı kararıyla ertelenenler — ÇÖZÜLDÜ DEĞİL

Tasarım önceliklendirildiği için aşağıdakilere bu turda kod müdahalesi
yapılmayacak; sonraki görevde ele alınacak:

1. **Ayarlar:** mevcut `SettingsWindow`, teknik yolları kapalı bir "Teknik
   ayrıntılar" bölümünde hâlâ gösteriyor. Bu, teknik ayrıntı bölümü olmadan
   sade Ayarlar beklentisiyle uyuşmuyor; gizli başlaması sorunu kapatmıyor.
2. **Adres normalizasyonu:** tam adres denetimi ve sürücü kökünün korunması
   ayrıca düzeltilip test edilmeli. Mevcut biçim testleri tüm kök/göreli
   adres sınır durumlarını kapsamıyor; "tüm göreli yollar reddedilir"
   şeklinde kesin güvence verilmemeli.
3. **Klasör değişiminde durum tutarlılığı:** yükleme başarısızlığında önceki
   klasör/index/sonuçların korunması ve aynı klasör yeniden uygulandığında
   varsayılan kaynağının değişmemesi henüz sağlanmış kabul edilemez.
   Başlangıç yüklemesiyle yarış durumları da uçtan uca doğrulanmalı.

### Devralan için sonraki adım / teslim sınırı

Önce son tasarımın farkları gözden geçirilecek; kullanıcı uygulamayı uygun
olduğunda kapattıktan sonra Debug derlemesi doğrulanacak. Kaynak tekrar
değişirse build ve ilgili headless testler yeniden raporlanacak. Görsel
onay kullanıcıda: normal/minimum boyut, hizalar, büyük iki görsel, taşma ve
sürükle-bırak/büyütme kontrolü. **Kullanıcı bilgisayarı aktif kullanırken
uygulama açma/kapatma, tıklama/klavye otomasyonu yapılmayacak**; canlı test
gerekirse kullanıcıdan kontrol istenecek. Kullanıcı onayı olmadan mevcut
ayarları veya test dışı ürün dosyalarını değiştirmek yok.

Bu tur publish/ZIP/Drive yüklemesi içermez. Mevcut publish paketlerinin
devam eden değişiklikleri içerdiği varsayılmamalı. Kaynak, test, commit ve
yeniden üretilen teslim paketi eşleştirilmeden "son sürüm hazır" denmeyecek.
Gerçek ölçek/fabrika kabulü ve lisans/model doğrulaması gibi mevcut açık
konular ayrıca `docs/PRODUCTION_CHECKLIST.md` ve `docs/MODEL_CARD.md`'de
izlenmeye devam eder; bu tasarım turu onları kapatmaz.

**Tarihsel kayıt notu:** Aşağıdaki ilk dönem gereksinimleri korunmuştur.
Eski sonuç sayısı/index konumu gibi kararların güncel karşılıkları için
`docs/DECISIONS.md` ve `CHANGELOG.md` esas alınmalıdır.

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
