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

---

## FAZ 4C — Logging & Indexing Observability

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

---

## FAZ 4D — Top-10 & UI Polish

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

**Kabul kriterleri:**
- Top-10 sonuç ekranda scroll ile rahat görülebiliyor.
- İndeksleme sonrası kullanıcı en az: toplam/başarılı/yeni/değişmeyen/
  silinen/başarısız/unsupported/skipped sayılarını görebiliyor.
- Başarısız/unsupported dosyalar istenirse detay listesinde görülebiliyor.
- Geçerli bir görsel dosyası sürükle-bırak ile query olarak kabul ediliyor
  ve önizleme güncelleniyor.
- Klasör, birden fazla dosya veya desteklenmeyen format drop edildiğinde
  uygulama çökmüyor, anlaşılır bir mesaj gösteriliyor.

**Bağımlılık:** Top-10 kısmı bağımsız; indexing summary kısmı FAZ 4B/4C'nin
veri modeline bağımlı; drag & drop kısmı bağımsız (mevcut query-seçme
akışına paralel bir giriş noktası ekler).

---

## FAZ 4E — 5000-Image Real Dataset Validation

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

**Bağımlılık:** FAZ 4A-4D (tüm production sağlamlaştırmaları tamamlanmış
olmalı — aksi halde gerçek veri testi eksik bir sistemle yapılmış olur).
Ayrıca **dış bağımlılık:** gerçek ~5000 görsellik veri setine erişim
(bkz. Open Question, `PRODUCTION_REQUIREMENTS.md` §13).

---

## FAZ 4F — Production Publish / Factory Rollout

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
  raporlandı** (bypass edilmedi — bkz. `CLAUDE.md`, önceki oturum notları).

**Bağımlılık:** FAZ 4E (gerçek ölçek doğrulaması geçmeden rollout yapılmaz).

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
3. **4E'nin dış bağımlılığı açık bir risktir** — gerçek ~5000 görsellik
   veriye ne zaman erişileceği bilinmiyor (bkz. Open Questions). Bu,
   takvim riski olarak Tech Lead/CTO'ya ayrıca iletilmeli.
