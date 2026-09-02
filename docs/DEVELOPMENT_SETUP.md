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

`hardeningtest`, kendi geçici klasörlerini oluşturup temizler — repoya veya
`%LocalAppData%\Lens\`'e kalıcı bir iz bırakmaz.

## 8. Publish

```
dotnet publish src/Lens.Desktop/Lens.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/<hedef-klasör>
```

Detay ve dağıtım süreci: `docs/DEPLOYMENT.md`.

## 9. `%LocalAppData%\Lens\` Altında Biriken Dosyalar

Geliştirme sırasında uygulamayı her çalıştırdığınızda şu klasör yapısı oluşur/güncellenir:

```
%LocalAppData%\Lens\
├── config\      (kullanıcı override ayarları, JSON)
├── cache\<hash>\index.json   (her denediğiniz ürün klasörü için ayrı, klasör yoluna göre hash'lenmiş)
└── logs\lens-yyyyMMdd.log    (30 gün retention, otomatik temizlenir)
```

Bunlar **repo dışındadır**, git tarafından hiç görülmez. Temiz bir test
ortamı istiyorsanız bu klasörü elle silebilirsiniz — uygulama sonraki
açılışta yeniden oluşturur (bkz. `docs/ARCHITECTURE.md`).
