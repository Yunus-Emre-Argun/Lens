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

**Confirmed (2026-09-01, FAZ 4A kickoff — önceden Recommended'dı)**
- Ürün dizini (paylaşılan UNC klasör) yalnızca görsel/desen dosyaları
  içerir.
- Her kullanıcı/PC kendi **local** index/cache'ini tutar —
  `%LocalAppData%\Lens\` altında (`config/`, `cache/`, `logs/` alt
  klasörleri; `logs/` bu fazda oluşturulur ama kullanılmaz). Bkz.
  DECISIONS.md #39, #45.
- Cache anahtarı: ürün dizini path'inden türetilen deterministic bir
  tanımlayıcı (hash) — farklı ürün dizinleri farklı cache kullanır, aynı
  dizine dönülünce eski cache tekrar kullanılır. Bkz. DECISIONS.md #44.
- Gerekçe: birden fazla Lens instance'ın aynı JSON dosyasına yazmaması,
  race condition/dosya bozulması riskinin azalması, paylaşılan klasörün
  Lens'e özel teknik dosyalarla kirlenmemesi, ağ üzerinde her aramada index
  I/O bağımlılığının ortadan kalkması.
- **Kabul edilen trade-off:** her yeni bilgisayarda ilk kullanımda ~5000
  görsel ayrı ayrı embed edilmek zorunda kalabilir (tahmini ~5 dakika, bkz.
  mimari görüş #5/#11). Bu MVP+ ölçeğinde kabul edilebilir bulunuyor.
  Merkezi/paylaşılan index yalnızca şu koşullardan biri gerçekleşirse
  yeniden değerlendirilmeli: (a) çok sayıda farklı istasyon aynı soğuk
  başlangıç maliyetini tekrar tekrar öder hale gelirse, (b) veri ölçeği
  ~5000'den bir büyüklük mertebesi daha artarsa (örn. 50.000+), veya
  (c) embedding süresi belirgin şekilde artarsa (daha büyük model vb.).

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
| SUPPORTED IMAGE BUT FAILED | `.jpg` uzantılı ama decode edilemedi (bozuk dosya) | Evet |
| UNSUPPORTED IMAGE FORMAT | `urun02.tif`, `.bmp`, `.webp` — bilinen görsel formatı ama şu an desteklenmiyor | Evet (ayrı, "unsupported" olarak) |
| NON-IMAGE / SKIPPED | `.lens_index.json`, `.txt`, `.csv` vb. | **Hayır** |

Gelecekte TIFF/BMP/WEBP desteği eklenmesi kolay olmalı (yalnızca
sınıflandırma/mesaj için "bilinen ama desteklenmeyen" listesi tutulur,
bugün gerçek decode desteği eklenmez).

**Implemented (FAZ 4B, 2026-09-01):** `FileClassification` enum'u ile
`SupportedImage` / `UnsupportedImageFormat` / `NonImage` olarak
sınıflandırılıyor; "SUPPORTED IMAGE BUT FAILED" durumu ayrı bir kategori
değil, `SupportedImage` dosyalarının işlenme aşamasında (embed edilirken)
oluşan bir sonuç olarak `IndexFileIssue.Kind` üzerinden izleniyor. Kavramsal
4'lü model korunuyor.

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

**Confirmed**
- **Top-10** gösterilecek (Top-5 yerine — SUPERSEDES eski karar #9).
- Arama algoritması (embedding + cosine similarity) **değişmiyor**.
- UI, 10 sonucu rahat gösterebilmeli (scroll/wrap grid — mevcut
  `WrapPanel` tabanlı sonuç grid'i zaten bunu destekleyecek şekilde
  genişletilebilir).

**Implemented (FAZ 4D, 2026-09-01):** `SimilaritySearch.TopK` çağrısı
10 olarak güncellendi; sonuç grid'i 5×2 `UniformGrid` düzeninde gösterilir.

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
  ROADMAP Faz 4E).
- Brute-force cosine similarity'nin ~5000 vektörde de yeterli olması
  **bekleniyor** (mimari görüş #11) ama gerçek veri üzerinde ölçülmeli.

**Out of Scope (bu faz setinde eklenmez)**
- Vector database, ANN index, merkezi index servisi, database, cloud,
  microservice, locking service.

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
| 1 | ~5000 gerçek görsel setine ne zaman erişilebilecek (FAZ 4E'nin bağımlılığı) | Gerçek ölçek doğrulaması bu veriye bağlı |
| 2 | Yönetici bilgisayarında/factory workstation'larında Smart App Control veya benzeri Application Control politikası aktif mi? | FAZ 4F rollout riski (bkz. DEMO_DEPLOYMENT_GUIDE.md §6) |

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

## 14. Later Phase (Bu Gereksinim Setinin Dışında)

- Login / kullanıcı yönetimi / yetkilendirme (production hedefinde hâlâ
  planlı, bkz. DECISIONS.md #16)
- Text search
- Raporlama, kullanıcı geçmişi
- Çoklu kullanıcı eşzamanlılık senaryoları (ötesinde detay)
- Recursive alt klasör taraması (şu an gerekli değil, ileride gerekirse
  değerlendirilir)
