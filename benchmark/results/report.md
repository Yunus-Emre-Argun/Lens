# Lens Faz 2 - CLIP vs SigLIP Benchmark Sonuclari

Kucuk olcekli, gosterge niteliginde bir benchmark (11 orijinal gorsel + sentetik varyasyonlar). Istatistiksel genel gecerlilik iddiasi yoktur.

## Genel Dogruluk (Top-K)

| Model | Top-1 | Top-3 | Top-5 | Sorgu sayisi |
|---|---|---|---|---|
| openai/clip-vit-base-patch16 | 98% | 100% | 100% | 55 |
| google/siglip-base-patch16-224 | 100% | 100% | 100% | 55 |

## Varyasyon Turune Gore Dogruluk (Top-1)

| Varyasyon | openai/clip-vit-base-patch16 | google/siglip-base-patch16-224 |
|---|---|---|
| brightness | 100% | 100% |
| contrast | 100% | 100% |
| crop | 100% | 100% |
| downscale_upscale | 91% | 100% |
| jpeg_quality | 100% | 100% |

## Zamanlama (CPU)

| Model | Model yukleme (bir kez) | Ort. embedding suresi | Ort. similarity+sort suresi | Ort. toplam sorgu suresi |
|---|---|---|---|---|
| openai/clip-vit-base-patch16 | 3.43 sn | 91 ms | 0 ms | 91 ms |
| google/siglip-base-patch16-224 | 2.89 sn | 91 ms | 0 ms | 91 ms |

Not: Model yukleme suresi bir kerelik maliyettir, sorgu basina tekrarlanmaz. "Toplam sorgu suresi" = embedding suresi + similarity/sort suresi.

Not: Yukaridaki "model yukleme" suresi, agirliklar zaten yerel diskte (Hugging Face cache) oldugu icin diski okuma suresidir. Ilk calistirmada agirliklar internetten indirilir (CLIP ~600 MB, SigLIP ~800 MB) ve bu tek seferlik indirme suresi burada olculmemistir; sadece sonraki yuklemeler icindir.