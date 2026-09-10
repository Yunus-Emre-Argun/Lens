# Desen Kodu Servisi ve Çevrimdışı Kod Gösterimi

Sonuç kartlarında dosya adının yanında desen kodunun gösterilmesi
(`desen_adi.jpg` **(00123)**) için eklenen iki akışlı çözümü anlatır.

> **Durum: deney dalı `feature/desen-code-service`.** `main` ve diğer pilot
> dalları değiştirilmemiştir.

## 1. Neden iki ayrı akış?

Normal kullanıcıların servise erişimi **yoktur** (VPN gerekir). Bu yüzden her
aramada servise bağımlı bir çözüm kurulmamıştır:

| Akış | Kim | Ne yapar |
|---|---|---|
| **Hazırlama** | VPN erişimi olan bilgisayar | ⋮ → **Desen Kodlarını Güncelle** ile kodları servisten alır, katalog yanındaki ortak dosyaya yazar |
| **Okuma** | Tüm kullanıcılar | Kodları o dosyadan **çevrimdışı** okur; servis erişimi gerekmez |

Servise erişilemiyorsa görsel arama normal çalışmaya **devam eder**; yalnızca
kod güncelleme işlemi kullanılamaz.

## 2. Servis sözleşmesi — DOĞRULANAN ve DOĞRULANAMAYAN

### ✅ Canlı doğrulandı

Uç `http://192.194.196.101:801/ozx.asmx` erişilebilir ve WSDL'i incelendi:

| | Değer |
|---|---|
| Namespace | `http://tempuri.org/` |
| SOAPAction | `http://tempuri.org/<MetotAdı>` |
| Binding | SOAP 1.1 (`Service1Soap`) ve SOAP 1.2 (`Service1Soap12`) |
| İstek | `<MetotAdı><parametre>değer</parametre></MetotAdı>` |
| Cevap | `<MetotAdıResponse><MetotAdıResult>metin</...></...>` |
| Kimlik doğrulama | **İstemiyor** — kimliksiz çağrı HTTP 200 döndü |

Bu desen, uygulamadaki istemcinin ürettiği zarfla **gerçek servise karşı
çalıştırılarak** doğrulandı: `HelloWorld` metodu HTTP 200 ve
`<HelloWorldResult>Hello World</HelloWorldResult>` döndü. Yani istemcinin
taşıma katmanı, SOAPAction biçimi ve cevap ayrıştırması **çalışır durumdadır**.

### ❌ Doğrulanamadı — `GetDesenKodu` bu uçta YOK

`ozx.asmx` **92 metot** yayınlıyor; `GetDesenKodu` bunlardan biri **değil**.
"desen / pattern / design / motif / dosya / file / image" geçen hiçbir metot
yok. Metotların tamamı üretim/ERP işleridir (iş emri, sevk, pastal, band,
performans).

Canlı kanıt:

```
POST /ozx.asmx   SOAPAction: "http://tempuri.org/GetDesenKodu"
→ HTTP 500
  <faultstring>Server did not recognize the value of HTTP Header
   SOAPAction: http://tempuri.org/GetDesenKodu.</faultstring>
```

Ayrıca `/desen.asmx` → 404, kök dizin → 403 (listeleme kapalı).

**Sonuç:** parametrenin gerçek adı, dönüş alanının adı ve dosya adının
uzantılı mı uzantısız mı gönderileceği **doğrulanamamıştır**. Aşağıdaki
istemci, aynı servisin kendi gözlemlenen geleneğini uygular — bu bir tahmin
değil, ama asıl metodun bu kalıbı izleyeceği de **garanti değildir**.

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

Alanlar boşsa servis **yapılandırılmamış** sayılır: güncelleme işlemi
çalışmaz, kayıtlı kodların gösterimi ve arama etkilenmez.

> Bu dosyaya **kimlik bilgisi yazılmaz**. Servis kimliksiz çağrıya yanıt
> vermektedir; VPN girişi ile servis kimlik doğrulaması **aynı şey değildir**.

## 3. Dosya adı eşleştirme

Varsayılan olarak yalnızca **dosya adı, uzantısıyla** gönderilir
(`desen_adi.jpg`). Tam yerel yol, UNC yolu veya dosya içeriği **asla**
gönderilmez.

`TryWithoutExtensionOnNotFound` açıkken, servis **açıkça "bulunamadı"**
derse uzantısız ad ikinci kez denenir. Bu fallback **bağlantı hatası, zaman
aşımı veya SOAP fault durumunda çalışmaz** — aksi halde geçici bir ağ sorunu
iki katına çıkar ve kalıcı "bulunamadı" gibi görünürdü. Hangi biçimin
eşleştiği metadata'ya (`QueriedFileName`) yazılır.

Dosya adından kod **tahmin edilmez**; adlar parçalanarak sorgulanmaz.

### Aynı adlı dosyalar — çözülmemiş belirsizlik

Metadata kayıtları katalog köküne göre **göreli yol** ile tutulur. Ancak
servis yalnızca dosya **adını** kabul ettiği için, farklı klasörlerdeki aynı
adlı dosyalar **aynı kodu alır**. İçerikleri farklıysa kodun hangisine ait
olduğu **kesinleştirilemez**.

Pratikte bugün bu durum oluşmaz: mevcut katalog taraması yalnızca üst dizini
okur (`Directory.EnumerateFiles`), alt klasör taranmaz. Bu görev tarama
kapsamını **değiştirmemiştir**. Alt klasör taraması ileride açılırsa bu
belirsizlik gerçek hale gelir.

## 4. Metadata dosyası

```
<ÜrünDizini>\.lens\metadata\desen-codes-v1.json    (kilit: desen-codes.lock)
```

Embedding indekslerinden (`.lens/index.json`, `.lens/indexes/*`) ve onların
kilitlerinden **tamamen ayrıdır**:

- Modelden bağımsızdır — CLIP/DINO seçimi veya renkli/gri ayarı değişince
  **yeniden sorgulama gerekmez**.
- Arama skorlarını **etkilemez**.
- Görsellerle birlikte taşındığında çalışmaya devam eder.
- Kod güncelleme ile indeksleme **birbirini bloklamaz** (ayrı kilitler).

### Şema (sürüm 1)

| Alan | Açıklama |
|---|---|
| `SchemaVersion` | 1. Farklı değer = bilinmeyen şema, dosya **kullanılmaz** |
| `SourceService` | Kodların hangi kaynaktan geldiği (izlenebilirlik) |
| `LastSuccessfulRefreshUtc` | En son **tamamlanan** güncellemenin zamanı |
| `Entries[].RelativePath` | Katalog köküne göre göreli yol (kayıt anahtarı) |
| `Entries[].QueriedFileName` | Servise gerçekte gönderilen ad |
| `Entries[].Code` | Desen kodu — **string**, baştaki sıfırlar korunur (`"00123"`) |
| `Entries[].State` | `Found` / `NotFound` |
| `Entries[].UpdatedUtc` | Kaydın son **başarılı** güncellenme anı |

Kod **string** tutulur: baştaki sıfırlar anlamlıdır ve kesin uzunluk
doğrulanmamıştır. Sayıya çevrilmez, sabit hane kısıtı konmaz.

## 5. Hata ve güncelleme davranışı

Üç durum **birbirinden ayrıdır** ve tek bir "başarısız"a indirgenmez:

| Durum | Anlamı | Davranış |
|---|---|---|
| `Found` | Geçerli kod geldi | Kod ve tarih güncellenir |
| `NotFound` | Servis "kod yok" dedi (boş cevap) | Kod boş kaydedilir; sonraki açık yenilemede tekrar denenir |
| `ServiceUnavailable` | Ulaşılamadı / zaman aşımı / SOAP fault | **Eski kod ve eski tarih aynen korunur** |
| `InvalidResponse` | Cevap ayrıştırılamadı | **Eski kayıt korunur** |

Ek kurallar:

- Boş cevap **gerçek kod gibi kaydedilmez**.
- Bağlantı hatası **"kod bulunamadı" diye kalıcılaştırılmaz**; kaydı olmayan
  bir dosya için hiç kayıt oluşturulmaz.
- Artık katalogda olmayan dosyaların kayıtları düşer — kod **başka bir
  dosyaya taşınmaz**.
- Yazım **atomiktir**; okuyucular yarım JSON görmez.
- Kataloğa yazılamazsa **açık hata** gösterilir; kullanıcı ayarları dizinine
  gizlice yazılmaz.

## 6. Kodları güncelleme işlemi

⋮ → **Desen Kodlarını Güncelle** (VPN erişimi olan bilgisayarda kullanılır).

- Normal aramada **asla otomatik tetiklenmez**.
- Mevcut katalog taramasının ürettiği dosya listesini kullanır.
- Aynı dosya adları **tekilleştirilir** (tek sorgu).
- Sıralı ve zaman aşımlıdır; **iptal edilebilir**.
- Sınırsız retry **yoktur**; art arda 5 bağlantı hatasında işlem **durur**
  (binlerce zaman aşımı beklenmez).
- İlerleme panelinde tamamlanan/toplam sorgu gösterilir; UI thread'i
  bloklanmaz; mevcut busy guard ile uyumludur.

Servise erişilemezse: *"Kod güncellemek için şirket ağı/VPN erişimi
gerekiyor"* mesajı gösterilir, arama çalışmaya devam eder.

## 7. Arayüz

- **Sonuç kartı:** dosya adının hemen altında, **kalın** `(00123)`.
  Dosya adı seçilebilir bir `TextBox` olduğu için (karar #54) içinde kalın
  parça olamaz; ayrıca kart ~130 DIP genişliktedir, satır içi gösterim uzun
  adlarda taşardı.
- **Seçilen Sonuç:** 320 DIP genişlik olduğundan kod dosya adının **yanında**
  gösterilir (`*` + `Auto` sütun — kod her zaman sığar, ad kendi sütununda
  kalır, taşma olmaz).
- Kod yoksa öge **tamamen gizlenir** — boş `()` gösterilmez.
- Kod/metadata durumu **tek bir yerde** bildirilir; kartlarda tekrar eden
  hata yazısı oluşmaz.
- Kod sonradan geldiğinde yalnızca kod alanları yeniden çizilir; **sıralama,
  seçim ve kaydırma konumu değişmez**.
- Klasör değişiminde eşleme tamamen yenilenir; eski klasörün kodları yeni
  sonuçlara **sızmaz**. Arama, başladığı andaki eşlemenin kopyasını kullanır.
- Sorgu görselinin kodu, dosya adına bakarak katalogdaki bir kayıttan
  **türetilmez**.

Model seçimi, ön işleme, benzerlik hesabı, eşik ve embedding verisi bu
işlemden **etkilenmez**.

## 8. Dağıtım

Metadata dosyası **görsellerle aynı klasörde** (`.lens\metadata\`) durur.

- **Paylaşılan katalog:** VPN'li bilgisayar bir kez günceller, tüm kullanıcılar
  aynı dosyayı okur.
- **Yerel katalog kopyaları:** görseller kopyalanırken `.lens\metadata\`
  klasörünün **de kopyalanması gerekir**; aksi halde o kopyada kod görünmez.
- Dosya **git'e ve kurulum paketine gerçek verilerle dahil edilmez**
  (`.gitignore` → `.lens/`).
- Merkezî bir erişim mekanizması **yoktur** ve varsayılmamıştır.

## 9. Doğrulanan / doğrulanamayan

| | Durum |
|---|---|
| Servis erişimi, WSDL, namespace, SOAPAction, SOAP 1.1/1.2 | ✅ canlı doğrulandı |
| İstemcinin zarfı + cevap ayrıştırması (gerçek `HelloWorld` metoduyla) | ✅ canlı doğrulandı |
| Kimlik doğrulama gerekmediği | ✅ canlı doğrulandı |
| **`GetDesenKodu` metodunun varlığı** | ❌ **serviste YOK** |
| Parametre adı, dönüş alanı, uzantılı/uzantısız biçim | ❌ doğrulanamadı |
| Gerçek kodlarla uçtan uca akış | ❌ doğrulanamadı |
| Metadata yazma/okuma, hata koruması, iptal, tekilleştirme | ✅ otomatik testlerle (sahte servis) |
| Canlı arayüz görünümü | ❌ uygulama açılmadı |

## İlgili Dokümanlar

- Kararlar: `docs/DECISIONS.md`
- Mimari: `docs/ARCHITECTURE.md`
- Dağıtım: `docs/DEPLOYMENT.md`
