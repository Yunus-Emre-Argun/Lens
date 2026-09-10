# CLIP Desen Odaklı İyileştirme Deneyi

> **Durum: deney dalı `feature/clip-pattern-pilot`.** `main` ve DINOv2 pilot
> dalı değiştirilmemiştir. Bu doküman bir **ölçüm kaydıdır**, production
> model kararı değildir.

## 1. Soru

CLIP'in **ağırlıklarına dokunmadan**, yalnızca etrafındaki katmanları
(görüntü ön işleme, renk etkisi, kadraj, skor birleştirme) değiştirerek
doğru desenleri katalogda daha üst sıralara taşıyabilir miyiz? Ve bu, DINOv2'ye
alternatif oluşturur mu?

**CLIP ağırlıkları eğitilmedi, fine-tuning yapılmadı, yeni model indirilmedi.**
Kullanılan dosya mevcut `models/clip-vision-b16-openai.onnx`
(`openai/clip-vit-base-patch16`, revision `57c216476eefef5ab752ec549e440a49ae4ae5f3`,
SHA-256 `b75f9ea71a29fe3ad98406d63986ad99a2714ae18fcbddcc48a664d151f40126`).

## 2. Ölçüm kurgusu

| | |
|---|---|
| Katalog | Kullanıcının gerçek klasörü — **2.007 desteklenen görsel** (2.152 dosya; 139 `.htm`, 2 svg, 2 gif, 1 kısmi indirme dahil 145'i desteklenmeyen) |
| Birebir kopya grubu | 22 |
| Sorgu | 24 kaynak görselden üretilen **336 deterministik dönüşüm** |
| Ayrım | Kaynak bazında **dev 12 / val 12** — aynı desenin dönüşümleri tek kümede kalır, yapay başarı olmaz |
| Metrikler | R@1, R@20, R@100, MRR, doğru hedefin sırası, doğrulanmamış en yakın rakibe göre pay |
| Donanım | Aynı makine, CPU, her yöntem için aynı koşullar |

**İki doğruluk güvencesi kuruldu:**

1. **Baseline gerçekten baseline mi?** Deneydeki `rgb.center` görünümünün
   üretimdeki `ImagePreprocessor` çıktısıyla **bit düzeyinde aynı** olduğu her
   çalıştırmada doğrulanır; eşleşmezse deney durur. Aksi halde ölçülen
   "iyileşme", baseline'ı yanlış kurmuş olmaktan geliyor olabilirdi.
2. **Birebir kopya farkındalığı.** Sorgu, kaynağın bir kopyasını bulursa
   **doğru** sayılır. Kopyayı "yanlış" saymak yapay bir başarısızlık üretirdi.

### Bilinen gerçek eşleşme çifti — yalnızca TEK yön geçerli

Kullanıcının bildirdiği çiftin **yalnızca biri** katalogda: diğeri katalogda
birebir kopya olarak bulunuyor. Bu yüzden sorgu = katalogda **olmayan** dosya,
hedef = katalogdaki dosya olarak tek yönde ölçüldü. Ters yönde sorgu kendi
kopyasını %100 ile bulurdu — bu ölçüm değil, totolojidir.

Bu tek veri noktası **yöntem seçiminde kullanılmadı** (aşırı uyum riski),
yalnızca raporlanır.

## 3. Elenen yöntemler (negatif bulgular)

| Yöntem | val R@1 | Karar |
|---|---:|---|
| Baseline (bugünkü) | 85,1% | referans |
| **3×3 ızgara (9 görünüm)** | 87,5%\* | **Elendi** — küçük hücreler genel yapıyı kaybediyor; 9× indeks maliyetine karşılık kazanç yok |
| **Gri tonlama tek başına** | 86,3%\* | **Elendi** — "rengi atarsak desen kalır" sezgisi ölçümde tutmadı |
| **Sorgu tarafında 4 dönüş** | 93,5%\* | **Elendi** — maksimum alma yanlış adayların skorunu da yükseltiyor |

\* Bu üç satır 72 görsellik eleme havuzundandır (baseline orada 94,0%).

## 4. Tam katalog sonuçları (2.007 görsel)

| Yöntem | Görünüm/görsel | dev R@1 | val R@1 | val R@20 | val MRR | val medyan pay |
|---|---:|---:|---:|---:|---:|---:|
| `A0` CLIP baseline | 1 | 82,1% | 85,1% | 94,0% | 0,880 | 0,050 |
| `A3` + whitening | 1 | 86,3% | 85,7% | 97,6% | 0,895 | — |
| `C2` çok ölçekli | 3 | 89,9% | 91,7% | 98,2% | 0,937 | — |
| `E2` çok ölçekli + whitening | 3 | 92,3% | 91,7% | 98,2% | 0,942 | — |
| `C3` 5 örtüşen bölge | 5 | 90,5% | 94,0% | 99,4% | 0,961 | 0,040 |
| `C6` global + 5 bölge | 6 | 92,3% | 93,5% | 99,4% | 0,955 | 0,058 |
| `E3` 5 bölge + whitening | 5 | 94,0% | 92,9% | 99,4% | 0,952 | 0,184 |
| **`E4` global + 5 bölge + whitening** | **6** | **97,6%** | **96,4%** | **99,4%** | **0,976** | **0,244** |
| — | | | | | | |
| **DINOv2-Base (üretim)** | **1** | 95,2% | 93,5% | 98,8% | 0,953 | 0,195 |
| DINOv2-Base + whitening | 1 | 95,8% | 94,0% | 99,4% | 0,955 | 0,272 |

> **Uyarı:** Farklı yöntemlerin kosinüs yüzdeleri **aynı anlamı taşımaz**;
> whitening skor ölçeğini değiştirir. Karşılaştırılabilir olan **sıra tabanlı**
> ölçülerdir (R@k, MRR). "Pay" sütunu yalnızca aynı normalizasyona sahip
> satırlar arasında kıyaslanmalıdır.

### Bilinen gerçek çift (tek yön)

| Yöntem | Sıra | Doğru skor | Rakip | Pay |
|---|---:|---:|---:|---:|
| CLIP baseline | 1 | 0,922 | 0,916 | **+0,006** |
| CLIP `C1` letterbox | **2** | 0,922 | 0,926 | −0,004 |
| CLIP `C3` | 1 | 0,952 | 0,925 | +0,027 |
| CLIP `E4` (seçilen) | 1 | 0,677 | 0,600 | +0,077 |
| **DINOv2-Base** | 1 | 0,934 | 0,837 | **+0,097** |

Bugünkü CLIP bu çifti 1. sırada buluyor **ama payı 0,006** — gürültü
seviyesinde. Kadrajı değiştiren bir varyant onu 2. sıraya düşürebiliyor.

## 5. Seçilen yöntem: `E4` — global + 5 örtüşen bölge + whitening

Uygulamaya alınan yapılandırma:

1. **Kadraj:** tam görüntü (merkez crop) + kenarın %60'ı boyutunda **5
   örtüşen bölge** (merkez ve 4 köşe) → görsel başına **6 görünüm**. Örtüşme,
   bir motifin tam sınırda bölünüp hiçbir bölgede bütün kalmamasını önler.
2. **Normalizasyon:** her görünüm **ayrı** L2-normalize edilir; arama anında
   katalog ortalaması çıkarılıp yeniden normalize edilir (whitening).
3. **Skor:** `0,5 × global benzerlik + 0,5 × en iyi görünüm çifti`.
   Yalnızca "en iyi bölge"ye bakmak, tek bir küçük ortak motifin tüm
   eşleşmeyi taşımasına izin verirdi; yalnızca global'e bakmak kısmi deseni
   kaçırırdı.

**Neden seçildi:** en yüksek R@1 (val 96,4 / dev 97,6), en yüksek MRR (0,976),
ve doğru–rakip ayrımı baseline'ın **5 katı** (0,050 → 0,244). Görünüm sayısı,
baştan konan ≤10 sınırının altında.

Whitening ortalaması **index dosyasında saklanmaz** — katalog değiştiğinde
ortalama da değişir, saklanan değer sessizce eskir. Her aramada yeniden
hesaplanır (5.000 kayıtta ~15 milyon işlem; sorgunun kendi embedding
maliyetinin yanında ihmal edilebilir).

### Ölçülen maliyet

| | CLIP baseline | **CLIP `E4`** | DINOv2-Base |
|---|---:|---:|---:|
| Görsel başına embedding | 1 | **6** | 1 |
| Ölçülen embedding süresi | ~0,26 sn | **~1,54 sn** | ~0,46 sn |
| 2.007 görsel indeksleme (ölçüldü) | ~9 dk | **~51 dk** | ~15 dk |
| 5.000 görsel (**tahmin**) | ~21 dk | **~2,1 saat** | ~37 dk |
| Kayıt başına vektör | 512 | **3.072** | 768 |
| İndeks boyutu (DINOv2'ye göre) | 0,67× | **4×** | 1× |
| Sorgu gecikmesi (embedding) | ~0,26 sn | **~1,54 sn** | ~0,46 sn |

## 6. DINOv2'ye alternatif mi? — Dürüst cevap

**Kısmen. Sentetik dönüşüm metriklerinde evet, maliyet ve tek gerçek örnekte hayır.**

- ✅ CLIP **belirgin biçimde iyileştirilebiliyor**: val R@1 85,1 → 96,4
  (+11,3 puan), MRR 0,880 → 0,976, doğru–rakip ayrımı 5×.
- ✅ `E4`, DINOv2-Base'i sentetik metriklerde **geçiyor** (96,4 vs 93,5).
- ❌ Bunu **6 görünüm** karşılığında yapıyor: ~3,4× daha uzun indeksleme,
  4× indeks boyutu, ~3× sorgu gecikmesi.
- ❌ Elimizdeki **tek gerçek örnekte** DINOv2 daha iyi ayırıyor (+0,097),
  karşılaştırılabilir (whitening'siz) CLIP varyantlarının en iyisi +0,027'de
  kalıyor.
- ❗ **Whitening CLIP'e özgü bir kurtarma değil:** DINOv2'ye uygulandığında o
  da iyileşiyor (R@1 94,0, pay 0,195 → 0,272) ve **bedava**. DINOv2 +
  whitening (1 görünüm), CLIP `E4`'ün (6 görünüm) 2,4 puan gerisinde kalıyor
  ama 1/6 maliyetle ve gerçek çiftte daha iyi ayrımla.

**Sonuç:** Bu çalışma, "CLIP iyileştirilebilir mi?" sorusuna **evet** diyor;
"CLIP DINOv2'nin yerine önerilmeli mi?" sorusuna **bu verilerle hayır** diyor.
En dikkate değer aktarılabilir bulgu, **whitening'in her iki modelde de bedava
kazanç sağlaması**dır.

## 7. Doğrulanamayanlar

- **Asıl iş senaryosu doğrulanamadı.** "Düz desen örneği ↔ nevresime
  uygulanmış hâli" (kırışıklık, perspektif, arka plan) için etiketli veri
  diskte **yoktur**. Sentetik döndürme/kırpma bunu **kanıtlamaz**.
- Ground truth **tek gerçek çift** ile sınırlı; R@k ve MRR değerlerinin tamamı
  **sentetik** sorgulardan gelir ve dönüşüm dayanıklılığını ölçer, gerçek
  erişim başarısını değil.
- Aynalama (mirror) iş kuralı hâlâ belirsiz olduğu için ölçülmedi.
- Hız ölçümleri bu geliştirme makinesine aittir; hedef ofis bilgisayarında
  yeniden ölçülmelidir.
- Canlı arayüz açılmadı; görsel kabul kullanıcıyı bekliyor.

## 8. Eşik

Whitening skor ölçeğini değiştirir; CLIP'in eski %80'i ve DINOv2 pilotunun
%55'i bu yönteme **aynen taşınamaz**. Doğru hedeflerin listede kalma oranı:

| Eşik | %40 | %45 | %50 | **%55** | %60 | %65 | %70 | %80 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| CLIP baseline | 100% | 100% | 100% | 100% | 100% | 100% | 100% | 96% |
| **`E4` (seçilen)** | 100% | 99% | 98% | **96%** | 92% | 89% | 79% | 54% |

**%55 seçildi:** mevcut üretimin (CLIP @ %80) tutulma oranını (%96) **birebir
korur**, yani kullanıcının alıştığı "ne kadar filtreliyor" hissi değişmez.
Bu bir **kalibrasyon değildir** — kabul edilebilir yanlış pozitif oranı gerçek
katalogda ölçülmeden kesin değer belirlenemez.

> **Davranış notu:** whitening sonrası benzemeyen kayıtlar **negatif** skor
> alabilir. Bu yüzden %0 eşiği artık "her şey" anlamına gelmez. Doğru
> eşleşmeler yüksek skor aldığı için pratik kayıp oluşturmaz (testte
> doğrulandı: Grup O, O28/O28b).

## 9. İndeks ve profil

```
<ÜrünDizini>\.lens\indexes\clip-pattern-v1\index.json   (kilit: index.lock, aynı klasörde)
```

Eski CLIP (`.lens\index.json`) ve DINOv2 (`.lens\indexes\dinov2-base-v1\`)
dosyalarına **okunmaz, yazılmaz, silinmez** — hash karşılaştırmasıyla
doğrulandı.

Profil (şema sürümü **3**) şunları taşır ve herhangi biri değişirse index
**tamamen** yeniden oluşturulur:

| Alan | Değer |
|---|---|
| Model kimliği | `openai/clip-vit-base-patch16` |
| Revision | `57c216476eefef5ab752ec549e440a49ae4ae5f3` |
| Model SHA-256 | `b75f9ea71a29fe3ad98406d63986ad99a2714ae18fcbddcc48a664d151f40126` |
| Ön işleme sürümü | `clip-pattern-center-overlap5-v1` |
| Embedding boyutu | `3072` (6 görünüm × 512) |
| Özellik türü | `ProjectedImageEmbeds-x6-global+overlap5` |
| Crop stratejisi | `CenterCrop224+Overlap5@0.60` |
| Normalizasyon | `L2-per-view+catalog-mean-whitening` |

Görünümler **birleşik tek vektör** olarak saklanır (512'lik bloklar ard arda).
Böylece mevcut index/kilit/atomik-yazma altyapısı **kopyalanmadan** yeniden
kullanılır; okurken `PatternSimilaritySearch` vektörü bloklara böler.
Normalizasyon **görünüm bazındadır** — birleşik 3072'lik vektörü toplu
normalize etmek görünüm benzerliğinin anlamını bozardı (test: Grup O, O41).

## 10. Yeniden üretilebilir komutlar

```
# Eleme turu (küçük havuz, hızlı)
Lens.AiProof clippattern <katalog> <çalışma-dizini> elim 300

# Tam katalog, seçili stratejiler
Lens.AiProof clippattern <katalog> <çalışma-dizini> full 2007 "A0_baseline_rgb,E4_center+overlap5_whiten"

# Aynı koşullarda DINOv2 karşılaştırması
Lens.AiProof clippattern <katalog> <çalışma-dizini> full 2007 "A0_baseline_rgb" "" "" dinov2
```

Katalog klasörüne **hiçbir şey yazılmaz**; tüm çıktılar (sorgu görselleri,
embedding önbelleği, raporlar) git ve katalog dışındaki çalışma dizinine gider.
Embedding'ler görünüm bazında önbelleklenir — tekrar çalıştırmalar saniyeler
sürer.

## İlgili Dokümanlar

- Kararlar: `docs/DECISIONS.md`
- Model kartı: `docs/MODEL_CARD.md`
- DINOv2 pilot ölçümleri: `docs/MODEL_BENCHMARK.md`
- Dağıtım: `docs/DEPLOYMENT.md`, `docs/CLICKONCE.md`
