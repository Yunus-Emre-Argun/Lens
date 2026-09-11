# Desen Kodu Servisi, Çevrimdışı Kod Gösterimi ve Eksik Kod Yer Tutucusu

Sonuç kartlarında dosya adının yanında desen kodunun gösterilmesi
(`desen.jpg` **(00123)**), kodu alınamayan ürünlerde ise aynı yerde kısa bir
yer tutucu (`desen.jpg` **(—)**) gösterilmesi için kurulan iki akışlı çözümü
anlatır.

> **Durum: deney dalı `feature/desen-code-placeholder`.**
> Dal, çok modelli sürümden (`feature/multi-model-search`) açılmıştır.
> `main` ve diğer pilot dalları değiştirilmemiştir.
>
> **Gerçek servis metodu hâlâ bulunamamıştır** (bkz. §2). Bu dalda da canlı
> servis doğrulaması **yapılmamıştır**; tüm doğrulama sahte servisle
> otomatik testler üzerindendir.

## 1. Neden iki ayrı akış?

Normal kullanıcıların servise erişimi **yoktur** (VPN gerekir). Bu yüzden her
aramada servise bağımlı bir çözüm kurulmamıştır:

| Akış | Kim | Ne yapar |
|---|---|---|
| **Hazırlama** | VPN erişimi olan bilgisayar | ⋮ → **Desen Kodlarını Güncelle** ile kodları servisten alır, katalog yanındaki ortak dosyaya yazar |
| **Okuma** | Tüm kullanıcılar | Kodları o dosyadan **çevrimdışı** okur; servis erişimi gerekmez |

Servis yapılandırılmamış veya erişilemez olsa bile **uygulama açılışı, görsel
arama ve indeksleme normal çalışır**. Arama sırasında servise **hiçbir istek
gönderilmez** ve servis cevabı **beklenmez**; yalnızca kod güncelleme işlemi
kullanılamaz.

## 2. Servis sözleşmesi — DOĞRULANAN ve DOĞRULANAMAYAN

### ✅ Önceki turda canlı doğrulandı (`feature/desen-code-service`)

Uç `http://192.194.196.101:801/ozx.asmx` erişilebilir ve WSDL'i incelendi:

| | Değer |
|---|---|
| Namespace | `http://tempuri.org/` |
| SOAPAction | `http://tempuri.org/<MetotAdı>` |
| Binding | SOAP 1.1 (`Service1Soap`) ve SOAP 1.2 (`Service1Soap12`) |
| İstek | `<MetotAdı><parametre>değer</parametre></MetotAdı>` |
| Cevap | `<MetotAdıResponse><MetotAdıResult>metin</...></...>` |
| Kimlik doğrulama | **İstemiyor** — kimliksiz çağrı HTTP 200 döndü |

Bu desen, uygulamadaki istemcinin ürettiği zarfla gerçek servise karşı
çalıştırılarak doğrulanmıştı: `HelloWorld` metodu HTTP 200 ve
`<HelloWorldResult>Hello World</HelloWorldResult>` döndü.

### ❌ Doğrulanamadı — `GetDesenKodu` bu uçta YOK

`ozx.asmx` **92 metot** yayınlıyor; `GetDesenKodu` bunlardan biri **değil**.
"desen / pattern / design / motif / dosya / file / image" geçen hiçbir metot
yok.

Canlı kanıt (önceki tur):

```
POST /ozx.asmx   SOAPAction: "http://tempuri.org/GetDesenKodu"
→ HTTP 500
  <faultstring>Server did not recognize the value of HTTP Header
   SOAPAction: http://tempuri.org/GetDesenKodu.</faultstring>
```

**Bu turda uca yeni bir çağrı yapılmamıştır.** Parametrenin gerçek adı, dönüş
alanının adı ve dosya adının uzantılı mı uzantısız mı gönderileceği hâlâ
**doğrulanmamıştır**.

### Bu yüzden sözleşme yapılandırmadadır

Metot/parametre adları koda **gömülmemiştir**. Doğru ad öğrenildiğinde
yeniden derleme değil, exe yanındaki `appsettings.json`'da tek satır
değişikliği yeterlidir:

```json
{
  "AdminDefaultProductDirectory": "",
  "DesenCodeService": {
    "Endpoint": "",
    "MethodName": "",
    "ParameterName": "",
    "Namespace": "http://tempuri.org/",
    "TimeoutSeconds": 15,
    "AbortAfterConsecutiveFailures": 5,
    "SendFileExtension": true,
    "TryWithoutExtensionOnNotFound": true
  }
}
```

Alanlar boşsa servis **yapılandırılmamış** sayılır: güncelleme işlemi tek bir
mesajla durur, **hiçbir servis isteği denenmez** (her ürün için tekrarlayan
başarısız çağrı oluşmaz), kayıtlı kodların gösterimi ve arama etkilenmez.

> Bu dosyaya **kimlik bilgisi yazılmaz**. Servis kimliksiz çağrıya yanıt
> vermektedir; VPN girişi ile servis kimlik doğrulaması **aynı şey değildir**.

## 3. Dosya adı eşleştirme

Varsayılan olarak yalnızca **dosya adı, uzantısıyla** gönderilir
(`desen.jpg`). Tam yerel yol, UNC yolu veya dosya içeriği **asla**
gönderilmez.

`TryWithoutExtensionOnNotFound` açıkken, servis **açıkça "bulunamadı"**
derse uzantısız ad ikinci kez denenir. Bu fallback **bağlantı hatası, zaman
aşımı veya SOAP fault durumunda çalışmaz** — aksi halde geçici bir ağ sorunu
iki katına çıkar ve kalıcı "bulunamadı" gibi görünürdü. Hangi biçimin
eşleştiği metadata'ya (`QueriedFileName`) yazılır.

Dosya adından kod **tahmin edilmez**; adlar parçalanarak sorgulanmaz.

### ⚠ Aynı adlı dosyalar — GERÇEK ve ÇÖZÜLMEMİŞ belirsizlik

Bu sürümde alt klasör taraması **açıktır** (`CatalogScanner`), dolayısıyla
belirsizlik artık teorik değil **gerçektir**:

- **Kayıt anahtarı** katalog köküne göre `/` ayraçlı **göreli yoldur**.
  `a/desen.jpg` ile `b/desen.jpg` metadata'da **ayrı kayıtlardır** ve
  birbirinin kodunu **ezmez** (test P53, P55).
- **Ama servis yalnızca dosya adını kabul eder.** Bu yüzden aynı adlı iki
  dosya için **tek sorgu** yapılır ve ikisi de **aynı kodu** alır (test P54).
  İçerikleri farklıysa kodun hangisine ait olduğu **kesinleştirilemez**.

Bu **çözülmüş değildir ve çözülmüş gibi sunulmamaktadır**. Gerçek çözüm,
servisin göreli yol veya başka bir tekil anahtar kabul etmesini gerektirir;
bu, servis tarafında bir değişikliktir ve bu dalın kapsamı dışındadır.

## 4. Metadata dosyası

```
<ÜrünDizini>\.lens\metadata\desen-codes-v1.json    (kilit: desen-codes.lock)
```

Embedding indekslerinden (`.lens/index.json`, `.lens/indexes/*` — dört model
profili dâhil) ve onların kilitlerinden **tamamen ayrıdır**:

- Modelden bağımsızdır — DINO/CLIP seçimi, renkli/gri modu veya "desen odaklı
  karşılaştırma" değişince **yeniden sorgulama gerekmez**.
- Arama skorlarını **etkilemez**.
- Görsellerle birlikte taşındığında çalışmaya devam eder.
- Kod güncelleme ile indeksleme **birbirini bloklamaz** (ayrı kilitler).

### Şema (sürüm 1)

| Alan | Açıklama |
|---|---|
| `SchemaVersion` | 1. Farklı değer = bilinmeyen şema, dosya **kullanılmaz** |
| `SourceService` | Kodların hangi kaynaktan geldiği (izlenebilirlik) |
| `LastSuccessfulRefreshUtc` | En son **tamamlanan** güncellemenin zamanı |
| `Entries[].RelativePath` | Katalog köküne göre `/` ayraçlı göreli yol (kayıt anahtarı) |
| `Entries[].QueriedFileName` | Servise gerçekte gönderilen ad |
| `Entries[].Code` | Desen kodu — **string**, baştaki sıfırlar korunur (`"00123"`) |
| `Entries[].State` | `Found` / `NotFound` |
| `Entries[].UpdatedUtc` | Kaydın son **başarılı** güncellenme anı |

Kod **string** tutulur: baştaki sıfırlar anlamlıdır ve kesin uzunluk
doğrulanmamıştır. Sayıya çevrilmez, sabit hane kısıtı konmaz.

> **Yer tutucu metadata'ya YAZILMAZ.** `(—)` yalnızca arayüz gösterimidir;
> dosyaya `00000`, tire veya başka bir sahte kod **kaydedilmez** (test P56,
> P60).

## 5. Hata ve güncelleme davranışı

Dört durum **birbirinden ayrıdır** ve tek bir "başarısız"a indirgenmez:

| Durum | Anlamı | Davranış |
|---|---|---|
| `Found` | Geçerli kod geldi | Kod ve tarih güncellenir |
| `NotFound` | Servis "kod yok" dedi (boş cevap) | Kayıt kod **olmadan** tutulur; ekranda `(—)` görünür; sonraki açık yenilemede tekrar denenir |
| `ServiceUnavailable` | Ulaşılamadı / zaman aşımı / SOAP fault | **Eski kod ve eski tarih aynen korunur** |
| `InvalidResponse` | Cevap ayrıştırılamadı | **Eski kayıt korunur** |

Ek kurallar:

- Boş cevap **gerçek kod gibi kaydedilmez**.
- Bağlantı hatası **"kod bulunamadı" diye kalıcılaştırılmaz**; kaydı olmayan
  bir dosya için hiç kayıt oluşturulmaz (ekranda yalnızca `(—)` görünür).
- Artık katalogda olmayan dosyaların kayıtları düşer — kod **başka bir
  dosyaya taşınmaz**.
- Yazım **atomiktir**; okuyucular yarım JSON görmez.
- Kataloğa yazılamazsa **açık hata** gösterilir; kullanıcı ayarları dizinine
  gizlice yazılmaz.

## 6. Kodları güncelleme işlemi

⋮ → **Desen Kodlarını Güncelle** (VPN erişimi olan bilgisayarda kullanılır).

- Normal aramada **asla otomatik tetiklenmez**; sıradan arama servis cevabı
  **beklemez**.
- Dosya listesi indekslemenin kullandığı **aynı tarama sözleşmesinden**
  (`CatalogScanner`) gelir: alt klasörler dâhil, `/` ayraçlı göreli yol,
  `.lens` atlanır, junction/symlink takip edilmez.
- Aynı dosya adları **tekilleştirilir** (tek sorgu).
- Sıralı ve zaman aşımlıdır; **iptal edilebilir**.
- Sınırsız retry **yoktur**: art arda `AbortAfterConsecutiveFailures` kadar
  hata olursa işlem **durur** ve kalan görseller için aynı çağrı
  **tekrarlanmaz**.
- **Tüm ürünleri etkileyen hata** (metodun bulunamaması, kapalı uç, yanlış
  adres) bu eşiğe takılır: işlem erken durur ve **tek bir durum mesajı**
  gösterilir — son hatanın açıklaması (örn. SOAP faultstring) o mesajın
  içindedir. **Her dosya için ayrı uyarı açılmaz.**
- İlerleme panelinde tamamlanan/toplam sorgu gösterilir; UI thread'i
  bloklanmaz; mevcut busy guard ile uyumludur.

> Eşik, metin eşleştirmesine (`faultstring == "Method not found"` gibi)
> **bilerek bağlanmamıştır**: servisin gerçek hata metni bilinmiyor,
> tahmine dayalı bir kural yanlış durumda sessizce çalışmazdı.

Servis sonradan hazır olduğunda **aynı işlem** gerçek kodları alır; başarılı
kayıttan sonra ekrandaki `(—)` yer tutucuları gerçek kodla **değişir** (test
P62). Liste yeniden oluşturulmaz — sıralama, seçim ve kaydırma konumu
korunur.

## 7. Arayüz

### Gösterim

| Durum | Gösterim | Araç ipucu |
|---|---|---|
| Kod var | `desen.jpg` **(00123)** | `alt/desen.jpg — desen kodu 00123` |
| Kod yok | `desen.jpg` **(—)** | `alt/desen.jpg — Desen kodu henüz alınamadı` |

- Aynı davranış **hem sonuç kartlarında hem "Seçilen Sonuç" alanında**
  geçerlidir; biçim tek kaynaktan gelir
  (`Lens.Core.DesenCodes.DesenCodeDisplay`), iki yer birbirinden sapamaz.
- Kod kutusu **hiçbir zaman gizlenmez**; kod geldiğinde yalnızca metni
  değişir, yerleşim **kaymaz**.
- Yer tutucu **rakam değildir** (em dash, U+2014): `00000` veya `0` ile
  karıştırılamaz (test P46).

### Dar kartta taşma

Kart ve "Seçilen Sonuç" alanında dosya adı `*`, kod `Auto` sütundadır. Kod
sütunu önce ölçüldüğü için **kod hiçbir genişlikte ekrandan itilmez**; taşan
kısım dosya adı sütunudur.

- Kartta gösterilen ad, göreli yolun **yalnızca son parçasıdır**
  (`alt/klasor/desen.jpg` → `desen.jpg`); **tam göreli yol araç ipucundadır**.
- Satır sarma kapalıdır (`TextWrapping="NoWrap"`), böylece uzun bir ad kodu
  aşağı itmez.

> **Bilinen sınırlama:** dosya adı, karar #54 gereği seçilebilir/kopyalanabilir
> bir `TextBox`'tır ve **WPF `TextBox` `TextTrimming` desteklemez**. Bu yüzden
> çok uzun bir dosya adı sütun kenarında **kırpılır ama sonuna `…` konmaz**.
> Görünür `…` istenirse adın `TextBlock`'a çevrilmesi gerekir; bu, karar
> #54'ün kopyalanabilirlik kuralını kaldırır ve **ayrı bir onay konusudur**.

### Diğer kurallar

- Kod/metadata durumu **tek bir yerde** bildirilir; kartlarda tekrar eden
  hata yazısı oluşmaz — kodu olmayan kart yalnızca sade `(—)` gösterir.
- Eşleme anahtarı **göreli yoldur**; dosya adıyla eşleme yapılmaz, böylece
  alt klasörlerdeki aynı adlı dosyalar birbirinin kodunu **göstermez**.
- Klasör değişiminde eşleme tamamen yenilenir; eski klasörün kodları yeni
  sonuçlara **sızmaz**. Arama, başladığı andaki eşlemenin kopyasını kullanır.
- Sorgu görselinin kodu, dosya adına bakarak katalogdaki bir kayıttan
  **türetilmez**.

Model seçimi (DINOv2 Base / CLIP Standart), renkli/gri modu, "desen odaklı
karşılaştırma", ön işleme, benzerlik hesabı, eşik ve embedding verisi bu
işlemden **etkilenmez**.

## 8. Dağıtım

Metadata dosyası **görsellerle aynı klasörde** (`.lens\metadata\`) durur.

- **Paylaşılan katalog:** VPN'li bilgisayar bir kez günceller, tüm kullanıcılar
  aynı dosyayı okur. **VPN'siz kullanıcılar yerel metadata'daki kodları
  görmeye devam eder.**
- **Yerel katalog kopyaları:** görseller kopyalanırken `.lens\metadata\`
  klasörünün **de kopyalanması gerekir**; aksi halde o kopyada tüm ürünler
  `(—)` görünür.
- Dosya **git'e ve kurulum paketine gerçek verilerle dahil edilmez**
  (`.gitignore` → `.lens/`).
- Merkezî bir erişim mekanizması **yoktur** ve varsayılmamıştır.

## 9. Doğrulanan / doğrulanamayan

| | Durum |
|---|---|
| Servis erişimi, WSDL, namespace, SOAPAction, SOAP 1.1/1.2 | ✅ önceki turda canlı doğrulandı |
| İstemcinin zarfı + cevap ayrıştırması (gerçek `HelloWorld` metoduyla) | ✅ önceki turda canlı doğrulandı |
| **`GetDesenKodu` metodunun varlığı** | ❌ **serviste YOK** |
| Parametre adı, dönüş alanı, uzantılı/uzantısız biçim | ❌ doğrulanamadı |
| Gerçek kodlarla uçtan uca akış | ❌ doğrulanamadı |
| **Bu turda canlı servis çağrısı** | ❌ **yapılmadı** |
| Aynı adlı dosyaların ayırt edilmesi | ⚠ metadata ayırır, **servis ayıramaz** (§3) |
| Metadata yazma/okuma, hata koruması, iptal, tekilleştirme, erken duruş | ✅ otomatik testlerle (sahte servis) |
| `(—)` yer tutucusu, göreli yol anahtarı, yer tutucunun diske yazılmaması | ✅ otomatik testlerle (Grup P) |
| Canlı arayüz görünümü | ❌ uygulama açılmadı |

## İlgili Dokümanlar

- Kararlar: `docs/DECISIONS.md`
- Çok modelli arama: `docs/MULTI_MODEL_SEARCH.md`
- Mimari: `docs/ARCHITECTURE.md`
- Dağıtım: `docs/DEPLOYMENT.md`
