# Desen Kodu Servisi, Çevrimdışı Kod Gösterimi ve Eksik Kod Yer Tutucusu

Sonuç kartlarında dosya adının yanında desen kodunun gösterilmesi
(`desen.jpg` **(00123)**), kodu alınamayan ürünlerde ise aynı yerde kısa bir
yer tutucu (`desen.jpg` **(—)**) gösterilmesi için kurulan iki akışlı çözümü
anlatır.

> **Durum: deney dalı `feature/desen-code-integration`.**
> Dal, `feature/desen-code-placeholder` üzerinden (o da çok modelli sürümden)
> açılmıştır. `main` ve diğer pilot dalları değiştirilmemiştir.
>
> **2026-09-11: `GetDesenKodu` servis tarafında yayımlandı ve sözleşme canlı
> olarak doğrulandı** (bkz. §2). İstemci gerçek uca bağlanıyor, `HTTP 200`
> alıyor ve cevabı doğru ayrıştırıyor. **Ancak gerçek bir ürün için doğru
> kodun döndüğü hâlâ doğrulanmadı** — bkz. §2.4.

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

## 2. Servis sözleşmesi — DOĞRULAMA DURUMU

### 2.1 ✅ 2026-09-11 — `GetDesenKodu` YAYIMLANDI ve WSDL'de doğrulandı

`http://192.194.196.101:801/ozx.asmx?WSDL` yeniden okundu (HTTP 200, 193.946
bayt). Servis artık **93 metot** yayınlıyor (önceki turda 92 idi) ve yeni
metot `GetDesenKodu`. WSDL'den birebir okunan sözleşme:

| | WSDL'deki değer |
|---|---|
| Endpoint | `http://192.194.196.101:801/ozx.asmx` |
| Metot | `GetDesenKodu` |
| Parametre | `DosyaAdi`, `s:string`, `minOccurs=0 maxOccurs=1` |
| Namespace | `http://tempuri.org/` |
| SOAPAction | `http://tempuri.org/GetDesenKodu` (hem `Service1Soap` hem `Service1Soap12` binding'inde) |
| Dönüş alanı | `GetDesenKoduResult`, `s:string` |
| Stil | `document` |

Bu, görevde bildirilen sözleşmenin **tamamını** doğruluyor.

### 2.2 ✅ Canlı çağrı — uygulamanın KENDİ istemcisiyle

`Lens.AiProof desencodelive <katalog> <n>` modu eklendi. Bu mod:

- uç/metot/parametre adlarını **koddan değil**, exe yanındaki
  `appsettings.json`'dan okur (üretimle **aynı** yapılandırma yolu),
- dosya listesini indekslemenin kullandığı **aynı tarama sözleşmesinden**
  (`CatalogScanner`) alır,
- **yalnızca dosya adı** gönderir — görsel içeriği, tam yerel yol, UNC yolu
  gönderilmez,
- varsayılan **5**, en fazla **25** dosya sorar (kod düzeyinde sınır) —
  "önce az sayıda dene" kuralı bu moda **bağlanmıştır**, buradan tüm kataloğa
  binlerce istek gönderilemez.

**Sonuç (2026-09-11, gerçek katalogdan 5 dosya adı):**
`0 kod bulundu, 5 "kod yok", 0 servis hatası`.

Yani: SOAPAction **kabul edildi**, HTTP **200** döndü, SOAP fault **yok**,
`GetDesenKoduResult` alanı **bulundu ve ayrıştırıldı**. Önceki turdaki
`HTTP 500 / "Server did not recognize the value of HTTP Header SOAPAction"`
hatası **artık oluşmuyor**.

Bilinmeyen bir dosya için servisin döndürdüğü gerçek gövde:

```xml
<GetDesenKoduResponse xmlns="http://tempuri.org/"><GetDesenKoduResult /></GetDesenKoduResponse>
```

Boş (kendi kendine kapanan) sonuç alanı istemcide **`NotFound`** olarak
yorumlanır — hata **değil**, "bu dosya için kod yok". Bu gerçek gövde
otomatik teste sabitlendi (P70).

### 2.3 ❌ Tarihsel kayıt — önceki turda `GetDesenKodu` serviste YOKTU

> Aşağıdaki bilgi **artık geçerli değildir**, tarihsel olarak saklanmaktadır.
>
> `feature/desen-code-service` turunda (2026-09-10) uç 92 metot yayınlıyordu
> ve `GetDesenKodu` bunlardan biri **değildi**:
>
> ```
> POST /ozx.asmx   SOAPAction: "http://tempuri.org/GetDesenKodu"
> → HTTP 500
>   <faultstring>Server did not recognize the value of HTTP Header
>    SOAPAction: http://tempuri.org/GetDesenKodu.</faultstring>
> ```
>
> O turda istemcinin taşıma katmanı, aynı servisteki gerçek `HelloWorld`
> metoduyla dolaylı olarak doğrulanmıştı. **2026-09-11 itibarıyla bu dolaylı
> doğrulamaya gerek kalmamıştır**: asıl metot yayımlandı ve doğrudan
> çağrıldı.

### 2.4 ⚠ HÂLÂ DOĞRULANMADI — dönen kodun DOĞRU kod olduğu

`HTTP 200` veya boş cevap, **tek başına doğru eşleşme kanıtı değildir.**

Bu makinede **gerçek üretim desen dosya adı yok**. Denemede kullanılan
katalog (`C:\Users\win11\Desktop\data`) internetten indirilmiş stok görsel
adlarından oluşuyor (`1,146,735 Baroque Pattern ... Shutterstock (1).jpg`);
ERP'nin bu adları tanımaması **beklenen** sonuçtur. Servis yalnızca
`GetDesenKodu` sunuyor — kod **listeleyen** bir metot yok, dolayısıyla
doğrulama çifti servisten de türetilemiyor.

**Bu yüzden aşağıdakiler AÇIK kalmıştır:**

- Gerçek bir desen dosyası için servisin **kod döndürdüğü** görülmedi
  (`Found` yolu canlı çalıştırılmadı).
- Dönen kodun **doğru** kod olduğu doğrulanamadı.
- Dosya adının **uzantılı mı uzantısız mı** eşleştiği canlı olarak
  görülmedi (fallback kuralı §3'te; kod tarafı hazır).

**Kapatmak için gereken:** bilinen tek bir çift — *"şu dosya adı → şu desen
kodu"*. Bununla tek bir `desencodelive` çalıştırması doğrulamayı tamamlar.

### 2.5 Sözleşme neden hâlâ yapılandırmada?

Metot/parametre adları **koda gömülmedi**. Doğrulandıkları için repo'daki
örnek ayarda artık **dolu** geliyorlar, ama servis tarafı bir gün adı
değiştirirse yeniden derleme değil, tek satırlık ayar değişikliği yeterli
olmalı.

```json
{
  "AdminDefaultProductDirectory": "",
  "DesenCodeService": {
    "Endpoint": "",
    "MethodName": "GetDesenKodu",
    "ParameterName": "DosyaAdi",
    "Namespace": "http://tempuri.org/",
    "TimeoutSeconds": 15,
    "AbortAfterConsecutiveFailures": 5,
    "SendFileExtension": true,
    "TryWithoutExtensionOnNotFound": true
  }
}
```

**`Endpoint` repo'da BİLEREK boştur.** Gerçek ortam adresi bir altyapı
bilgisidir; kaynak koda ve repo'daki örnek ayara **yazılmaz**, kurulum/dağıtım
ayarına girilir (bkz. §8).

`Endpoint` boşken servis **yapılandırılmamış** sayılır: güncelleme işlemi tek
bir mesajla durur ve **hiçbir servis isteği denenmez**; uygulama açılışı,
arama, indeksleme ve kayıtlı kodların gösterimi etkilenmez. Eksik alan mesajı
**yalnızca gerçekten eksik olan alanı** sayar (P68).

> Bu dosyaya **kimlik bilgisi yazılmaz**. Servis kimliksiz çağrıya yanıt
> vermektedir; VPN girişi ile servis kimlik doğrulaması **aynı şey değildir**.
> VPN kullanıcı adı/şifresi hiçbir dosyaya, loga veya commit'e yazılmamıştır.

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
- **Ama servis yalnızca dosya adını kabul eder.** Bu, 2026-09-11 WSDL
  okumasıyla **doğrulanmıştır**: `GetDesenKodu` elemanının şemasında tek bir
  alan vardır — `DosyaAdi`, `s:string`. Klasör, göreli yol veya başka bir
  ayırt edici parametre **yoktur**. Bu yüzden aynı adlı iki dosya için **tek
  sorgu** yapılır ve ikisi de **aynı kodu** alır (test P54). İçerikleri
  farklıysa kodun hangisine ait olduğu **kesinleştirilemez**.

Bu **çözülmüş değildir ve çözülmüş gibi sunulmamaktadır**. Gerçek çözüm,
servisin göreli yol veya başka bir tekil anahtar kabul etmesini gerektirir;
bu, **servis tarafında bir değişikliktir** ve uygulama tarafından
kapatılamaz. Servis desteği gelene kadar bu durum açık bir sınırlamadır.

Pratik etkisi: katalogda alt klasörlerde **aynı adlı** dosyalar varsa, o
dosyaların kod gösterimi yanıltıcı olabilir. Kayıtlar birbirini ezmez, ama
ikisi de aynı kodu gösterir.

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

### Servis adresinin pakete girmesi

`Endpoint` repo'da boştur, ama **kurulum paketinde dolu olmak zorundadır**:

- **ClickOnce**, dağıtılan her dosyanın hash'ini manifeste yazar. Paket
  üretildikten **sonra** `appsettings.json`'ı elle düzenlemek kurulumu
  **bozar** (doğrulama hatası). Bu yüzden gerçek adres **publish anında**
  dosyada olmalıdır.
- **Taşınabilir paket** için de aynı yol izlenir; orada dosyayı sonradan
  düzenlemek teknik olarak mümkündür ama iki farklı yöntem tutmamak için
  publish anında yazılır.

Sonuç: iç ağ adresi **paketin içinde** dağıtılır. Bu bir kimlik bilgisi
değildir (kullanıcı adı/şifre hiçbir yere yazılmaz), ancak bir **iç altyapı
bilgisidir** — paketler kurum dışına verilmemelidir.

Adres değişirse: yeni bir publish üretilmelidir; kurulu pakette dosyayı elle
değiştirmek ClickOnce doğrulamasını bozar.

## 9. Doğrulanan / doğrulanamayan

### Canlı servis (gerçek uca çağrı yapıldı)

| | Durum |
|---|---|
| Uç erişimi, WSDL, namespace, SOAPAction, SOAP 1.1/1.2 | ✅ canlı doğrulandı (2026-09-11) |
| **`GetDesenKodu` metodunun varlığı** | ✅ **yayımlanmış, WSDL'de var** (93 metot) |
| Parametre adı/tipi (`DosyaAdi`, string) ve dönüş alanı (`GetDesenKoduResult`, string) | ✅ WSDL'den birebir doğrulandı |
| İstemcinin zarfı + SOAPAction + cevap ayrıştırması, **asıl metot üzerinden** | ✅ canlı doğrulandı (5 dosya, HTTP 200, fault yok) |
| Kimlik doğrulama gerekmediği | ✅ canlı doğrulandı |
| "Kod yok" cevabının gerçek gövdesi (`<GetDesenKoduResult />`) | ✅ canlı yakalandı, teste sabitlendi (P70) |
| **Gerçek bir ürün için kod DÖNDÜĞÜ** | ❌ **görülmedi** — makinede gerçek desen dosya adı yok (§2.4) |
| **Dönen kodun DOĞRU kod olduğu** | ❌ **doğrulanamadı** — bilinen doğrulama çifti yok |
| Uzantılı/uzantısız eşleşme biçimi | ❌ canlı görülmedi (kod tarafı hazır, §3) |

### Sahte servis (ağ kullanmayan otomatik testler — Grup P)

| | Durum |
|---|---|
| Metadata yazma/okuma, bozuk/bilinmeyen şema, atomik kayıt | ✅ |
| Servis hatası/timeout/geçersiz cevapta eski kodun korunması | ✅ |
| Boş cevabın gerçek kod gibi kaydedilmemesi | ✅ |
| Tekilleştirme, iptal, erken duruş, hata ayrıntısının raporlanması | ✅ |
| `(—)` yer tutucusu, göreli yol anahtarı, yer tutucunun diske yazılmaması | ✅ |
| Baştaki sıfırların korunması | ✅ |
| Kod güncellemesinin embedding index'ini değiştirmemesi | ✅ |
| WSDL'de doğrulanan sözleşmenin istemcide aynen uygulanması | ✅ (P69) |

> Sahte servis testleri canlı doğrulamanın **yerine geçmez**; canlı doğrulama
> da sahte servis testlerinin yerine geçmez. İkisi ayrı raporlanır.

### Arayüz

| | Durum |
|---|---|
| Canlı arayüz görünümü | ❌ uygulama açılmadı (kullanıcının izni olmadan çalıştırılmaz) |

## İlgili Dokümanlar

- Kararlar: `docs/DECISIONS.md`
- Çok modelli arama: `docs/MULTI_MODEL_SEARCH.md`
- Mimari: `docs/ARCHITECTURE.md`
- Dağıtım: `docs/DEPLOYMENT.md`
