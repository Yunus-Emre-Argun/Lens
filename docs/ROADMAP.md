# Roadmap — Lens (Faz 4: Production Hazırlığı)

Faz 1-3 (Python benchmark → C#/.NET WPF MVP → 188-aday stress test)
tamamlandı, bkz. `docs/DECISIONS.md`. Bu doküman, 2026-09-01 ikinci yönetici
görüşmesinden çıkan gereksinimleri (`docs/PRODUCTION_REQUIREMENTS.md`)
uygulamaya almak için izlenecek fazları tanımlar.

**Bu doküman henüz onaylanmadı — implementasyona başlamadan önce Tech
Lead/CTO onayı bekleniyor.**

Sıralama mantığı: önce temel depolama/config mimarisi (her şey buna bağımlı),
sonra sağlamlık (dosya tarama, hata sınıflandırma, atomic write), sonra
gözlemlenebilirlik (logging), sonra UI, sonra gerçek ölçek doğrulaması, en
son gerçek ortam dağıtımı. Öncelik sırası (`PRODUCTION_REQUIREMENTS.md` §12):
doğruluk → veri kaybı direnci → doğru hata mesajı → loglanabilirlik →
network dayanıklılığı → basit kullanım → performans.

---

## FAZ 4A — Configuration & Storage Architecture

**Amaç:** Index/cache'i ortak ağ klasöründen çıkarıp local'e taşımak,
varsayılan ürün dizini için configuration mekanizması kurmak. Diğer tüm
fazların temeli.

**Minimum işler (Confirmed, 2026-09-01 FAZ 4A kickoff — bkz. DECISIONS.md #39-45):**
1. `%LocalAppData%\Lens\` altında `config/`, `cache/`, `logs/` yapısı
   (`logs/` bu fazda oluşturulur, kullanılmaz).
2. Index/cache konumunu `%LocalAppData%\Lens\cache\<path-hash>\` altına
   taşı (mevcut `.lens_index.json` formatı/şeması değişmiyor — yalnızca
   konum değişiyor). Cache anahtarı: ürün dizini path'inin hash'i —
   aynı dizine dönülünce aynı cache kullanılır.
3. Index yazımını **atomic** yap (temp dosya + `File.Replace`/`Move`).
4. Admin default config (exe yanında, salt-okunur JSON — `appsettings.json`
   içinde `AdminDefaultProductDirectory`) ve user override config
   (`%LocalAppData%\Lens\config\` altında, yazılabilir —
   `UserOverrideProductDirectory` + `UseUserOverride`) **ayrı dosyalar**.
   Kendi `System.Text.Json` kodu, ek NuGet yok.
5. Uygulama açılışında: `UseUserOverride=true` ise user override, değilse
   admin default yüklenir → erişilemiyorsa kullanıcı dostu durum mesajı +
   manuel klasör seçme. Kullanıcının o oturumda seçtiği farklı klasör
   **diske yazılmaz** (session-only); "Bu klasörü varsayılan olarak kullan"
   ile açıkça kalıcı hale getirilebilir.

**Kabul kriterleri:**
- Aynı ürün dizini iki farklı local index konumuyla (örn. iki farklı test
  kullanıcı profili) sorunsuz çalışıyor, birbirini bozmuyor.
- Index dosyası yazımı sırasında process zorla sonlandırılırsa (simüle
  edilmiş kesinti), önceki geçerli index dosyası bozulmadan kalıyor.
- İki farklı ürün dizini arasında geçiş yapıldığında her biri kendi
  cache'ini kullanıyor; aynı dizine dönülünce eski cache tekrar kullanılıyor
  (yeniden embed edilmiyor).
- Kullanıcının geçici seçtiği klasör restart sonrası kayboluyor; "varsayılan
  yap" sonrası restart sonrası korunuyor; override manuel temizlenebiliyor.
- Config dosyası yoksa veya bozuksa uygulama crash olmuyor, boş/varsayılan
  durumda açılıyor.
- Varsayılan dizin erişilemez durumdayken (örn. var olmayan UNC path)
  uygulama crash olmuyor, açık bir durum mesajı gösteriyor.

**Bağımlılık:** Yok (ilk faz).

---

## FAZ 4B — Robust File Discovery & Indexing

**Durum: Tamamlandı ve kabul edildi (2026-09-01).**

**Amaç:** ~5000 dosyalık gerçek bir klasörde format sınıflandırmasını ve
hata toleransını sağlamlaştırmak.

**Minimum işler:**
1. Dosya tarama mantığını 4 kategoriye ayır: PROCESSED / SUPPORTED IMAGE
   BUT FAILED / UNSUPPORTED IMAGE FORMAT / NON-IMAGE-SKIPPED (bkz.
   `PRODUCTION_REQUIREMENTS.md` §7).
2. `IndexUpdateStats`'a (mevcut sınıf) bu kategorilerin sayaçlarını ve
   başarısız/unsupported dosyaların (dosya adı, format, sebep) listesini
   ekle.
3. "Bilinen ama desteklenmeyen" format listesi (`.tif`, `.tiff`, `.bmp`,
   `.webp`, `.gif`) — yalnızca sınıflandırma/mesaj için, decode desteği
   eklenmiyor.
4. Var olan incremental indexing mantığı (relative path + size +
   LastWriteTimeUtc) korunuyor — ~5000 ölçekte hâlâ yeterli kabul ediliyor.
5. **[Confirmed, 2026-09-01]** Search-before-refresh / index freshness:
   "Ara" butonuna basıldığında sistem cache'e körü körüne güvenmemeli —
   arama öncesi ürün dizininin freshness durumu kontrol edilmeli
   (yeni/değişmiş/silinmiş dosya var mı). Varsa: önce incremental index
   update, ardından query search çalışmalı. Kullanıcı kısa süre önce
   başarıyla "İndeksi Güncelle" yaptıysa aynı klasör gereksiz yere hemen
   tekrar taranmamalı (örn. son başarılı taramadan bu yana geçen süre/son
   tarama zaman damgası ile bir eşik kontrolü — kesin mekanizma bu fazda
   tasarlanacak). Amaç: kullanıcının manuel index güncellemeyi unutması
   yüzünden yeni ürünlerin aramada görünmemesini önlemek. Manuel "İndeksi
   Güncelle" butonu korunur.

**Kabul kriterleri:**
- `.lens_index.json` (veya benzeri teknik dosya) klasörde bulunsa bile
  hata olarak sayılmıyor, "skipped" olarak sınıflandırılıyor.
- Bozuk bir `.jpg` dosyası "failed" olarak işaretleniyor, tüm indeksleme
  sürecini durdurmuyor (mevcut per-file try/catch davranışı zaten bunu
  sağlıyor — genişletilecek).
- `.tif` gibi bilinen ama desteklenmeyen bir format "unsupported format"
  olarak ayrı sınıflandırılıyor (failed ile karıştırılmıyor).
- Klasöre yeni/değişmiş/silinmiş dosya eklenip "Ürün Klasörü Seç" akışı
  tekrarlanmadan doğrudan "Ara"ya basıldığında, arama güncel duruma göre
  sonuç veriyor (gerekirse otomatik incremental update tetiklenerek).
- Ardı ardına yapılan aramalar, her seferinde gereksiz yeniden tarama
  yapmıyor (kısa süre önce güncellenmişse atlanıyor).

**Bağımlılık:** FAZ 4A (local index konumu ve atomic write üzerine inşa edilir).

**Gerçekleşen sonuç (2026-09-01):** Dosyalar `SupportedImage` /
`UnsupportedImageFormat` / `NonImage` olarak sınıflandırılıyor;
`IndexUpdateStats` toplam/yeni/güncellenen/değişmeyen/silinen/desteklenmeyen
format/atlanan sayaçlarını ve `Issues` (dosya, format, sebep) listesini
taşıyor. Freshness TTL **30 saniye** olarak onaylandı: "Ara" öncesi metadata
tabanlı `DetectChanges` kontrolü yapılır; son başarılı taramadan/kontrolden
30 saniye geçmemişse tekrar taranmaz. Manuel "İndeksi Güncelle / Klasörü
Tara" her zaman force scan yapar ve TTL'i sıfırlar. Toplam tarama hatasında
(örn. dizin erişilemez) önceki geçerli index korunur, uygulama çökmez.

---

## FAZ 4C — Logging & Indexing Observability

**Durum: Tamamlandı ve kabul edildi (2026-09-01).**

**Amaç:** Persistent log dosyası + bu logun UI'da özetlenmesi için altyapı.

**Minimum işler:**
1. `%LocalAppData%\Lens\logs\` altında rolling log dosyası (öneri: günlük,
   plain text — bkz. Open Question, kesin format bu fazda netleşecek).
2. Minimal logging yardımcı fonksiyonu/sınıfı — yeni ağır bağımlılık
   eklenmeden (kendi kodu ya da çok hafif bir alternatif, bu fazda
   değerlendirilecek).
3. FAZ 4B'nin ürettiği sınıflandırma verisini (PROCESSED/FAILED/UNSUPPORTED/
   SKIPPED) log satırlarına yaz.

**Kabul kriterleri:**
- Bir indeksleme turu sonrası log dosyasında her başarısız/unsupported
  dosya için okunabilir bir satır var (timestamp, severity, dosya, format,
  sebep).
- Log dosyası büyüklüğü kontrolsüz büyümüyor (en azından günlük rotasyon).

**Bağımlılık:** FAZ 4B (loglanacak veri modelini üretir).

**Gerçekleşen sonuç (2026-09-01):** Kendi kodu ile minimal bir logging
katmanı eklendi (`ILensLogger` + `FileLogger`, ek NuGet yok). Düşük coupling
gereği indeksleme çekirdeği (`ImageIndex`) loglamadan habersiz bırakıldı;
log satırları çağıran katmanın (WPF) döndürülen istatistik/özet nesnelerinden
üretiliyor. Log dosyası: `%LocalAppData%\Lens\logs\lens-yyyyMMdd.log`,
düz UTF-8 metin, `yyyy-MM-dd HH:mm:ss [LEVEL] Operation | File: .. | Format:
.. | Reason: ..` formatında, INFO/WARNING/ERROR seviyeleri, 30 günlük
retention (uygulama açılışında eski dosyalar temizlenir). Loglama
başarısız olursa uygulama etkilenmez (tüm yazma işlemleri try/catch ile
korunur). Ana ekrandaki indeksleme özeti sadeleştirildi: yalnızca kullanıcı
için anlamlı bilgi (yeni/güncellenen/silinen/sorun sayısı, sıfır olan
kalemler gösterilmez) gösterilir; ayrıntılı teknik sayaçlar yalnızca log
dosyasında ve "Sorunlu Dosyalar" penceresinde bulunur.

---

## FAZ 4D — Top-10 & UI Polish

**Durum: Tamamlandı ve kabul edildi (2026-09-01).**

**Amaç:** Top-5 → Top-10 değişikliği + indexing summary'nin UI'da
gösterilmesi + sınırlı görsel iyileştirme.

**Minimum işler:**
1. `SimilaritySearch.TopK` çağrısını 5 → 10 olarak güncelle (algoritma
   değişmiyor). *Bu adım bağımsızdır, istenirse erken/ayrı yapılabilir.*
2. Sonuç grid'ini (mevcut `WrapPanel`) 10 sonucu rahat gösterecek şekilde
   genişlet (scroll zaten var).
3. Basit bir "İndeksleme Detayları / Hatalar" alanı — FAZ 4B/4C'nin
   ürettiği özet sayıları ve başarısız/unsupported dosya listesini gösterir.
   Log görüntüleyiciye dönüşmez, sade kalır.
4. Sınırlı görsel iyileştirme: hiyerarşi, accent color, başarı/hata durum
   renkleri. Büyük redesign/animasyon yok.
5. **[Confirmed, 2026-09-01]** Query görseli için drag & drop: mevcut
   "Sorgu Görseli Seç" butonu korunur; buna ek olarak kullanıcı query
   görselini sorgu/preview alanına sürükleyip bırakabilir. WPF native
   drag-and-drop (yeni UI framework/dependency yok). Davranış: yalnızca
   jpg/jpeg/png kabul edilir; desteklenmeyen dosyada kullanıcı dostu mesaj;
   klasör drop edilirse kabul edilmez; birden fazla dosya bırakılırsa
   "Lütfen tek bir görsel bırakın" mesajı; başarılı drop sonrası preview
   güncellenir; dosya seçme butonu fallback olarak çalışmaya devam eder.
6. **[Confirmed, 2026-09-01]** Sonuç ekranı düzeni — query/karşılaştırma
   alanı: query görseli üstte, mevcut halinden **daha büyük** ve **sürekli
   görünür**. Top-10 sonuçlar altta grid/list olarak **sürekli görünür**
   (hiçbir zaman kaybolmaz). Kullanıcı Top-10'dan bir sonuca tıklarsa: ana
   query görseli değişmez; tıklanan sonuç, query'nin yanındaki ayrı bir
   "karşılaştırma" alanında büyük gösterilir + similarity score yazılır;
   başka bir sonuca tıklanırsa yalnızca karşılaştırma alanı güncellenir;
   seçili Top-10 kartı hafif border/highlight ile işaretlenebilir. Amaç:
   kullanıcı 10 aday arasında dolaşırken ana sorgu desenini kaybetmeden
   yan yana karşılaştırabilsin. Örnek düzen:
   ```
        Sorgulanan Görsel      Seçilen Sonuç
        ┌──────────────┐      ┌──────────────┐
        │    QUERY     │      │    RESULT    │
        └──────────────┘      └──────────────┘
                               Similarity %...
   ------------------------------------------------
                En Benzer 10 Sonuç
    [1] [2] [3] [4] [5]
    [6] [7] [8] [9] [10]
   ```

**Kabul kriterleri:**
- Top-10 sonuç ekranda scroll ile rahat görülebiliyor.
- İndeksleme sonrası kullanıcı en az: toplam/başarılı/yeni/değişmeyen/
  silinen/başarısız/unsupported/skipped sayılarını görebiliyor.
- Başarısız/unsupported dosyalar istenirse detay listesinde görülebiliyor.
- Geçerli bir görsel dosyası sürükle-bırak ile query olarak kabul ediliyor
  ve önizleme güncelleniyor.
- Klasör, birden fazla dosya veya desteklenmeyen format drop edildiğinde
  uygulama çökmüyor, anlaşılır bir mesaj gösteriliyor.
- Top-10 sonuçlarından birine tıklanınca query görseli sabit kalıyor,
  yalnızca karşılaştırma alanı (görsel + similarity score) değişiyor;
  Top-10 listesi hiçbir adımda ekrandan kaybolmuyor.

**Bağımlılık:** Top-10 kısmı bağımsız; indexing summary kısmı FAZ 4B/4C'nin
veri modeline bağımlı; drag & drop kısmı bağımsız; query/karşılaştırma
düzeni Top-10'un kendisine bağımlı (aynı fazda, ondan hemen sonra
yapılması mantıklı).

**Gerçekleşen sonuç (2026-09-01):** Yukarıdaki tüm maddeler uygulandı;
ayrıca kabul sürecinde şu ek iyileştirmeler eklendi:
- **"Yeni Arama" butonu**: query/karşılaştırma/Top-10'u temizler, ürün
  klasörünü/index'i/cache'i değiştirmez ("geri" kavramı yok, tek ekranlı
  akışa uygun).
- **Minimal "⋮" menü** (sağ üst köşe): Ayarlar (salt-okunur özet — mevcut
  klasör/kaynak/config yolları; klasör değiştirme işlemleri hâlâ ana
  ekrandaki mevcut butonlarla yapılır, Faz 4A mimarisi değişmedi),
  Log Klasörünü Aç (`%LocalAppData%\Lens\logs\` Explorer'da açılır, log
  içeriği uygulama içinde gösterilmez), Hakkında (sade sürüm bilgisi).
- **Büyük görsel önizleme/zoom**: query görseli, karşılaştırmadaki seçilen
  sonuç ve Top-10'daki tüm sonuçlar çift tıklamayla ayrı bir
  `ImagePreviewWindow` içinde açılır (fare tekerleği ile zoom, sürükleyerek
  pan, çift tık ile sığdırmaya sıfırlama, ESC ile kapatma). Aynı anda tek
  önizleme açık kalır (yenisi açılınca öncekisi kapanır). Pencere gerçek
  görsel çözünürlüğüne göre değil, ana ekrandaki küçük önizleme kutusundan
  "biraz daha büyük" bir varsayılan boyutla açılır — ekranı kaplamaz;
  kullanıcı isterse pencereyi büyütebilir veya zoom yapabilir. Top-10
  kartlarında tek tık (seçim) ve çift tık (önizleme) birbirini bozmadan
  birlikte çalışır. Dosya silinmiş/erişilemezse (UNC ağ klasörü) sade bir
  hata mesajı gösterilir, ana pencere etkilenmez.

**Son görsel geri bildirim turu (2026-09-02):**
- Sürükle-bırak sırasında `QueryDropZone`'da accent border + hafif arka
  plan tonu + "Görseli buraya bırak" metni; `DragLeave`/`Drop` sonrası
  normale döner; geçersiz dosyada yanlış "kabul edilebilir" görünümü
  verilmez.
- Karşılaştırma alanında görüntülenen benzerlik **tam %100.0** ise yeşil
  vurgu (gösterilen/yuvarlanmış metin karşılaştırılır, ham `double == 1.0`
  değil).
- Sorgulanan görsel paneli her zaman accent renkli/2px border ile
  belirginleştirilmiş durur (karşılaştırma paneli nötr gri/1px kalır) —
  kullanıcı ilk bakışta "arattığım görsel bu" diye ayırt edebilir.
- Query, karşılaştırma sonucu ve Top-10 kartlarındaki dosya adları
  salt-okunur ama seçilebilir/kopyalanabilir metin kutusu (I-beam imleç,
  fare ile seçim, Ctrl+C, sağ-tık "Kopyala"). Top-10 kartlarında dosya adı
  butonun **dışına** taşındı — metin seçimi artık kartın tek-tık seçim
  davranışını bozmuyor; çift-tık önizleme thumbnail üzerinde korunuyor.
- Sürükleme sırasında küçük (64px), yarı saydam bir thumbnail fareyi takip
  eder (native WPF `Adorner`, `IsHitTestVisible=false` — WPF'in drag-event
  hit-testini bozmaz, yeni dependency yok). Windows Explorer'ın kendi
  sürükleme "ghost"u Lens penceresine girince kaybolduğu için eklendi;
  `DragEnter`'da bir kez yüklenir, `DragOver`'da yalnızca pozisyonu
  güncellenir, `DragLeave`/`Drop`'ta hemen kaldırılır.
- **Bilinen sınırlama:** gerçek OS-seviyesi sürükleme davranışı (drag
  preview'ın gerçekten fareyi takip etmesi) otomasyonla güvenilir test
  edilemedi; kod incelemesiyle doğrulandı, manuel doğrulama gerekiyor.

---

## FAZ 4E — Reliability Hardening (Codex Audit Follow-up)

**Durum: Tamamlandı ve kabul edildi (2026-09-02).**

**Amaç:** Codex tarafından yapılan bir production-audit sonrası tespit edilen
4 dayanıklılık maddesini kapatmak. FAZ 4B'nin per-dosya hata toleransı
mimarisi üzerine inşa edilir, yeni bir mimari kavram eklemez.

**Yapılan işler:**
1. **Geçici hata ≠ gerçek silme:** `ImageIndex.BuildOrUpdate`, bir dosyanın
   metadata okuma veya embed adımı GEÇİCİ bir nedenle (network kesintisi,
   dosya kilidi, izin sorunu) başarısız olduğunda — dosya directory
   taramasında hâlâ görünüyorsa — o dosyanın önceki SAĞLAM kaydını sonuca
   aynen taşır; "removed" saymaz. Yalnızca directory snapshot'ında hiç
   görünmeyen dosyalar gerçekten silinmiş sayılır.
2. **Bozuk/uyumsuz cache recovery:** `ImageIndex.Load`, bozuk/yarım JSON,
   deserialize hatası, null/eksik alan, embedding boyutu CLIP'in beklediği
   512'den farklı, veya embedding içinde NaN/Infinity içeren bir cache
   dosyasını "hepsi ya da hiçbiri" politikasıyla tamamen güvensiz sayar:
   uygulama çökmez, cache'i yok sayıp boş listeyle döner; çağıran taraf
   bunu normal "index yok, yeniden oluştur" durumu gibi ele alır.
3. **UNC/network UI freeze/crash azaltma:** Uygulama açılışındaki varsayılan
   dizin çözümlemesi ve "İndeksi Güncelle" öncesi ön-kontrol artık UI
   thread'ini bloklamıyor (`Task.Run` + try/catch); olası bir erişim hatası
   kullanıcı dostu bir durum mesajına çevrilir, "busy" durumu her çıkış
   yolunda düzgün sıfırlanır, exception hiçbir `async void` event handler'dan
   kontrolsüz kaçmaz.
4. **Büyük/aşırı çözünürlüklü görsel resource guard:** Tek bir kontrol
   noktasından (`Lens.Core.Ai.ImageResourceLimits`) dosya boyutu (~50 MB) ve
   piksel sayısı (~50 MP) sınırı; tam piksel decode'undan ÖNCE (ImageSharp
   `Image.Identify` ile, yalnızca header okunur) kontrol edilir. Bu guard
   indexing, query embedding ve büyük önizleme/zoom'un ÜÇÜNÜN de ortak geçiş
   noktasında (`ImagePreprocessor.PreprocessToChwTensor`) ve büyük önizleme
   penceresinin tam-çözünürlük yükleyicisinde çalışır — bypass yoktur.
   Limiti aşan bir ürün görseli embed edilmez, "Görsel boyutu desteklenen
   sınırı aşıyor" mesajıyla Issue olarak kaydedilir; eski sağlam kaydı varsa
   (2) numaralı maddedeki AYNI mekanizmayla korunur. Query tarafında görsel
   seçilir seçilmez (arama başlamadan) reddedilir.

**Kabul kriterleri:** Kendi konsol test aracına (`Lens.AiProof hardeningtest`
modu) eklenen fonksiyonel testlerle doğrulandı — gerçek dosya kilidi, bozuk
JSON, NaN/Infinity embedding, 511/513 boyutlu embedding, sentetik 60MP
görsel, >50MB dosya ve PDF/ZIP/TXT senaryolarının tümü otomatik test
kapsamındadır (29/29 PASS). `dotnet build` Debug ve Release'de 0 warning/0
error. UNC/network maddesi kod incelemesiyle doğrulandı (gerçek bir UNC pay
üzerinde manuel doğrulama hâlâ önerilir — bkz. yönetici bilgisayarı
checklist'i).

**Bağımlılık:** FAZ 4A-4D (mevcut indexing/cache/UI mimarisi üzerine inşa
edilir, yeni bir mimari karar eklemez).

**Bilerek bu fazın kapsamı dışında bırakılanlar** (ileride gerçekten gerekli
görülürse ayrıca değerlendirilecek): model/preprocessing cache versioning,
cache içeriğinin SHA-256 ile doğrulanması, aynı anda birden fazla Lens
örneğine karşı dosya kilidi/mutex, atomic-write'a ek "backup dosyası"
geliştirmesi, boyut+zaman damgası yerine tam dosya hash'i, log
gizliliği/redaction refactor'ü, `MainWindow`'un büyük refactor'ü/MVVM
dönüşümü, bağımlılık/.NET sürüm yükseltmeleri, code signing, installer/MSI,
vector database.

---

## FAZ 4F — 5000-Image Real Dataset Validation

**Amaç:** Şu ana kadarki tüm varsayımları (brute-force performansı, index
boyutu/süresi, doğruluk) gerçek ~5000 görsellik veri üzerinde ölçmek.

**Minimum işler:**
1. Gerçek (veya gerçeğe en yakın erişilebilir) ~5000 görsellik veri setiyle
   ilk indeksleme süresini, index dosya boyutunu, bellek kullanımını ölç.
   Mevcut C# stress-test aracı (`Lens.AiProof stresstest` modu) bu ölçek
   için genişletilebilir.
2. Query süresini (embed + cosine similarity + sort) ~5000 aday havuzunda
   ölç, 5 saniye hedefiyle karşılaştır.
3. Mümkünse gerçek/gerçeğe yakın query görselleriyle Top-1/3/10 doğruluğunu
   gözlemle (mevcut sentetik varyasyon seti + varsa gerçek örnekler).
4. Sonuçları `benchmark/results/` altında raporla — "1000/5000'de garanti
   çalışır" iddiası değil, "ölçülen davranış" olarak.

**Kabul kriterleri:**
- İlk indeksleme süresi ölçüldü ve makul kabul edildi (ör. birkaç dakika
  mertebesinde, kullanıcıya progress gösterilerek).
- Query süresi 5 saniye hedefinin belirgin altında.
- Brute-force cosine similarity'nin bu ölçekte yeterli olduğu (veya
  olmadığı) somut sayılarla doğrulandı.

**Bağımlılık:** FAZ 4A-4E (tüm production sağlamlaştırmaları tamamlanmış
olmalı — aksi halde gerçek veri testi eksik bir sistemle yapılmış olur).
Ayrıca **dış bağımlılık:** gerçek ~5000 görsellik veri setine erişim
(bkz. Open Question, `PRODUCTION_REQUIREMENTS.md` §13).

---

## FAZ 4G — Production Publish / Factory Rollout

**Amaç:** Self-contained publish'i gerçek fabrika ortamına (UNC path,
gerçek workstation) taşımak.

**Minimum işler:**
1. Gerçek UNC path config'e girilerek uçtan uca test.
2. Hedef workstation'da self-contained publish çalıştırma doğrulaması
   (bkz. `DEMO_DEPLOYMENT_GUIDE.md` §6 — Application Control riski burada
   kontrol edilmeli).
3. Gerekirse mapped drive (`Z:\...`) senaryosunun da manuel doğrulanması
   (ana yaklaşım UNC, ama alternatif olarak çalıştığı teyit edilmeli).

**Kabul kriterleri:**
- Gerçek fabrika workstation'ında uygulama açılıyor, varsayılan dizin
  yükleniyor (veya düzgün fallback gösteriyor), arama uçtan uca çalışıyor.
- Application Control/güvenlik politikası engeli varsa **tespit edilip
  raporlandı** (bypass edilmedi — bkz. `CLAUDE.md`, `DEMO_DEPLOYMENT_GUIDE.md` §6).

**Bağımlılık:** FAZ 4F (gerçek ölçek doğrulaması geçmeden rollout yapılmaz).

---

## Faz Sıralamasına Dair Not (Senior Engineer Değerlendirmesi)

Yöneticinin önerdiği 6 fazlı iskelet büyük ölçüde korunmuştur; üç küçük
gözlem eklendi:

1. **4B ve 4C sıkı bağlıdır** — dosya sınıflandırması (4B) hem log
   satırlarının (4C) hem de UI özetinin (4D) veri kaynağıdır. Ayrı fazlar
   olarak tutuldu (küçük/gözden geçirilebilir değişiklik ilkesi gereği)
   ama aynı veri modeli üzerinde çalışıldığından art arda ve kesintisiz
   yapılmaları önerilir.
2. **4D'nin Top-10 kısmı bağımsızdır** — `TopK` çağrısında `5` yerine `10`
   geçmek ve grid'i genişletmek, indexing-summary UI'sından bağımsız,
   istenirse çok daha erken/ayrı yapılabilir. İndexing-summary kısmı ise
   4B/4C'nin verisine bağımlı olduğu için o sırada kalmalı.
3. **4F'nin dış bağımlılığı açık bir risktir** — gerçek ~5000 görsellik
   veriye ne zaman erişileceği bilinmiyor (bkz. Open Questions). Bu,
   takvim riski olarak Tech Lead/CTO'ya ayrıca iletilmeli.
