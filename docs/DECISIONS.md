# Kararlar — Lens

Bu doküman alınmış ve henüz alınmamış mimari/teknik kararları ayrı ayrı listeler.
Alınmamış kararlarda kesin hüküm verilmez; alternatifler `ARCHITECTURE_PROPOSAL.md`
içinde tartışılır.

---

## Confirmed (Alınmış Kararlar)

| # | Karar | Not |
|---|-------|-----|
| 1 | Proje klasörü git ile versiyonlanacak | `git init` proje başlangıcında yapıldı |
| 2 | İlk PoC kapsamı yalnızca görselle arama | Text search, login vb. dahil değil |
| 3 | İlk demo, klasördeki mevcut görsellerle çalışabilir | Gerçek DB entegrasyonu ilk demo için zorunlu değil |
| 4 | Hedef platform Windows masaüstü | Fabrika içi kullanım |
| 5 | Model karşılaştırması varsayıma değil ölçüme dayanacak | CLIP/SigLIP arasında moda/yenilik gerekçesiyle seçim yapılmayacak |
| 6 | **[Faz 1]** PoC mimarisi local-first | Dış AI servisi kullanılmayacak (PoC kapsamında) |
| 7 | **[Faz 1]** Uygulama dili Python | — |
| 8 | **[Faz 1]** Görsel kaynağı klasör tabanlı | Gerçek DB entegrasyonu sonraki faz |
| 9 | **[Faz 1]** Pipeline: görsel → embedding → cosine similarity → Top-5 sonuç | Önceki "5-10 aday" ifadesi Top-5 olarak netleşti. **⚠️ SUPERSEDED — bkz. #35 (Top-10)** |
| 10 | **[Faz 1]** Nihai kararı kullanıcı verir | Sistem kesin eşleşme iddia etmez |
| 11 | **[Faz 1]** ~1.000 görsel ölçeğinde vector database kullanılmayacak, brute-force arama yapılacak | ARCHITECTURE_PROPOSAL.md Bölüm 10'daki tahmin onaylandı; CPU'da ayrıca ölçülecek. **⚠️ Ölçek varsayımı SUPERSEDED — bkz. #32 (~5000)**. Brute-force kararı kendisi geçerliliğini koruyor, ~5000'de yeniden ölçülecek |
| 12 | **[Faz 1]** GUI bu fazda yok | Sonraki faz konusu |
| 13 | **[Faz 1]** Gerçek fabrika DB entegrasyonu sonraki faz | Değişmedi |
| 14 | **[Faz 2]** CLIP vs SigLIP benchmark tamamlandı (11 görsel + sentetik varyasyon, 55 sorgu) | Top-1: CLIP %98, SigLIP %100. Top-3/Top-5: ikisi de %100. CPU sorgu süresi ~91ms, ikisinde de eşit. Detay: `benchmark/results/report.md`. **Final model seçimi henüz Confirmed değil** — bkz. Not Yet Decided #1 |
| 15 | **[Faz 3]** Ürün, Windows masaüstü uygulaması olarak C#/.NET ile geliştirilecek | Yönetici onayı (2026-09-01). Faz 1/2 Python kodu **silinmeyecek**; benchmark/model değerlendirme aracı olarak kalır, runtime bu değildir |
| 16 | **[Faz 3]** Production hedefinde kullanıcı girişi (login) ve yetkilendirme olacak; **bugünkü MVP'de YOK** | Yönetici onayı (2026-09-01) |
| 17 | **[Faz 3]** Production hedefinde görsel kaynağı iki seçenekli olacak: fabrika DB veya local klasör; **bugünkü MVP'de yalnızca local klasör** | Yönetici onayı (2026-09-01). Faz 1 karar #8 ile tutarlı. **⚠️ SUPERSEDED (2026-09-01, ikinci görüşme) — bkz. #31.** DB'den doğrudan okuma production hedefinden tamamen çıkarıldı; kaynak yalnızca dosya dizini |
| 18 | **[Faz 3]** Production hedefinde yeni ürün girişi Lens uygulaması üzerinden yapılacak; **bugünkü MVP'de ürün ekleme ekranı / CRUD / DB'ye yazma YOK** | Yönetici onayı (2026-09-01). **⚠️ SUPERSEDED (2026-09-01, ikinci görüşme) — bkz. #30.** Yeni ürün ekleme artık kalıcı olarak Lens'in kapsamı dışında, "bugün yok" değil "hiçbir zaman Lens'in sorumluluğu değil" |
| 19 | **[Faz 3]** Text search ileride olabilir; **bugünkü MVP'de YOK** | Yönetici onayı (2026-09-01), Faz 1 karar #2 ile tutarlı |
| 20 | **[Faz 3A]** MVP model: **CLIP** (openai/clip-vit-base-patch16) | Tech Lead/CTO onayı (2026-09-01). **Provisional/reversible karar** — production için kesin model kararı değildir. Gerekçe: Top-5 zaten CLIP=%100/SigLIP=%100 eşit; SigLIP yalnızca `downscale_upscale` varyasyonunda Top-1 avantajı gösterdi; CLIP'in ONNX/.NET entegrasyon riski daha düşük. SigLIP sonucu ve yeniden değerlendirme ihtimali dokümantasyonda kalır (bkz. Confirmed #14) |
| 21 | **[Faz 3A]** WPF (.NET 8) masaüstü UI framework olarak onaylandı | Tech Lead/CTO onayı (2026-09-01) |
| 22 | **[Faz 3A]** Lens.exe içinde Python runtime/process YOK; AI inference ONNX Runtime (`Microsoft.ML.OnnxRuntime`) ile .NET içinde çalışır | Tech Lead/CTO onayı (2026-09-01). Python benchmark kodu ayrı bir engineering aracı olarak kalır (karar #15) |
| 23 | **[Faz 3A]** Embedding'ler kalıcı local cache/index olarak saklanır; her klasör seçiminde yeniden hesaplanmaz | Tech Lead/CTO onayı (2026-09-01) — önceki "kalıcı cache yok" önerisi **reddedildi**. Incremental kontrol: relative path + file size + LastWriteTimeUtc + embedding. Vector DB / SQLite kullanılmayacak (gerçekten gerekmedikçe) |
| 24 | **[Faz 3A]** İlk ürün index'i: 11 gerçek nevresim görseli, .NET/CLIP embedder ile üretilir | Tech Lead/CTO onayı (2026-09-01). Faz 2'nin 55 sentetik varyasyonu ürün index'ine girmez — yalnızca benchmark/test verisidir. Python ve .NET arasında preprocessing tutarsızlığı olmaması için embedding'ler Python'dan aktarılmaz, .NET tarafında yeniden üretilir |
| 25 | **[Faz 3A]** MVP iş kuralı: aynı desenin farklı renk varyasyonu aynı ürün kabul edilir | Tech Lead/CTO onayı (2026-09-01). **Geçici/reversible karar** — ileride değişebilir. Bu varsayım için ekstra grayscale/özel algoritma eklenmeyecek; gerçek sonuçlar gözlemlenecek |
| 26 | **[Faz 3A]** Query görselleri MVP'de temiz/katalog tipi olacak | Tech Lead/CTO onayı (2026-09-01). Karmaşık telefon fotoğrafı, kırışık kumaş, perspektif farkı, karmaşık arka plan MVP kapsamı dışında |
| 27 | **[Faz 3A]** Final MVP dağıtımı: self-contained Windows publish (Visual Studio/.NET SDK/Python gerekmeden çalışır) | Tech Lead/CTO onayı (2026-09-01). Yönetici bilgisayarı donanım açısından yeterli kabul edilir, ek optimizasyon yapılmayacak |
| 28 | **[Faz 3A]** Gerçek ürün görselleri (`nevresim/`) ve büyük ONNX model dosyaları GitHub'a commit edilmeyecek | Tech Lead/CTO onayı (2026-09-01). `.gitignore` koruması sürdürülür. Repo public/private durumu ve model dağıtım yaklaşımı ayrı değerlendirilecek |
| 29 | **[Faz 3A]** Çalışma sırası fazlara bölündü: önce minimal .NET AI proof (GUI'siz/minimal), sonra onay sonrası Faz 3B (tam WPF UI) | Tech Lead/CTO onayı (2026-09-01) |
| 30 | **[Faz 4]** Yeni ürün/desen ekleme Lens'in sorumluluğu değil; başka bir sistem/süreç tarafından ürün dizinine eklenir | Yönetici görüşmesi (2026-09-01, ikinci görüşme). **SUPERSEDES #18.** Detay: `docs/PRODUCTION_REQUIREMENTS.md` §1 |
| 31 | **[Faz 4]** Görsel kaynağı yalnızca **dosya dizini**; DB'den doğrudan okuma yok | Yönetici görüşmesi (2026-09-01). **SUPERSEDES #17.** Detay: `docs/PRODUCTION_REQUIREMENTS.md` §2 |
| 32 | **[Faz 4]** Gerçek ölçek: **~5000 görsel**, **tek (flat) klasör**; recursive alt klasör taraması şu an gerekli değil | Yönetici görüşmesi (2026-09-01). **SUPERSEDES** #11'deki ~1.000 ölçek varsayımını. Detay: `docs/PRODUCTION_REQUIREMENTS.md` §2 |
| 33 | **[Faz 4]** Varsayılan ürün dizini **configurable** olacak (hardcode edilmeyecek); **UNC network path** birincil yaklaşım | Yönetici görüşmesi (2026-09-01). Gerçek path henüz bilinmiyor — blocker değil. Detay: `docs/PRODUCTION_REQUIREMENTS.md` §3 |
| 34 | **[Faz 4]** Varsayılan dizine erişilemezse uygulama **crash olmayacak**; kullanıcı dostu durum mesajı + manuel klasör seçme sunulacak | Yönetici görüşmesi (2026-09-01) |
| 35 | **[Faz 4]** **Top-10** gösterilecek (Top-5 yerine); arama algoritması değişmiyor | Yönetici görüşmesi (2026-09-01). **SUPERSEDES #9** |
| 36 | **[Faz 4]** Logging confirmed requirement: persistent log dosyası + UI'da indeksleme özeti | Yönetici görüşmesi (2026-09-01). Detay: `docs/PRODUCTION_REQUIREMENTS.md` §8 |
| 37 | **[Faz 4]** Dosya sınıflandırması: yalnızca `.jpg/.jpeg/.png` image pipeline'a girer; diğer dosyalar (`.json`, `.txt` vb.) hata sayılmadan atlanır; bilinen ama desteklenmeyen görsel formatları (`.tif`, `.bmp`, `.webp`) ayrı "unsupported format" olarak sınıflandırılır | Yönetici görüşmesi (2026-09-01). Detay: `docs/PRODUCTION_REQUIREMENTS.md` §7 |
| 38 | **[Faz 4]** UI'da sınırlı görsel iyileştirme (hiyerarşi, accent color, durum renkleri); büyük redesign/animasyon/ağır tema YOK | Yönetici görüşmesi (2026-09-01) |
| 39 | **[Faz 4A]** Ürün dizini (paylaşılan UNC klasör) yalnızca görsel içerir; her kullanıcı/PC kendi local index/cache'ini `%LocalAppData%\Lens\` altında tutar | **Confirmed** — Yönetici onayı (2026-09-01, FAZ 4A kickoff). Önceden Tech Lead önerisiydi (Recommended), şimdi onaylandı. Detay: `docs/PRODUCTION_REQUIREMENTS.md` §5 |
| 40 | **[Faz 4A]** Index dosyası yazımı **atomic** olacak (temp dosya + replace) | **Confirmed** — Yönetici onayı (2026-09-01). ~5000 ölçekte tam-dosya-yeniden-yazma sırasında kesinti/çökme durumunda index kaybı riskini azaltmak için |
| 41 | **[Faz 4A]** Varsayılan dizin, exe yanında duran basit bir JSON config dosyasından (`appsettings.json`, kendi `System.Text.Json` kodumuzla, **ek NuGet yok**) okunacak; klasik `Properties.Settings`/`user.config` kullanılmayacak | **Confirmed** — Yönetici onayı (2026-09-01). Gerekçe: `user.config` sürüm-bazlı path taşınma sorunu |
| 42 | **[Faz 4A]** Admin default ve user override ayrı tutulur: `AdminDefaultProductDirectory` (salt-okunur, exe yanındaki config'te) vs `UserOverrideProductDirectory` + `UseUserOverride` (yazılabilir, `%LocalAppData%\Lens\config\` altında) | Yönetici onayı (2026-09-01) |
| 43 | **[Faz 4A]** Kullanıcının seçtiği alternatif klasör **varsayılan olarak geçici**dir (session-only, restart'ta admin default'a döner); kullanıcı açıkça "Bu klasörü varsayılan olarak kullan" derse `UseUserOverride=true` yazılıp kalıcı olur | Yönetici onayı (2026-09-01) |
| 44 | **[Faz 4A]** Cache anahtarı: ürün dizini path'inden türetilen deterministic bir tanımlayıcı (hash); farklı ürün dizinleri farklı cache klasörü kullanır, aynı dizine dönülünce eski cache tekrar kullanılır | Yönetici onayı (2026-09-01) |
| 45 | **[Faz 4A]** `%LocalAppData%\Lens\` altında `config/`, `cache/`, `logs/` alt klasör yapısı; `logs/` bu fazda oluşturulur ama kullanılmaz (logging implementasyonu FAZ 4C) | Yönetici onayı (2026-09-01) |
| 46 | **[Faz 4B]** Search-before-refresh: "Ara" öncesi ürün dizini freshness kontrolü yapılır; yeni/değişmiş/silinmiş dosya varsa arama öncesi incremental update tetiklenir; kısa süre önce güncellenmişse tekrar taranmaz | Yönetici onayı (2026-09-01). **Implement edildi ve kabul edildi** (FAZ 4B, 2026-09-01). Freshness TTL = **30 saniye** (yönetici düzeltmesi). Detay: `docs/ROADMAP.md` FAZ 4B |
| 49 | **[Faz 4B]** Dosya sınıflandırması `SupportedImage` / `UnsupportedImageFormat` / `NonImage` olarak 3 kategoride uygulandı; `IndexUpdateStats`/`IndexFileIssue`/`ChangeSummary` veri modeli eklendi; toplam tarama hatasında önceki geçerli index korunur | Yönetici onayı (2026-09-01, FAZ 4B kabul). Detay: `docs/ROADMAP.md` FAZ 4B |
| 50 | **[Faz 4C]** Logging: kendi kodu ile `ILensLogger`/`FileLogger` (ek NuGet yok); düşük coupling gereği `ImageIndex` loglamadan habersiz bırakıldı, log satırları çağıran katmanın (WPF) döndürdüğü istatistiklerden üretiliyor. Format: `%LocalAppData%\Lens\logs\lens-yyyyMMdd.log`, düz UTF-8, `[INFO/WARNING/ERROR]`, 30 günlük retention, loglama hatası uygulamayı çökertmez | Yönetici onayı (2026-09-01, FAZ 4C kabul). **Resolves Not Yet Decided #7.** Detay: `docs/ROADMAP.md` FAZ 4C |
| 51 | **[Faz 4C]** Ana ekrandaki indeksleme özeti sadeleştirildi: yalnızca kullanıcıya anlamlı bilgi gösterilir (sıfır olan sayaçlar/"değişmeyen" ana özette gösterilmez); ayrıntılı sayaçlar yalnızca log dosyasında ve "Sorunlu Dosyalar" penceresinde kalır | Yönetici onayı (2026-09-01, FAZ 4C sırasında). Veri modeli (IndexUpdateStats) değişmedi, yalnızca UI metni sadeleştirildi |
| 47 | **[Faz 4D]** Query görseli için WPF native drag & drop desteği (mevcut "Sorgu Görseli Seç" butonuna ek, yeni dependency yok) | Yönetici onayı (2026-09-01). **Implement edildi ve kabul edildi** (FAZ 4D, 2026-09-01). Detay: `docs/ROADMAP.md` FAZ 4D |
| 48 | **[Faz 4D]** Sonuç ekranı: query görseli üstte büyük/sürekli görünür; Top-10 altta sürekli görünür grid; Top-10'dan tıklanan sonuç ayrı bir "karşılaştırma" alanında büyük gösterilir + similarity score (query değişmez, sadece karşılaştırma alanı güncellenir) | Yönetici onayı (2026-09-01). **Implement edildi ve kabul edildi** (FAZ 4D, 2026-09-01). Detay: `docs/ROADMAP.md` FAZ 4D |
| 52 | **[Faz 4D]** "Yeni Arama" butonu (query/karşılaştırma/Top-10 temizlenir, ürün klasörü/index/cache korunur); minimal "⋮" menü (Ayarlar — salt-okunur özet, Log Klasörünü Aç, Hakkında); Top-10/query/karşılaştırma görselleri için çift-tıkla büyük önizleme/zoom penceresi (`ImagePreviewWindow`: fare tekerleği zoom, sürükle-pan, çift tık sığdır, ESC kapat, aynı anda tek önizleme açık) | Yönetici onayı (2026-09-01, FAZ 4D kabul + son UI/UX polish turu). Detay: `docs/ROADMAP.md` FAZ 4D |
| 53 | **[Faz 4D]** Önizleme penceresi boyutu görselin tam piksel çözünürlüğüne göre değil, ana ekrandaki küçük önizleme kutusuna (220px) göre "biraz daha büyük" (uzun kenar ~480px + pay) hesaplanır; ekran çalışma alanının %85'i ile üst sınırlanır | Yönetici onayı (2026-09-01) — ilk versiyon (görselin tam çözünürlüğüne göre boyutlandırma) ekranı gereğinden fazla kapladığı için düzeltildi |
| 54 | **[Faz 4D]** Son görsel geri bildirim turu: sürükle-bırak sırasında QueryDropZone'da accent border/hafif arka plan/"Görseli buraya bırak" metni (DragLeave/Drop'ta normale döner, geçersiz dosyada gösterilmez); karşılaştırma alanında görüntülenen benzerlik değeri tam **%100.0** ise yeşil vurgu (biçimlendirilmiş metin karşılaştırması, ham `double == 1.0` değil); query paneli border'ı her zaman accent renkli/2px (sonuç paneli nötr kalır) — "arattığım görsel bu" hissi. Query, karşılaştırma sonucu ve Top-10 kartlarındaki dosya adları salt-okunur ama seçilebilir/kopyalanabilir metin kutusu (I-beam imleç, Ctrl+C, sağ-tık kopyala); Top-10 kartlarında dosya adı butonun DIŞINA alındı ki metin seçimi kart tıklama/seçim davranışını bozmasın. Sürükleme sırasında küçük, yarı saydam bir thumbnail (native WPF `Adorner`, `IsHitTestVisible=false`) fareyi takip eder — Explorer'ın kendi "ghost" görseli Lens penceresine girince kaybolduğu için eklendi | Yönetici onayı (2026-09-01/02, canlı kullanım geri bildirimi). Gerçek OS-seviyesi sürükleme davranışı (drag preview'ın fareyi takip etmesi) otomasyonla test edilemedi, manuel doğrulama gerekiyor |

---

## Not Yet Decided (Henüz Alınmamış Kararlar)

Bu konularda **kesin hüküm verilmemiştir**.

| # | Konu | Neden Açık |
|---|------|------------|
| 1 | **Production** için final model seçimi: CLIP mi SigLIP mi, yoksa başka bir model mi | MVP için CLIP provisional olarak seçildi (karar #20); production için kesin değil, ileride yeniden değerlendirilebilir |
| 2 | Dış AI servislerine (cloud API) izin verilip verilmeyeceği | PoC kapsamında kullanılmayacağı netleşti (karar #6); ileri faz için hâlâ açık |
| 3 | ~~Gerçek fabrika veritabanı DBMS türü~~ | **Artık ilgisiz** — karar #31 ile görsel kaynağı olarak DB kalıcı olarak kapsam dışı bırakıldı |
| 4 | Çoklu kullanıcı / eşzamanlılık gereksinimleri | İleri faz, detay netleşmedi. Not: dosya erişimi tarafında kısmi cevap var (bkz. karar #39 — Recommended, local cache) |
| 5 | Test/benchmark metodolojisi (sentetik varyasyonların temsil gücü) | Gerçek ikinci fotoğraf verisi yok |
| 6 | **[Faz 1]** Aynı desen farklı renk aynı ürün mü sayılmalı? (production için) | MVP'de geçici olarak "aynı ürün" kabul edildi (karar #25); production için kesin değil — benchmark bu konuyu kanıtlamadı |
| ~~7~~ | ~~**[Faz 4]** Log formatı ve logging altyapısı~~ | **Çözüldü** — bkz. karar #50 (FAZ 4C, 2026-09-01) |

Not: Arayüz teknolojisi (#15/#21), embedding depolama biçimi (#23), local
cache konumu/atomic write/config ayrımı (#39-45) artık **Confirmed**.
"Override restart sonrası hatırlanmalı mı" (#43 ile — hatırlanmaz, session-
only, explicit "varsayılan yap" hariç) ve "ayarlar dosyası şeması" (#41-42
ile) netleşti; tablodan kapatıldı.

---

## Later Phase (Şimdilik Karar Gerektirmeyen)

- Text search mimarisi (MVP'de yok — karar #19)
- Login / kullanıcı yönetimi (production'da planlı, MVP'de yok — karar #16)
- Raporlama
- Çoklu kullanıcı ölçeklenmesi
- Vector database / ANN index (ölçek ~5000'den çok büyürse yeniden
  değerlendirilecek — bkz. karar #32, `docs/PRODUCTION_REQUIREMENTS.md` §5)
- Dış AI servisi kullanımı (yönetim onayı verirse)
- Recursive alt klasör taraması (şu an gerekli değil — karar #32)

**Kalıcı olarak kapsam dışı (Later değil, artık hiç planlanmıyor):**
- Fabrika DB entegrasyonu — görsel kaynağı olarak (karar #31, SUPERSEDES #17)
- Ürün ekleme / CRUD ekranları, yeni ürün girişi Lens üzerinden (karar #30, SUPERSEDES #18)

---

## Karar Alma Süreci

Yukarıdaki "Not Yet Decided" maddeleri, kullanıcı ve/veya Tech Lead/CTO ile
yapılacak mimari toplantı sonrası bu tabloya "Confirmed" olarak taşınacaktır.
Onay olmadan hiçbir madde implementasyona esas alınmaz (bkz. `CLAUDE.md`).
