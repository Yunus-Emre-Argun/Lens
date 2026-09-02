# Veri İşleme / Gizlilik Notu

Bu, harici kullanıcılara yönelik bir gizlilik politikası değildir — Lens
dahili bir fabrika aracıdır. Bu doküman, hangi verinin nerede tutulduğunu
ekip içi netlik için özetler.

## Hangi Veri, Nerede

| Veri | Nerede tutulur | Dışarı çıkar mı? |
|---|---|---|
| Ürün görselleri (orijinal) | Kullanıcının seçtiği yerel klasör veya UNC ağ paylaşımı | **Hayır** — Lens bunları hiçbir zaman kopyalamaz/taşımaz/yüklemez |
| Görsel embedding'leri (float[512]) | `%LocalAppData%\Lens\cache\<hash>\index.json` | **Hayır** — yalnızca yerel diskte |
| Dosya adı + boyut + son değişiklik zamanı | Aynı cache dosyası (embedding ile birlikte) | **Hayır** |
| İşlem logları (dosya adı, format, hata sebebi) | `%LocalAppData%\Lens\logs\lens-yyyyMMdd.log` | **Hayır** |
| Query (sorgu) görseli | Kullanıcının diskte seçtiği dosya; yalnızca bellekte işlenir | **Hayır** — kaydedilmez, cache'e girmez |

## Dış Servis Kullanımı

**Yok.** Embedding üretimi tamamen yerel makinede, CLIP ONNX modeli ile CPU
üzerinde çalışır (bkz. `docs/DECISIONS.md` #6 — PoC kapsamında dış AI servisi
kullanılmayacağı onaylandı). İnternet bağlantısı gerekmez.

## Loglarda Ne Var, Ne Yok

Log dosyaları dosya **adını**, uzantısını ve hata sebebini içerir (örn.
`urun123.jpg` okunamadı, sebep: ...). Log dosyaları görsel **içeriğini**
(piksel verisi, thumbnail) hiçbir zaman içermez.

## Ürün Klasörü Seçerken Dikkat

- Kullanıcı, gerçek ürün görsellerini içeren bir UNC yolunu seçtiğinde, bu
  yol yalnızca `%LocalAppData%\Lens\config\` içinde (kullanıcı override
  olarak, isterse) veya `appsettings.json`'da (admin default olarak) yerel
  makinede saklanır — bir sunucuya gönderilmez.
- **Geliştirme/test sırasında gerçek fabrika UNC yollarını `appsettings.json`'a
  yazıp commit etmeyin** (bkz. `SECURITY.md`, `docs/DEVELOPMENT_SETUP.md` §5).

## Benchmark/Test Verisi

`benchmark/` altındaki gerçek ürün görselleri (`nevresim/`, `benchmark/data/raw`,
`benchmark/data/variations`, `benchmark/data/distractors`) `.gitignore` ile
commit dışı bırakılmıştır (bkz. `docs/DECISIONS.md` #28). Yalnızca sonuç
özetleri (skorlar, süreler) ve dosya adı metadata'sı commit edilir — bu
metadata'da gerçek dosya adı kalıntıları bulunabileceği için yeni
benchmark verisi eklerken dosya adlarının hassas bilgi (gerçek müşteri/kişi
adı vb.) içermediğinden emin olun.
