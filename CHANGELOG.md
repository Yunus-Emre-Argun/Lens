# Changelog

Bu doküman [Keep a Changelog](https://keepachangelog.com/) biçimini takip
eder. Aşağıdaki geçmiş girdileri, mevcut git commit geçmişi ve
`docs/ROADMAP.md`'deki faz kayıtlarından **geriye dönük** doldurulmuştur —
bu proje henüz tag tabanlı bir release süreci kullanmadığı için sürüm
numarası yerine faz adı ve tarih kullanılmıştır. Buradan sonrası
`docs/RELEASE_PROCESS.md`'de önerilen tag tabanlı release sürecine göre
güncellenmelidir.

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
