# Çok Modelli ve Renkli/Gri Arama

> **Durum: deney dalı `feature/multi-model-search`.** `main` ve diğer pilot
> dalları değiştirilmemiştir. Kullanıcının görsel kabul testi **yapılmamıştır**.

Kullanıcı, ana ekrandaki **Arama Ayarları** panelinden kullanacağı modeli ve
görüntü değerlendirme biçimini seçebilir. Her arama **yalnızca seçilen tek
modelle** yapılır; iki modelin sonuçları birleştirilmez.

## 1. Varsayılan profil korunmuştur

| Ayar | Değer |
|---|---|
| Model | **DINOv2 Base** |
| Görüntü | **Renkli** |
| Desen odaklı karşılaştırma | **Kapalı** |
| Minimum benzerlik | **%55** |
| En fazla sonuç | 20 |
| Arama öncesi otomatik indeks kontrolü | Açık |

Bu profil, kullanıcının yaklaşık 5.000 görsellik gerçek katalog denemesinde
aranan deseni ilk sırada bulmuştur. **Bu bir canlı kabul gözlemidir, bağımsız
benchmark sonucu değildir.**

### Korunduğunun kanıtı (otomatik test — Grup Q)

| Kontrol | Sonuç |
|---|---|
| DINO renkli `EmbeddingProfile` önceki pilotunkiyle birebir aynı | ✅ Q1 |
| Index klasörü değişmedi (`dinov2-base-v1`) | ✅ Q2 |
| Model dosyası / kimlik / revision değişmedi | ✅ Q3 |
| 768 boyut, CLS token, L2 | ✅ Q4 |
| Ön işleme kimliği değişmedi | ✅ Q5 |
| Şema sürümü 2 (mevcut index geçerli) | ✅ Q6 |
| Eşik %55 | ✅ Q7 |
| **Ön işleme tensörü bit düzeyinde aynı** | ✅ Q40 |
| **Embedding bit düzeyinde aynı** | ✅ Q41 |
| **Aynı index üzerinde sıralama ve skorlar birebir aynı** | ✅ Q42 |
| Merkezleme kapalıyken mevcut arama yoluna aynen devredilir | ✅ Q17 |

**Not:** CLIP tarafında eski `ClipEmbedder` ile yeni ortak embedder arasında
`maxAbsDiff ≈ 6e-08` (tek ULP) fark ölçülmüştür. Sebebi, eski sınıfın
varsayılan ONNX oturum ayarlarını, yenisinin `allow_spinning=0` kullanmasıdır;
farklı paralel indirgeme sırası float32'de son bitte oynayabilir. **DINO
renkli tarafı bundan etkilenmez** — orada iki taraf da aynı ayarları kullandığı
için sonuç bit düzeyinde aynıdır.

## 2. Arayüz

Arama Ayarları paneline mevcut satırların **altına** eklendi (mevcut
kontrollerin `Grid.Row` değerleri değiştirilmedi, sayısal kutular 76 DIP
ölçüsünü korudu):

| Kontrol | Seçenekler | Varsayılan |
|---|---|---|
| **Model** | `DINOv2 Base` · `CLIP Standart` | DINOv2 Base |
| **Görüntü değerlendirme** | `Renkli` · `Gri tonlamalı` | Renkli |
| **Desen odaklı karşılaştırma** | onay kutusu | Kapalı |

Açılır listeler tam panel genişliğine yayılır (`ColumnSpan=2`) — 76 DIP'lik
sayısal kutu sütununa sığmazlar ve minimum pencerede taşma oluşmaz. Aktif
model/görüntü seçimi panelden okunur; ayrıca büyük bir başlık **eklenmemiştir**.

Tooltip'ler:
- Görüntü değerlendirme: *"Renkten bağımsız değerlendirme için gri tonlama uygular"* — garanti vermez.
- Desen odaklı: *"Katalogdaki ortak görsel özelliklerin etkisini azaltarak desenlerin daha belirgin karşılaştırılmasını sağlar."*

## 3. Seçenek değişim davranışları

**Model veya Renkli/Gri değişince:**
- Sorgu görseli **korunur** (aynı görselle iki yöntem karşılaştırılabilsin).
- Sonuç listesi, seçilen sonuç, sonuç sayısı temizlenir; kaydırma başa alınır.
- Önceki ONNX oturumu **kapatılır** — iki büyük model aynı anda bellekte
  tutulmaz. Yeni oturum ilk arama/indekslemede **arka planda** yüklenir.
- Seçilen profilin indeksi yüklenir; yoksa
  *"Bu model ve görüntü seçeneği için ilk indeksleme gerekiyor."*
- Eşik, o kombinasyonun kayıtlı/varsayılan değerine döner.

**Desen odaklı karşılaştırma değişince:**
- **Yeniden indeksleme gerekmez** — arama zamanı dönüşümüdür.
- Sorgu korunur, sonuçlar temizlenir, kullanıcı yeniden "Ara"ya basar.
- Katalog ortalaması önbelleği geçersizleşir.

**Arama/indeksleme sürerken** model, renk, desen odaklı seçimi mevcut busy
guard ile kilitlenir; değişiklik denenirse seçim geri alınır. İşlem bitince
veya hata verince kontroller yeniden kullanılabilir.

## 4. Renkli ve gri ön işleme

**Renkli:** her model kendi doğrulanmış ön işlemesini kullanır. DINO renkli
çıktısı önceki pilotla **birebir aynıdır**. Renk için ek ağırlık, histogram
veya ceza **eklenmemiştir**.

**Gri:** görüntü, ölçekleme/kırpmadan **önce** griye çevrilir (tam
çözünürlükten hesaplansın diye); tek kanal modelin beklediği üç kanala eşit
kopyalanır (R=G=B); ardından modelin **kendi** normalizasyonu uygulanır.
Sorgu ve katalog **her zaman aynı renk modunda** değerlendirilir — karışık
karşılaştırma mümkün değildir (ayrı indeksler ve ayrı ön işleme kimliği).

> Gri modun daha iyi olduğu **iddia edilmemektedir**. Önceki CLIP deneyinde
> gri tek başına genel başarıyı düşürdü. Renk etkisinden bağımsız
> karşılaştırma isteyen kullanıcı için **seçilebilir bir alternatiftir**.

## 5. Ayrı indeksler

| Profil | Index yolu |
|---|---|
| DINO renkli | `.lens/indexes/dinov2-base-v1/index.json` ← **değişmedi** |
| DINO gri | `.lens/indexes/dinov2-base-gray-v1/index.json` |
| CLIP renkli | `.lens/indexes/clip-standard-rgb-v1/index.json` |
| CLIP gri | `.lens/indexes/clip-standard-gray-v1/index.json` |

Her indeksin kilidi **kendi klasöründedir**. Şunlara dokunulmaz: eski CLIP
`.lens/index.json`, CLIP Desen Pilotu indeksi, desen kodu metadata dosyası.

Profil uyuşmazlığında (model, SHA-256, boyut, renk modu, ön işleme,
normalizasyon, şema) embedding'ler **kullanılmaz**, ilgili indeks yeniden
oluşturulur. Kullanıcı bir profil seçtiğinde **yalnızca o profil** indekslenir;
dört indeks arka arkaya hazırlanmaz.

## 6. Desen odaklı karşılaştırma = embedding merkezleme ve yeniden normalleştirme

**Teknik adlandırma:** burada kovaryans dönüşümü **yapılmaz**; yalnızca
katalog ortalaması çıkarılıp vektörler yeniden L2-normalize edilir. Bu **tam
whitening değildir** ve öyle sunulmamaktadır.

Akış: seçili indeksin ham embedding ortalaması hesaplanır → **aynı ortalama**
hem sorgudan hem katalog kayıtlarından çıkarılır → yeniden L2 normalize edilir
→ kosinüs bu vektörlerle hesaplanır. **Ham indeks değişmez.**

### Ortalama önbelleği ve geçersizleşmesi

Önbellek, hesaplandığı **kayıt listesi nesnesinin kimliğine** bağlıdır.
Uygulama, indeksi her yüklediği/güncellediği noktada **yeni bir liste** atar;
bu yüzden şu durumların hepsinde önbellek kendiliğinden düşer — elle temizleme
çağrısı unutulamaz:

katalog klasörü değişince · aktif model değişince · renkli/gri değişince ·
indeks güncellenince · görsel eklenince/silinince · indeks yeniden yüklenince

Ayrıca profil değişiminde açıkça `Invalidate()` çağrılır (ek savunma katmanı).

### Sınır durumları — sessizce ham skora dönülmez

| Durum | Davranış |
|---|---|
| Boş katalog | Uygulanamaz; kullanıcıya bildirilir |
| Tek kayıt | Uygulanamaz (ortalama = kaydın kendisi) |
| Birbirinin aynı embedding'ler | Uygulanamaz; anlaşılır neden |
| Sıfıra yakın merkezlenmiş vektör | O kayıt atlanır; hepsi öyleyse uygulanamaz |
| NaN/Infinity | Ortalama hesaplanmaz → uygulanamaz |
| Boyut uyuşmazlığı | **Açık hata** (`InvalidEmbeddingException`) |

Merkezleme uygulanamazsa arama **durdurulur** ve nedeni gösterilir — farklı
bir yöntemin sonucu "desen odaklı" etiketiyle gösterilmez.

Bu seçenek sıralamayı ve skor dağılımını **değiştirir**; yüzdeler açık/kapalı
modlar arasında karşılaştırılamaz.

## 7. Eşikler

Eşik **model + renk modu + merkezleme** kombinasyonu başına saklanır
(`UserSettings.ThresholdByProfile`, anahtar `Model|Renk|centered/raw`).
Kullanıcı DINO renkli standart profiline döndüğünde önceki değerini görür.

| Kombinasyon | İlk kullanım varsayılanı | Gerekçe |
|---|---|---|
| DINO renkli, merkezleme kapalı | **%55** | Kanıtlanmış profil (değişmedi) |
| CLIP renkli, merkezleme kapalı | **%80** | CLIP'in tarihsel varsayılanı |
| DINO gri | **%50** | Gri, renk bilgisini kaldırdığı için skorlar aşağı kayar; doğru eşleşmeleri gereksiz elememek için renkliden 5 puan düşük |
| CLIP gri | **%70** | Aynı gerekçe, CLIP'in %80'ine göre |

> Gri ve merkezlenmiş profillerin eşikleri **geçici, ölçüme dayalı başlangıç
> değerleridir — kalibrasyon değildir.** Kabul edilebilir yanlış pozitif oranı
> gerçek katalogda ölçülmeden kesin değer belirlenemez. Merkezleme açıkken
> ayrı bir anahtar kullanılır, çünkü skor ölçeği değişir.

En fazla sonuç ortak kalır: varsayılan 20, azami 999.

## 8. Alt klasör taraması

Katalog artık alt klasörleriyle birlikte taranır (`CatalogScanner`).

- Kayıt anahtarı artık dosya adı değil, katalog köküne göre **`/` ayraçlı
  göreli yol** — farklı alt klasörlerdeki aynı adlı görseller **ayrı kayıt**.
- **Kök seviyesi geriye uyumlu:** kökteki bir dosyanın göreli yolu yine
  yalnızca dosya adıdır → mevcut ~5.000 görsellik DINO indeksi **geçerli
  kalır**, yeniden embed edilmez.
- `.lens` klasörü taranmaz.
- Junction/symlink/reparse point **takip edilmez** (döngü riski); sorunlu
  listesine eklenir.
- Derinlik sınırı 16.
- **Kök** klasöre erişilemezse hata yukarı taşınır → `ScanError` → **mevcut
  indeks korunur**. (Bu kritik: sessizce "boş katalog" dönmek tüm kayıtların
  silinmiş sayılmasına ve indeks kaybına yol açardı.) **Alt** klasör sorunları
  taramayı durdurmaz, sorunlu listesine eklenir.
- Katalog dışına çıkan yollar kabul edilmez.

## 9. İndeksleme hızı — ölçüm

Ölçüm (bu makine, 20 çekirdek, 24 gerçek görsel, `Lens.AiProof ortbench`):

| Yapılandırma | Saf çıkarım | Döngüde çıkarım | Ön işleme | Toplam/görsel | 5.000 tahmini |
|---|---:|---:|---:|---:|---:|
| ONNX varsayılanı | 117 ms | 1085 ms | 51 ms | 1136 ms | ~95 dk |
| **`allow_spinning=0` (üretimde)** | 118 ms | **430 ms** | **17 ms** | **447 ms** | **~37 dk** |

`allow_spinning=0` ayarı `ProfiledImageEmbedder`'da uygulanmıştır ve
**2,5× hızlanma** sağlar. Bu ayar yalnızca thread bekleme politikasıdır;
sayısal çıktıyı değiştirmez (Q40–Q42 bit düzeyinde eşdeğerliği doğrular).

### Neden başka optimizasyon uygulanmadı

Ölçüm, **ön işlemenin toplam sürenin yalnızca %3,8'i** olduğunu gösteriyor
(17 / 447 ms). Dosya okuma, decode, resize ve normalizasyonu tamamen ortadan
kaldırsak bile kazanç %4'ün altında kalırdı. Asıl fark, aynı modelin **saf
çıkarımı 118 ms iken indeksleme döngüsünde 430 ms** olmasıdır; döngüdeki
**minimum 116 ms** olduğu için bu sabit bir ek maliyet değil, **varyanstır**
(zamanlayıcı/GC etkileşimi şüphesi — bu turda izole edilemedi).

Bu nedenle ön işleme tarafında spekülatif değişiklik **yapılmamıştır**:
ölçülmemiş bir kazanç için kanıtlanmış DINO yolunu değiştirmek doğru olmazdı.
Kalan fark **açık bir sınırlama** olarak raporlanmıştır.

Değiştirilmeyenler: girdi çözünürlüğü, model boyutu, embedding boyutu,
ön işleme kalitesi, sayısal hassasiyet/quantization.

## 10. Kullanıcı ayarları

Saklananlar: seçilen model, renkli/gri, desen odaklı karşılaştırma,
kombinasyon başına minimum benzerlik, en fazla sonuç, otomatik indeks tercihi.

Eski ayar dosyası yeni alanları içermiyorsa uygulama açılır ve **kanıtlanmış
DINO renkli profiline** geçer. Bilinmeyen/geçersiz değerlerde de aynı güvenli
varsayılana dönülür (`SearchModelCatalog.ResolveOrDefault`).

Bu pilotun veri klasörü **`%LocalAppData%\Lens.MultiModel\`** — diğer kurulu
pilotların ayarlarını ezmez. Klasör adı `LensDataFolder` assembly
metadata'sıyla derleme zamanında belirlenir; metadata yoksa davranış
öncekiyle birebir aynıdır (`Lens`).

## 11. Paketler

### Taşınabilir

```
publish/Lens.Desktop-win-x64-multi-model/     835 MB, 473 dosya
```

İçinde her iki model de bulunur:

| Model | Boyut | SHA-256 |
|---|---:|---|
| `dinov2-base.onnx` | 330,5 MB | `51014b029a9feaec58825836b0fa42b3b4aa86ae92dd35dd4db5d928dbff263d` |
| `clip-vision-b16-openai.onnx` | 329,0 MB | `b75f9ea71a29fe3ad98406d63986ad99a2714ae18fcbddcc48a664d151f40126` |

Bulunmayanlar (doğrulandı): PDB, yerel geliştirici yolu, ürün görselleri,
kullanıcı ayarları, indeksler, loglar, desen kodu metadata'sı, servis/VPN
kimlik bilgisi. `appsettings.json` boş şablondur.

### ClickOnce

```
publish/ClickOnce-multi-model/setup.exe       834 MB, 479 dosya
```

**Bağımsız kurulum kimliği** (manifestte doğrulandı):

| Paket | Kimlik |
|---|---|
| CLIP (orijinal) | `Lens.Desktop.application` |
| DINOv2 pilotu | `Lens.Desktop.application` |
| **Çok Modelli pilot** | **`Lens.Desktop.MultiModel.application`** |

`AssemblyName` profile özel olarak `Lens.Desktop.MultiModel` yapılmıştır;
ayrı klasör ve görünen adın tek başına yeterli olmadığı bilinerek. Görünen ad
`Lens (Çok Modelli Pilot)`, masaüstü kısayolu açık.

**Mevcut ClickOnce paketleri değişmedi** — `publish/ClickOnce/` ve
`publish/ClickOnce-dinov2-base/` klasörlerinde 478'er dosyanın tamamı öncesi/
sonrası SHA-256 listesiyle **bayt bayt aynı** doğrulandı.

Paket **kurulmadı** ve uygulama **açılmadı**. `InstallUrl`/`UpdateUrl` boş
olduğu için bu yerel/offline bir deneme paketidir; otomatik güncelleme
işlevsizdir ve tamamlanmış bir üretim dağıtımı değildir.

## 12. Bilinen sınırlamalar

- Gri ve merkezlenmiş profillerin eşikleri **geçici**; gerçek katalogda
  kalibre edilmedi.
- Gri modun genel başarıyı artırdığı **ölçülmedi**; önceki CLIP deneyinde
  gri tek başına düşürmüştü.
- İndeksleme döngüsündeki 118 → 430 ms farkı **açıklanamadı**.
- Hız ölçümleri bu geliştirme makinesine ait; hedef bilgisayarda yeniden
  ölçülmeli.
- Alt klasör desteği kod ve testle doğrulandı, **gerçek çok klasörlü katalogda
  denenmedi**.
- Canlı arayüz açılmadı; panel yerleşimi, DPI ve minimum pencere davranışı
  kullanıcı kontrolünü bekliyor.
- Desen kodu özelliği bu dalda **yoktu** (§17); sonuç kartı yerleşimi ileride
  kod eklenmesini engellemeyecek şekilde bırakılmıştı. **Güncelleme:** özellik
  daha sonra bu sürümün üzerine, `feature/desen-code-placeholder` dalında
  taşındı — kod eşlemesi alt klasör taramasıyla tutarlı olsun diye **göreli
  yol** anahtarını kullanır ve kodu alınamayan ürünlerde `(—)` yer tutucusu
  gösterilir. Arama, model/renk seçimi ve indeksleme davranışı bundan
  **etkilenmez**. Ayrıntı: `docs/DESEN_CODE_SERVICE.md`.

## İlgili Dokümanlar

- Kararlar: `docs/DECISIONS.md`
- Model kartı: `docs/MODEL_CARD.md`
- Mimari: `docs/ARCHITECTURE.md`
- Dağıtım: `docs/DEPLOYMENT.md`, `docs/CLICKONCE.md`
