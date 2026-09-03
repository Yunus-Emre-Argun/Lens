# Production Gereksinimleri — Lens

Bu doküman, **2026-09-01 tarihli yönetici görüşmesinden** çıkan yeni
gereksinimleri kaydeder. Bu görüşmeyle proje demo/PoC hedefinden, fabrikada
gerçekten kullanılacak bir masaüstü ürün hedefine geçmiştir.

Notasyon: **Confirmed** = yönetici tarafından açıkça belirtildi.
**Recommended** = Tech Lead mühendislik analizi, mimari olarak öneriliyor
ama henüz kullanıcı/CTO onayı almadı. **Open Question** = kod/mimari kararını
etkileyen, henüz cevaplanmamış soru. **Out of Scope** = bu faz setinde
yapılmayacak.

Bu doküman `docs/DECISIONS.md`'deki karar tarihçesini tekrar etmez; ilgili
karar numaralarına referans verir. Detaylı mimari tartışma ve gerekçeler
için bkz. `docs/ARCHITECTURE_PROPOSAL.md` (Faz 1, hâlâ geçerli genel
ilkeler) ve ilgili `docs/DECISIONS.md` / `docs/ROADMAP.md` maddeleri.

---

## 1. Ürün Kapsamı Değişikliği

**Confirmed**
- Yeni ürün/desen ekleme **Lens'in sorumluluğu değildir**. Yeni ürünler
  başka bir sistem/süreç tarafından ürün dizinine veya fabrikanın mevcut
  veri altyapısına eklenir.
- Lens'in görevi: mevcut ürün/desen görsellerini **okumak, indekslemek ve
  aramak**.
- Ürün CRUD, yeni ürün ekleme ekranı, DB'ye ürün yazma → artık hedef
  kapsamda **değil** (bkz. DECISIONS.md — SUPERSEDES eski karar #18).

---

## 2. Veri Kaynağı

**Confirmed**
- Görseller **doğrudan veritabanından okunmayacak**. Lens, görselleri bir
  **dosya dizininden** okuyacak (bkz. DECISIONS.md — SUPERSEDES eski karar
  #17'deki "fabrika DB" seçeneği).
- Gerçek ölçek: şu anda **~5000 desen görseli**, **tek (flat) klasörde**.
  Recursive alt klasör taraması **şu an gerekli değil** (SUPERSEDES eski
  ~1.000 tahmini, bkz. DECISIONS.md #11).

---

## 3. Ortak Dizin / Varsayılan Yol

**Confirmed**
- Kullanıcılar aynı ürün/desen dizinine erişecek (yönetici aynı zamanda
  system administrator, erişimi o sağlayacak).
- Birincil yaklaşım: **UNC network path** (örn. `\\FABRIKA-SERVER\Desenler`
  — gerçek path henüz bilinmiyor, **hardcode edilmeyecek**).
- Bu path bir **configuration/settings parametresi** olacak.
- Lens açıldığında varsayılan ürün dizini otomatik hazır gelmeli; mümkünse
  kullanıcı doğrudan indeksleme/arama akışına geçebilmeli.
- Kullanıcı isterse UI'dan başka bir dizin seçebilmeli (mevcut "Ürün
  Klasörü Seç" akışı zaten bunu sağlıyor).
- Varsayılan path **mutlak bağımlılık olmamalı**: erişilemiyorsa uygulama
  **crash olmamalı**; kullanıcı dostu durum mesajı ("Varsayılan ürün
  dizinine ulaşılamadı") gösterip manuel klasör seçme imkânı sunmalı.

**Later / Alternative (dokümante edilir, ana implementasyon değil)**
- **Mapped network drive** (örn. `Z:\...`) — yönetici bunu da tanımlayabilir.
  Şimdilik **alternatif/future configuration option** olarak dokümante
  edilir; ana implementasyon tercihi UNC'dir.

---

## 4. Ayarlar / Configuration

**Confirmed (2026-09-01, FAZ 4A kickoff)**
- Klasik `Properties.Settings`/`user.config` **kullanılmayacak** (gerekçe:
  `user.config` sürüm-bazlı path taşınma sorunu). Basit JSON config
  dosyaları, kendi `System.Text.Json` okuma/yazma kodumuzla — **ek NuGet
  bağımlılığı yok**.
- İki ayrı config ayrımı: **Admin default** (exe yanındaki, salt-okunur,
  IT tarafından elle düzenlenen dosya — `AdminDefaultProductDirectory`) ve
  **User override** (`%LocalAppData%\Lens\config\` altında, yazılabilir —
  `UserOverrideProductDirectory` + `UseUserOverride`). Bkz. DECISIONS.md #42.
- Kullanıcının manuel seçtiği klasör **varsayılan olarak geçicidir**
  (session-only); yalnızca kullanıcı açıkça "Bu klasörü varsayılan olarak
  kullan" derse kalıcı olur. Bkz. DECISIONS.md #43.
- Kesin dosya adları/şema FAZ 4A implementasyon planında netleşir.

---

## 5. Index / Cache Yerleşimi

**⚠️ SUPERSEDED (Faz 1, 2026-09-03) — bkz. DECISIONS.md #61-62.** Aşağıdaki
"her kullanıcı kendi local cache'ini tutar" modeli, gerçek kullanımda her
istasyonun ~5000 görseli ayrı ayrı embed etmesi gereken trade-off'u kabul
edilemez bulunduğu için **shared (paylaşılan) index** ile değiştirildi:

**Confirmed (Faz 1, 2026-09-03)**
- Canonical index artık **ürün dizininin kendi içinde**:
  `<ProductDirectory>/.lens/index.json` (UNC olabilir). Tüm kullanıcılar/PC'ler
  AYNI index dosyasını paylaşır — soğuk başlangıç maliyeti yalnızca BİR kez
  ödenir, istasyon başına değil.
- Eşzamanlı yazımı önlemek için tek-yazarlı exclusive dosya kilidi zorunlu:
  `<ProductDirectory>/.lens/index.lock` (`FileShare.None`, load→scan→
  update→save boyunca tutulur). Reader kilit almaz. Bkz. DECISIONS.md #62,
  `Lens.Core.Indexing.IndexLock`.
- Atomic save UNC için güçlendirildi: benzersiz temp dosya adı, hedef
  önceden silinmez, `File.Replace` desteklenmezse `Move(overwrite:true)`'a
  düşer. Bkz. DECISIONS.md #63.
- **Bilinen açık risk:** kilit-alamama nedeninin ("başka yazar tutuyor" vs.
  "farklı bir I/O sorunu") ayrımı yalnızca exception tipine dayanır ve
  gerçek UNC/SMB üzerinde henüz doğrulanmadı — bkz.
  `docs/PRODUCTION_CHECKLIST.md` "Codex Bulguları" (lock I/O hata ayrımı).
- `.lens` klasörü için IT/paylaşım izinleri: Lens kullanıcısının en az
  create/write/replace-rename/delete (temp cleanup) hakkına ihtiyacı vardır.
  Önerilen düzen: ürün görselleri genel **read**, `.lens` teknik klasörü
  kontrollü **write**.
- Config (`appsettings.json`), user settings ve loglar hâlâ
  `%LocalAppData%\Lens\` altında (bu faz DEĞİŞMEDİ) — yalnızca embedding
  index'i taşındı. Eski `%LocalAppData%\Lens\cache\<hash>\index.json`
  normal operasyonda kullanılmıyor, otomatik silinmiyor da.

**Eski (Faz 4A) model — artık normal operasyonda geçerli değil:**
- ~~Her kullanıcı/PC kendi **local** index/cache'ini tutar —
  `%LocalAppData%\Lens\` altında (`config/`, `cache/`, `logs/` alt
  klasörleri).~~ Bkz. DECISIONS.md #39 (superseded).
- ~~Cache anahtarı: ürün dizini path'inden türetilen deterministic bir
  tanımlayıcı (hash).~~
- Eski gerekçe (birden fazla Lens instance'ın aynı JSON dosyasına
  yazmaması) artık **exclusive lock ile** çözülüyor; "paylaşılan klasörün
  Lens'e özel teknik dosyalarla kirlenmemesi" endişesi `.lens/` alt
  klasörüyle (görünür ama izole, `.gitignore`'da da hariç tutulan bir
  runtime teknik klasörü) karşılanıyor.

---

## 6. Incremental Indexing

**Confirmed**
- Mevcut davranış korunuyor: ilk kullanımda tüm görseller embed edilir ve
  local persistent index/cache'e yazılır; sonraki çalıştırmalarda yalnızca
  yeni/değişmiş dosyalar embed edilir, silinen dosyalar index'ten çıkarılır.
- Mevcut metadata yaklaşımı (relative path + file size + LastWriteTimeUtc +
  embedding) bu ölçek (~5000) için **yeterli** kabul ediliyor. Daha ağır
  bir hash mekanizması şu an **gerekçelendirilmiyor** (bkz. mimari görüş #5).

---

## 7. Dosya Tarama / Format Sınıflandırması

**Confirmed**
- Yalnızca desteklenen **image formatları** (şu an: `.jpg`, `.jpeg`, `.png`)
  image pipeline'a gönderilir.
- Diğer dosyalar (`.json`, `.txt`, `.csv`, `.pdf` vb.) görsel olarak
  işlenmeye **çalışılmaz**.
- Tespit edilemeyen/desteklenmeyen görsel formatları kullanıcıya
  bildirilebilmeli.

**Recommended (mimari sınıflandırma — bkz. ROADMAP Faz 4B)**
Dört kategori:

| Kategori | Örnek | Hata sayılır mı? |
|---|---|---|
| PROCESSED | `urun01.jpg` başarıyla embed edildi | — |
| SUPPORTED IMAGE BUT FAILED | `.jpg` uzantılı ama decode edilemedi (bozuk dosya) veya boyut/çözünürlük sınırını aşıyor | Evet |
| UNSUPPORTED IMAGE FORMAT | `urun02.tif`, `.bmp`, `.webp` — bilinen görsel formatı ama şu an desteklenmiyor | Evet (ayrı, "unsupported" olarak) |
| NON-IMAGE FILE | `.pdf`, `.zip`, `.txt`, `.csv` vb. görsel olmayan dosya | Evet (ayrı, "desteklenmeyen dosya türü" olarak — kullanıcıya görünür, decode denenmez) |
| BİLİNEN ZARARSIZ (görünmez) | `.lens_index.json` (Lens'in eski artefaktı), `Thumbs.db`, `desktop.ini` | Hayır — sayaçlara/Issues'a hiç girmez |

Gelecekte TIFF/BMP/WEBP desteği eklenmesi kolay olmalı (yalnızca
sınıflandırma/mesaj için "bilinen ama desteklenmeyen" listesi tutulur,
bugün gerçek decode desteği eklenmez).

**Implemented (FAZ 4B, 2026-09-01):** `FileClassification` enum'u ile
`SupportedImage` / `UnsupportedImageFormat` / `NonImage` olarak
sınıflandırılıyor; "SUPPORTED IMAGE BUT FAILED" durumu ayrı bir kategori
değil, `SupportedImage` dosyalarının işlenme aşamasında (embed edilirken)
oluşan bir sonuç olarak `IndexFileIssue.Kind` üzerinden izleniyor. Kavramsal
4'lü model korunuyor.

**Güncellendi (kullanıcı geri bildirimi, 2026-09-02):** Görsel olmayan
dosyalar (`.pdf`, `.zip`, `.txt`, `.csv` vb.) artık sessizce yok
sayılmıyor — ürün klasörü esas olarak görsel içindir, bu yüzden
`FileIssueKind.NonImageFile` olarak Issues'a eklenir ve ana özet + "Sorunlu
/ Atlanan Dosyalar" penceresinde "Desteklenmeyen dosya türü" etiketiyle
görünür (decode yine DENENMEZ, indeksleme durmaz). Yalnızca Lens'in kendi
eski teknik artefaktı (`.lens_index.json`) ve Windows'un otomatik oluşturduğu
dosyalar (`Thumbs.db`, `desktop.ini`) hâlâ tamamen görünmez kalır
(`FileClassifier.KnownHarmless`).

---

## 8. Logging

**Confirmed**
- İki seviyeli görünürlük:
  - **A) Persistent log dosyası**: timestamp, severity, işlem, dosya
    adı/path, extension/format, hata mesajı, gerekiyorsa exception özeti.
  - **B) UI indexing summary**: toplam dosya, işlenebilir görsel sayısı,
    başarılı, yeni, değişmiş, değişmeyen, silinen, başarısız, unsupported
    format, skipped non-image. Başarısız/unsupported dosyalar için basit
    bir liste (dosya adı, format, hata nedeni) gösterilebilmeli — UI bir
    log görüntüleyiciye dönüşmemeli, basit bir "İndeksleme Detayları /
    Hatalar" alanı yeterli.

**Implemented (FAZ 4C, 2026-09-01):** Düz UTF-8 metin log formatı
seçildi. Kendi minimal implementasyonumuz (`ILensLogger`/`FileLogger`, ek
NuGet yok) kullanıldı. Dosya: `%LocalAppData%\Lens\logs\lens-yyyyMMdd.log`,
INFO/WARNING/ERROR seviyeleri, 30 günlük retention (açılışta eski dosyalar
temizlenir). Ana UI'daki indeksleme özeti sade tutuldu (yalnızca
yeni/güncellenen/silinen/sorun sayısı gibi kullanıcıya anlamlı bilgi);
ayrıntılı sayaçlar ve dosya bazlı hata listesi log dosyasında ve "Sorunlu
Dosyalar" penceresinde bulunur. Detay: `docs/DECISIONS.md` #50-51.

---

## 9. Arama Sonucu Sayısı

**⚠️ SUPERSEDED (Faz 1, 2026-09-03) — bkz. DECISIONS.md #60.** Sabit Top-N
yerine kullanıcının kontrol ettiği bir minimum benzerlik eşiği getirildi.

**Confirmed (Faz 1, 2026-09-03)**
- Kullanıcı arama öncesi **"Minimum benzerlik (%)"** girer (0-100, ondalık/TR
  virgül destekli, boş/negatif/>100/NaN/Infinity reddedilir).
- Pipeline: tüm skorlar hesaplanır → `score >= threshold` (inclusive,
  float-epsilon toleranslı) filtrelenir → azalan sıra → **en fazla 15**
  sonuç. Bkz. `Lens.Core.Search.SimilaritySearch.SearchWithThreshold`.
- Eşiği geçen sonuç yoksa hata DEĞİLDİR: önceki sonuç/seçim temizlenir,
  query görseli ve threshold girdisi korunur, sade durum mesajı gösterilir
  (modal yok).
- **OPEN PRODUCT DECISION:** threshold için kalıcı bir varsayım/default
  değer henüz onaylanmadı — alan boş başlar (bkz. DECISIONS.md Not Yet
  Decided #8).
- **Bilinen açık risk:** eşik karşılaştırmasındaki float-epsilon toleransı
  (`1e-4`) product owner onayı almamış teknik bir seçimdir; `%100` (kesin
  eşleşme) girildiğinde teknik olarak `%100` olmayan bir sonuç da "dahil"
  sayılabilir — bkz. `docs/PRODUCTION_CHECKLIST.md` "Codex Bulguları"
  (strict threshold epsilon).

**Eski (Faz 4D) davranış — artık geçerli değil:**
- ~~**Top-10** gösterilecek (Top-5 yerine).~~ `SimilaritySearch.TopK`
  çağrısı threshold'suz sabit-N modeliydi; UI 5×2 grid kullanıyordu.
  Sonuç grid'i artık en fazla 15 için 5×3 `UniformGrid` düzeninde.

---

## 10. UI İyileştirmeleri

**Confirmed**
- Mevcut sade UI genel olarak beğenildi; **büyük redesign, kompleks tema,
  animasyon, ağır UI framework YOK**.
- Değerlendirilecek küçük iyileştirmeler: daha iyi görsel hiyerarşi,
  sınırlı accent color, başarılı/hata durumları için anlaşılır renk
  kullanımı, Top-10 sonuç grid'i, indexing summary alanı.

**Implemented (FAZ 4D, 2026-09-01):** Accent renk (Ara butonu, seçili Top-10
kartı), başarı/uyarı renkli durum metni, sade bölüm başlıkları eklendi.
Ayrıca kabul sürecinde: "Yeni Arama" butonu, minimal "⋮" menü (Ayarlar/Log
Klasörünü Aç/Hakkında), çift-tıkla büyük görsel önizleme/zoom penceresi.
Detay: `docs/DECISIONS.md` #52-53.

---

## 11. AI / Arama Mimarisi

**Confirmed — değişmiyor**
- C# / .NET / WPF, CLIP ViT-B/16, ONNX Runtime, 512-dim normalized
  embedding, cosine similarity, persistent index/cache, incremental
  indexing.
- Mevcut doğrulama (188 aday, 55 query, C#: Top-1 %98.2 / Top-3 %100 /
  Top-5 %100, ~51 ms ortalama query) **umut verici ama ~5000 gerçek ürün
  üzerinde henüz ölçülmedi** — bu bir varsayımdır, kanıt değil (bkz.
  ROADMAP Faz 4F).
- Brute-force cosine similarity'nin ~5000 vektörde de yeterli olması
  **bekleniyor** (mimari görüş #11) ama gerçek veri üzerinde ölçülmeli.

**Out of Scope (bu faz setinde eklenmez)**
- Vector database, ANN index, merkezi index servisi, database, cloud,
  microservice.
- **Distributed** lock/queue/servis — Faz 1'de eklenen tek-yazarlı dosya
  kilidi (`.lens/index.lock`, bkz. §5) bunun yerine geçmez, minimal ve
  yalnızca tek paylaşılan klasör içindir.

---

## 12. Öncelik Sıralaması (Yönetici Görüşmesi)

1. Doğruluk
2. Veri kaybı / index bozulmasına dayanıklılık
3. Kullanıcıya doğru hata mesajı
4. Loglanabilirlik / troubleshooting
5. Network path sorunlarına dayanıklılık
6. Basit kullanım
7. Performans

---

## 13. Open Questions (Gerçekten Mimariyi Etkileyenler)

| # | Soru | Neden Önemli |
|---|------|---------------|
| 1 | ~5000 gerçek görsel setine ne zaman erişilebilecek (FAZ 4F'nin bağımlılığı) | Gerçek ölçek doğrulaması bu veriye bağlı |
| 2 | Yönetici bilgisayarında/factory workstation'larında Smart App Control veya benzeri Application Control politikası aktif mi? | FAZ 4G rollout riski (bkz. DEMO_DEPLOYMENT_GUIDE.md §6) |

**Çözüldü (FAZ 4A kickoff, 2026-09-01):** Override restart sonrası
hatırlanmıyor (session-only, bkz. DECISIONS.md #43); ayarlar dosyası
ayrımı ve konumu netleşti (bkz. DECISIONS.md #41-42, §4 yukarıda).

**Çözüldü (FAZ 4C, 2026-09-01):** Log formatı ve logging altyapısı netleşti
(bkz. DECISIONS.md #50, §8 yukarıda).

**Not:** Gerçek UNC path'in şu an bilinmemesi **blocker değildir** —
configurable olacağı için sonradan girilebilir. Mapped drive vs UNC
konusunda da belirsizlik yok: UNC birincil, mapped drive dokümante edilen
alternatif.

---

## 15. Otomatik İndeks Kontrolü (Faz 1, 2026-09-03)

**Confirmed**
- Ana ekranda "Arama öncesi indeksi otomatik kontrol et ve güncelle"
  checkbox'ı — varsayılan **açık**, tercih `user-settings.json`'da kalıcı
  (eski dosyada alan yoksa geriye uyumlu açık kabul edilir).
- Açıkken: index yok/boş/geçersizse "Ara" doğrudan oluşturur; index varsa
  30 sn freshness TTL + gerekirse incremental güncelleme (önceki
  search-before-refresh davranışıyla aynı, artık lock'lu).
- Kapalıyken: "Ara" hiçbir `DetectChanges`/`BuildOrUpdate`/yazım yapmaz,
  yalnızca bellekteki mevcut stable index ile arar; index yoksa arama
  başlamaz ("İndeksi Güncelle" yönlendirmesi gösterilir).
- Manuel "İndeksi Güncelle / Klasörü Tara" butonu checkbox'tan bağımsız
  her zaman force scan yapar. Bkz. DECISIONS.md #65.

## 16. Büyük/Aşırı Çözünürlüklü Görsel — Hard Limit Kaldırıldı (Faz 1, 2026-09-03)

**⚠️ SUPERSEDED (Faz 1) — bkz. DECISIONS.md #64.** Eski ~50 MB / ~50 MP
hard-rejection guard'ı (bkz. eski DECISIONS.md #58) **kaldırıldı**.

**Confirmed**
- Geçerli bir fabrika görseli artık **sadece büyük olduğu için
  reddedilmez**. Yeni sabit bir MB/MP reddetme eşiği de eklenmedi.
- Eşiğin üstündeki dosyalar decoder-seviyesi downsampling (ImageSharp
  `DecoderOptions.TargetSize`) ile ekonomik decode edilir — dosya kabul
  edilir, yalnızca tam çözünürlük belleğe alınmadan işlenir. Eşiğin
  altındaki (eskiden zaten kabul edilen) görseller ÖNCEKİ ile birebir aynı
  tam-çözünürlük yolunu kullanmaya devam eder (regresyon riski yok).
- Büyük önizleme/zoom penceresi de aynı mantıkla bounded `DecodePixelWidth`
  kullanır, reddetmez.
- Resilience korunuyor: dosya-seviyesi exception izolasyonu, decode hatası
  handling, Issue oluşturma, logging, last-known-good preservation — hiçbiri
  kaldırılmadı, yalnızca "sırf büyük diye reddet" davranışı kaldırıldı.

## 17. Later Phase (Bu Gereksinim Setinin Dışında)

- Login / kullanıcı yönetimi / yetkilendirme (production hedefinde hâlâ
  planlı, bkz. DECISIONS.md #16)
- Text search
- Raporlama, kullanıcı geçmişi
- Çoklu kullanıcı eşzamanlılık senaryoları (ötesinde detay)
- Recursive alt klasör taraması (şu an gerekli değil, ileride gerekirse
  değerlendirilir)
