# Geliştirme Ortamı Kurulumu

Bu doküman, Lens üzerinde çalışacak bir geliştiricinin sıfırdan çalışan bir
geliştirme ortamı kurması için adım adım talimat verir.

## 1. Gerekli Araçlar

| Araç | Sürüm | Not |
|---|---|---|
| Windows | 10/11 x64 | WPF uygulaması, yalnızca Windows'ta çalışır/derlenir |
| .NET SDK | 8.0 | `dotnet --version` ile kontrol edin; yoksa https://dotnet.microsoft.com/download/dotnet/8.0 |
| Git | herhangi bir güncel sürüm | |
| Python | 3.10+ (opsiyonel) | Yalnızca `benchmark/` altındaki model export/değerlendirme araçları için; Lens Desktop çalıştırmak için **gerekmez** |

## 2. Reponun Klonlanması

```
git clone <repo-url>
cd Lens
dotnet restore Lens.sln
```

## 3. Model Dosyasının Yerleştirilmesi

CLIP ONNX modeli repoda commit edilmemiştir (bkz. `docs/MODEL_CARD.md`).
İki seçenek:

**A) Paylaşılan bir kopyayı elle yerleştirme (önerilen, hızlı yol)**

Ekibinizden/önceki bir release'ten `clip-vision-b16-openai.onnx` dosyasını
alıp repo kökünde şu konuma koyun:

```
Lens\models\clip-vision-b16-openai.onnx
```

Mümkünse dosyanın SHA-256'sını `docs/MODEL_CARD.md`'deki kayıtlı hash ile
karşılaştırın (o alan henüz "release sırasında doldurulacak" olarak işaretli
olabilir — bu durumda karşılaştırma yapılamaz, bilginize).

**B) Kendiniz export etme (Python ortamı gerektirir)**

```
python -m venv .venv
.venv\Scripts\activate
pip install torch --index-url https://download.pytorch.org/whl/cpu
pip install -r requirements-benchmark.txt
python benchmark/export_onnx.py
```

Bu, `models/clip-vision-b16-openai.onnx` dosyasını üretir (~350 MB
mertebesinde). Detay: `docs/MODEL_CARD.md` "Export Yöntemi".

## 4. Örnek / Test Verisi Hazırlama

Uygulamayı denemek için gerçek fabrika verisi gerekmez — herhangi bir klasörde
birkaç `.jpg`/`.jpeg`/`.png` görseli yeterlidir:

```
mkdir C:\test-urunler
# birkaç görsel kopyalayın
```

Uygulama içinde "Ürün Klasörü Seç" ile bu klasörü seçip "İndeksi Güncelle"ye
basmanız yeterlidir. Gerçek ürün görselleri veya UNC yollarını repoya
**eklemeyin** (bkz. `SECURITY.md`, `docs/DATA_PRIVACY.md`).

Mevcut `benchmark/data/` altındaki sentetik test/karşılaştırma verisi yalnızca
model değerlendirme amaçlıdır; ham görseller `.gitignore` ile commit dışıdır
(bkz. `docs/DECISIONS.md` #28).

## 5. `appsettings.json` Yapılandırması

`src/Lens.Desktop/appsettings.json`, build çıktısına kopyalanır:

```json
{
  "AdminDefaultProductDirectory": ""
}
```

- **Boş bırakılırsa:** uygulama açılışta hiçbir klasör yüklemez, kullanıcı
  "Ürün Klasörü Seç" ile manuel seçer (geliştirme için önerilen durum).
- **Doldurulursa:** o klasör (yerel yol veya UNC) açılışta otomatik yüklenmeye
  çalışılır. Geliştirme ortamında buraya **gerçek fabrika UNC yolu yazmayın** —
  bu dosya commit edilir, gerçek yol repoya sızabilir.

Kullanıcının kendi seçtiği bir klasör override'ı `%LocalAppData%\Lens\config\`
altında ayrı tutulur (bkz. `docs/DECISIONS.md` #41-43) — `appsettings.json`'a
yazılmaz.

## 6. Uygulamayı Çalıştırma

```
dotnet run --project src/Lens.Desktop
```

veya Visual Studio/Rider ile `Lens.sln`'i açıp `Lens.Desktop`'ı başlatma
projesi yapıp F5.

## 7. Test ve Doğrulama

Ayrı bir birim test projesi yoktur (MVP/PoC kapsamı — bkz. `CLAUDE.md`).
Mevcut doğrulama araçları:

```
# Build kontrolü (0 warning/0 error beklenir)
dotnet build Lens.sln

# Reliability hardening fonksiyonel testleri (model gerektirir)
dotnet run --project src/Lens.AiProof -- hardeningtest

# Uçtan uca AI-proof (model + benchmark/data/variations gerektirir)
dotnet run --project src/Lens.AiProof

# Genişletilmiş stres testi (model + benchmark/data gerektirir)
dotnet run --project src/Lens.AiProof -- stresstest
```

`hardeningtest`, kendi geçici klasörlerini (`%TEMP%` altında) oluşturup
temizler — repoya veya `%LocalAppData%\Lens\`'e kalıcı bir iz bırakmaz.
[Faz 1] `dotnet run --project src/Lens.AiProof -- <klasör>` gibi manuel
çalıştırmalar ise o klasör içinde gerçek bir `.lens/index.json` oluşturur
(shared index, artık `%LocalAppData%` değil) — test amaçlı kullandıysanız
`.lens/` alt klasörünü elle silin (`.gitignore`'da zaten hariç tutuluyor,
commit riski yok, ama diskte kalır).

## 8. Publish

```
dotnet publish src/Lens.Desktop/Lens.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/<hedef-klasör>
```

Detay ve dağıtım süreci: `docs/DEPLOYMENT.md`.

## 9. Biriken Dosyalar: `%LocalAppData%\Lens\` ve `.lens\` (Faz 1'den beri iki ayrı yer)

**[Faz 1, 2026-09-03] Embedding index'i artık `%LocalAppData%` DEĞİL,
denediğiniz her ürün klasörünün KENDİ İÇİNDE** (`.lens/index.json`) tutulur
— bkz. `docs/ARCHITECTURE.md`, `docs/DECISIONS.md` #61.

```
%LocalAppData%\Lens\
├── config\      (kullanıcı override ayarları + auto-index tercihi, JSON)
└── logs\lens-yyyyMMdd.log    (30 gün retention, otomatik temizlenir)

<denediğiniz her ürün klasörü>\.lens\
├── index.json   (shared embedding index)
└── index.lock   (yalnızca yazma sırasında kısa süreli açık tutulan kilit dosyası)
```

`%LocalAppData%\Lens\` **repo dışındadır**, git tarafından hiç görülmez.
`<ürün klasörü>\.lens\` ise geliştirme sırasında repo İÇİNDEKİ bir test
klasörünü (örn. `benchmark/data/raw`) denerseniz o klasörün içinde oluşur —
`.gitignore`'da `.lens/` deseni ile hariç tutulur, commit riski yoktur ama
elle silmeniz gerekir (uygulama/`Lens.AiProof` bunu otomatik temizlemez).
Eski `%LocalAppData%\Lens\cache\<hash>\index.json` dosyaları (varsa,
önceki bir Lens sürümünden kalma) artık okunmuyor/yazılmıyor — temiz bir
test ortamı için elle silebilirsiniz, uygulama bunlara dokunmaz.

---

## [PİLOT dalı] DINOv2-Base modelini hazırlama

> Yalnızca `feature/dinov2-base-pilot` dalı için gereklidir. `main` dalı CLIP
> modelini kullanır ve bu adıma ihtiyaç duymaz.

Model dosyası (`models/dinov2-base.onnx`, ~330 MB) **git'e commit edilmez**
(bkz. `docs/DECISIONS.md` #28). Yeni bir geliştirme makinesinde şu adımlarla
yeniden üretilir:

1. Python sanal ortamı ve benchmark bağımlılıkları kurulu olmalı
   (`requirements-benchmark.txt` — `torch`, `transformers`, `onnx`,
   `onnxruntime`, `pillow`, `numpy`).

2. Dışa aktarma betiğini çalıştırın:

   ```
   python benchmark/export_dinov2_onnx.py
   ```

   Betik `facebook/dinov2-base` ağırlıklarını **pinlenmiş revision** ile
   (`f9e44c814b77203eaa57a6bdbbd535f21ede1415`) yükler — Hugging Face
   önbelleğinde yoksa bir kez indirir. CLS token döndüren minimal bir
   sarmalayıcıyı ONNX'e aktarır (opset 17, dinamik batch,
   `pixel_values` → `image_embeds`).

3. Betik bittiğinde şunları doğrulayın:
   - Dosya boyutu **330,5 MB**
   - SHA-256 **`51014b029a9feaec58825836b0fa42b3b4aa86ae92dd35dd4db5d928dbff263d`**
   - "PyTorch ve ONNX ciktilari sayisal olarak esdeger" satırı görünmeli.

   Betik uyuşmazlık bulursa hata verir ve model **kullanılmamalıdır**.

4. Mevcut bir dosyayı yeniden üretmeden doğrulamak için:

   ```
   python benchmark/export_dinov2_onnx.py --verify-only
   ```

**Not:** Bu betik yalnızca bir geliştirme aracıdır. Son kullanıcı makinesinde
Python **gerekmez**; uygulama modeli ONNX Runtime ile doğrudan yükler ve
çalışma zamanında internetten hiçbir şey indirmez.

### Pilot doğrulama ve tanılama modları

```
Lens.AiProof hardeningtest              # 301 kontrol (Grup N: DINOv2 profil/index/embedding)
Lens.AiProof dinosmoke [<dogrulama-cifti-klasoru>]
Lens.AiProof ortbench <yapilandirma>    # ONNX Runtime CPU thread tanilama
```

`dinosmoke`, dönüş/renk/gri/parlaklık/kısmi crop/ölçek/konum dayanıklılığını,
iş kuralı sıralamasını, eşik etkisini ve hızı gerçek üretim yolunda ölçer;
tüm ara dosyaları geçici klasörde üretir ve siler, gerçek klasörleri
kirletmez. İsteğe bağlı ikinci argüman bir doğrulama çiftinin bulunduğu
klasördür (dosyalar kopyalanmaz, raporda dosya adı geçmez).

`ortbench` her yapılandırmayı **ayrı bir süreçte** çalıştırmayı gerektirir
(aynı süreçte birden fazla ONNX oturumu ölçümü kirletir):

```
Lens.AiProof ortbench default
Lens.AiProof ortbench spin0
Lens.AiProof ortbench intra4
```

Hedef ofis bilgisayarında 5.000 görsellik indeksleme süresini tahmin etmek
için bu modu orada da çalıştırın (bkz. `docs/MODEL_CARD.md` hız bölümü).

---

## [PİLOT dalı] Desen odaklı CLIP deneyini yeniden çalıştırma

> Yalnızca `feature/clip-pattern-pilot` dalı için. Yeni model **indirilmez**;
> mevcut `models/clip-vision-b16-openai.onnx` kullanılır.

```
# Eleme turu (küçük havuz, hızlı)
Lens.AiProof clippattern <katalog> <çalışma-dizini> elim 300

# Tam katalog, seçili stratejiler
Lens.AiProof clippattern <katalog> <çalışma-dizini> full 2007 "A0_baseline_rgb,E4_center+overlap5_whiten"

# Aynı koşullarda DINOv2 karşılaştırması (son argüman model anahtarı)
Lens.AiProof clippattern <katalog> <çalışma-dizini> full 2007 "A0_baseline_rgb" "" "" dinov2
```

- Katalog klasörüne **hiçbir şey yazılmaz**; sorgu görselleri, embedding
  önbelleği ve raporlar yalnızca çalışma dizinine gider (git ve katalog
  dışında tutun).
- Embedding'ler **görünüm bazında** önbelleklenir; tekrar çalıştırmalar
  saniyeler sürer. Önbellek model başına ayrı klasördedir — aynı görünüm
  kimliği CLIP'te 512, DINOv2'de 768 boyutlu farklı bir vektördür.
- Deney, baseline görünümünün üretim ön işlemesiyle bit düzeyinde aynı
  olduğunu her koşuda doğrular; eşleşmezse durur.

Sonuçların yorumu ve doğrulanamayan noktalar: `docs/CLIP_PATTERN_EXPERIMENT.md`.
