# Third-Party Notices

Bu doküman, Lens Desktop uygulamasının (shipped runtime) ve geliştirme
araçlarının kullandığı üçüncü taraf bağımlılıkları listeler.

## Shipped Application (Lens.Desktop / Lens.Core)

| Paket | Sürüm | Lisans | Not |
|---|---|---|---|
| [Microsoft.ML.OnnxRuntime](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime) | 1.20.1 | MIT | ONNX model inference (CLIP vision encoder) |
| [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp) | 3.1.12 | **Six Labors Split License** (MIT değil) | Görsel decode/resize/preprocessing |

### ⚠️ SixLabors.ImageSharp — Lisans Uygunluğu Teyit Edilmeli

`SixLabors.ImageSharp`, çoğu .NET paketinin aksine **MIT lisanslı değildir**.
"Six Labors Split License" adında, ticari kullanım için belirli bir kurum
büyüklüğü/ciro eşiğinin üzerinde **ücretli bir ticari lisans** gerektirebilen
bir model kullanır (küçük işletmeler/bireysel geliştiriciler için ücretsiz
kullanım tanımlanmıştır, ama tam eşik rakamları zamanla değişebilir).

**Bu doküman şu anki tam eşik rakamlarını (ciro/çalışan sayısı vb.) teyit
etmemiştir ve uydurmamaktadır.** Lens fabrikada gerçekten üretime alınmadan
önce, mevcut lisans şartları resmi kaynaktan (https://sixlabors.com/pricing/
ve/veya paket içindeki `LICENSE` dosyası) kontrol edilmeli ve şirketin bu
şartlara uygun olup olmadığı (gerekirse ticari lisans satın alınıp
alınmayacağı) **açıkça karara bağlanmalıdır**. Bu, `docs/PRODUCTION_CHECKLIST.md`'de
bir açık madde olarak işaretlenmiştir.

## CLIP Modeli

Model dosyası (`models/clip-vision-b16-openai.onnx`) `openai/clip-vit-base-patch16`
Hugging Face modelinden export edilmiştir. Model lisansı bu dosyada tekrar
edilmez — güncel ve doğrulanmış bilgi için `docs/MODEL_CARD.md` "Model
Lisansı" bölümüne bakın (henüz teyit edilmemiş, uydurulmamıştır).

## Yalnızca Geliştirme/Benchmark Aracı (shipped app'in PARÇASI DEĞİL)

Aşağıdaki Python bağımlılıkları yalnızca `benchmark/` altındaki model
export/değerlendirme scriptleri için kullanılır (`requirements-benchmark.txt`).
Lens.exe bunları çalıştırmaz, dağıtılan publish paketine dahil değildirler:

| Paket | Sürüm | Kullanım |
|---|---|---|
| torch | 2.13.0+cpu | Model yükleme, ONNX export |
| transformers | 5.16.1 | CLIP/SigLIP model tanımı |
| pillow | 12.3.0 | Görsel I/O (benchmark) |
| numpy | 2.2.6 | Sayısal işlemler (benchmark) |
| sentencepiece | 0.2.2 | SigLIP tokenizer bağımlılığı |
| protobuf | 7.36.0 | Transitive bağımlılık |
| onnx | 1.17.0 | ONNX export doğrulama |
| onnxruntime | 1.20.1 | Export sonrası doğrulama |

Bu paketlerin lisans şartları, yalnızca bu araçları **çalıştıran
geliştiriciyi** ilgilendirir — dağıtılan üründe bulunmadıkları için üretim
lisans yükümlülüğü oluşturmazlar. Yine de bir CI/geliştirme ortamı kurulumunda
dikkate alınmalıdır.

## Güncelleme Notu

Bir bağımlılık eklendiğinde/sürümü değiştiğinde bu dosya güncellenmelidir
(bkz. `CONTRIBUTING.md` "Yeni Bir Bağımlılık Eklemek İstiyorsanız").
