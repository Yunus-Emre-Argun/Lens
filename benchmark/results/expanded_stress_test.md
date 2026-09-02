# Lens Faz 3C - Genisletilmis Veri Seti Stres Testi

Mevcut C#/.NET Lens.Core + CLIP ONNX pipeline'i (WPF MVP'de kullanilan ayni kod), 11 gercek urun + kullanicinin manuel olarak son haline getirdigi distractor seti ile test edildi. Model/algoritma degisikligi yapilmadi - amac mevcut sistemin buyuyen veri setinde gercek davranisini olcmekti.

Distractor kaynagi: Openverse (acik lisansli), kullanicinin manuel son duzenlemesiyle **177** gorsel. Toplam aday havuzu: **188**.

## Ozet Tablo

| Dataset | Top-1 | Top-3 | Top-5 | Avg Query (embed+sort) |
|---|---:|---:|---:|---:|
| 11 urun (baseline) | 98.2% | 100.0% | 100.0% | 51 ms |
| ~188 urun (+distractor) | 98.2% | 100.0% | 100.0% | 51 ms |

## Varyasyon Turune Gore Top-5 (genisletilmis havuz, ~188 aday)

| Varyasyon | Top-1 | Top-3 | Top-5 | Sorgu sayisi |
|---|---:|---:|---:|---:|
| brightness | 100% | 100% | 100% | 11 |
| contrast | 100% | 100% | 100% | 11 |
| crop | 100% | 100% | 100% | 11 |
| downscale_upscale | 91% | 100% | 100% | 11 |
| jpeg_quality | 100% | 100% | 100% | 11 |

## Indexing Performansi

- Distractor ilk indeksleme: 177 yeni gorsel, 10.5 sn toplam, ort. 60 ms/gorsel
- Distractor 2. calistirma (cache-hit dogrulamasi): yeni=0, degismeyen=177, 0.02 sn (persistent cache calisiyor)

## En Kotu 10 Query (genisletilmis havuzda)

### `sample_product_07__downscale_upscale.jpg` (downscale_upscale)
- Doğru ürün: `sample_product_07.jpeg`, sırası: **2** / 188
- Top-5:
  - 0.8897  `sample_product_11.jpeg`
  - 0.8815  `sample_product_07.jpeg` ← doğru ürün
  - 0.8403  `b1_0284_faded-mauve-vintage-photoshop-patterns-p.jpg` (distractor)
  - 0.8344  `b1_0003_bed-linen.jpg` (distractor)
  - 0.8202  `b1_0024_bed-sheet.jpg` (distractor)

### `sample_product_01__brightness.jpg` (brightness)
- Doğru ürün: `sample_product_01.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9915  `sample_product_01.jpeg` ← doğru ürün
  - 0.7957  `b1_0101_pattern-of-red-roses.jpg` (distractor)
  - 0.7852  `b1_0105_flower-bed.jpg` (distractor)
  - 0.7769  `b1_0264_free-hearts-patterns-twitter-backgrounds.jpg` (distractor)
  - 0.7492  `b1_0386_zentangle-6-3-12.jpg` (distractor)

### `sample_product_01__contrast.jpg` (contrast)
- Doğru ürün: `sample_product_01.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9930  `sample_product_01.jpeg` ← doğru ürün
  - 0.7977  `b1_0101_pattern-of-red-roses.jpg` (distractor)
  - 0.7838  `b1_0105_flower-bed.jpg` (distractor)
  - 0.7835  `b1_0264_free-hearts-patterns-twitter-backgrounds.jpg` (distractor)
  - 0.7630  `b1_0386_zentangle-6-3-12.jpg` (distractor)

### `sample_product_01__crop.jpg` (crop)
- Doğru ürün: `sample_product_01.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9724  `sample_product_01.jpeg` ← doğru ürün
  - 0.8126  `b1_0101_pattern-of-red-roses.jpg` (distractor)
  - 0.8013  `b1_0264_free-hearts-patterns-twitter-backgrounds.jpg` (distractor)
  - 0.7898  `b1_0105_flower-bed.jpg` (distractor)
  - 0.7504  `b1_0099_origami-paper.jpg` (distractor)

### `sample_product_01__downscale_upscale.jpg` (downscale_upscale)
- Doğru ürün: `sample_product_01.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9760  `sample_product_01.jpeg` ← doğru ürün
  - 0.7550  `b1_0101_pattern-of-red-roses.jpg` (distractor)
  - 0.7415  `b1_0386_zentangle-6-3-12.jpg` (distractor)
  - 0.7406  `b1_0105_flower-bed.jpg` (distractor)
  - 0.7377  `b1_0264_free-hearts-patterns-twitter-backgrounds.jpg` (distractor)

### `sample_product_01__jpeg_quality.jpg` (jpeg_quality)
- Doğru ürün: `sample_product_01.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9871  `sample_product_01.jpeg` ← doğru ürün
  - 0.7613  `b1_0101_pattern-of-red-roses.jpg` (distractor)
  - 0.7433  `b1_0105_flower-bed.jpg` (distractor)
  - 0.7432  `b1_0386_zentangle-6-3-12.jpg` (distractor)
  - 0.7357  `b1_0264_free-hearts-patterns-twitter-backgrounds.jpg` (distractor)

### `sample_product_03__brightness.jpg` (brightness)
- Doğru ürün: `sample_product_03.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9905  `sample_product_03.jpeg` ← doğru ürün
  - 0.8926  `b1_0029_webtreatsetc-abstract-warped-dots-seamle.jpg` (distractor)
  - 0.8736  `b1_0046_webtreats-tileable-organic-op-art-photos.jpg` (distractor)
  - 0.8713  `b1_0028_037-optical-after-effect.jpg` (distractor)
  - 0.8671  `b1_0288_purple-geometric-textile-pattern.jpg` (distractor)

### `sample_product_03__contrast.jpg` (contrast)
- Doğru ürün: `sample_product_03.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9993  `sample_product_03.jpeg` ← doğru ürün
  - 0.9011  `b1_0029_webtreatsetc-abstract-warped-dots-seamle.jpg` (distractor)
  - 0.8792  `b1_0028_037-optical-after-effect.jpg` (distractor)
  - 0.8773  `b1_0046_webtreats-tileable-organic-op-art-photos.jpg` (distractor)
  - 0.8732  `b1_0288_purple-geometric-textile-pattern.jpg` (distractor)

### `sample_product_03__crop.jpg` (crop)
- Doğru ürün: `sample_product_03.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9818  `sample_product_03.jpeg` ← doğru ürün
  - 0.8842  `b1_0029_webtreatsetc-abstract-warped-dots-seamle.jpg` (distractor)
  - 0.8604  `b1_0288_purple-geometric-textile-pattern.jpg` (distractor)
  - 0.8591  `b1_0046_webtreats-tileable-organic-op-art-photos.jpg` (distractor)
  - 0.8507  `b1_0028_037-optical-after-effect.jpg` (distractor)

### `sample_product_03__downscale_upscale.jpg` (downscale_upscale)
- Doğru ürün: `sample_product_03.jpeg`, sırası: **1** / 188
- Top-5:
  - 0.9815  `sample_product_03.jpeg` ← doğru ürün
  - 0.8969  `b1_0029_webtreatsetc-abstract-warped-dots-seamle.jpg` (distractor)
  - 0.8813  `b1_0288_purple-geometric-textile-pattern.jpg` (distractor)
  - 0.8674  `b1_0028_037-optical-after-effect.jpg` (distractor)
  - 0.8672  `b1_0046_webtreats-tileable-organic-op-art-photos.jpg` (distractor)

## Genel Gözlemler

- 55 sorgudan **0** tanesinde doğru ürün Top-5 dışına düştü (baseline'da bu sayı 0 idi).

_Not: Bu rapor CLIP'in ölçülen davranışını belgeler; ~189 adaylık bir gözlemdir, "1000+ üründe de böyle çalışır" sonucu çıkarılmamalıdır._
