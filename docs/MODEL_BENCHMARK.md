# Model Benchmark — Geniş Veri Desen Eşleştirme Ölçümü

**Test tarihi:** 2026-09-09
**Niteliği:** Araştırma/ölçüm çalışması. Production kaynak kodu, model dosyası,
index ve ClickOnce paketi bu çalışmada **değiştirilmemiştir**.

Bu doküman, Lens'in görsel eşleştirme modelinin geniş bir yerel veri kümesinde
ölçülmesini ve bir **pilot model adayının** seçilmesini kaydeder. Karar geçmişi
için `docs/DECISIONS.md`; üretimde **hâlen kullanılan** model için
`docs/MODEL_CARD.md`.

---

## 1. Problem tanımı

Lens'in çözmesi gereken soru genel görüntü benzerliği veya ürün kategorisi
benzerliği değildir:

> "Sorgulanan desen/motif, ürün görselinin herhangi bir yerinde uygulanmış mı?"

Bundan türeyen iş kuralları:

- Aynı desen **farklı renkte** ise eşleşme kabul edilir; renk sıralamayı
  belirlememelidir.
- Aynı renkte fakat **farklı motif** üst sıralara çıkmamalıdır.
- Desen **90°/180°/270°** çevrilmiş olsa da aynı kabul edilir.
- Hafif eğiklik, farklı çekim açısı ve perspektif sonucu bozmamalıdır.
- Desenin görselin **herhangi bir konumunda** bulunması yeterlidir; farklı
  ölçekte veya kısmen kadrajda olabilir.

Bu nedenle renk benzerliği başarı sayılmamış, ölçüt motif yapısı/geometrisi
olarak tanımlanmıştır.

---

## 2. Veri kümesi envanteri

Yerel geniş benchmark veri kümesi (üretim benzeri, masaüstünde tutulan toplu
görsel klasörü — tam yerel yol bilinçli olarak yazılmamıştır).

| Ölçüm | Değer |
|---|---|
| Toplam dosya | 2.154 |
| **Desteklenen görsel** | **2.007** (1.934 `.jpg`, 72 `.png`, 1 `.jpeg`) |
| Görsel olmayan dosyalar | 140 `.htm`, 2 `.gif`, 2 `.svg`, 1 `.json`, 1 `.crdownload` |
| Alt klasör | **Yok** — yalnızca Lens'in kendi `.lens` index klasörü |
| Özyinelemeli tarama | Uygulandı; alt klasör bulunmadığı için sonuç düz taramayla aynı |
| Bozuk/okunamayan görsel | **0** |
| Birebir kopya (SHA-256) | 22 grup, 23 fazladan dosya |
| Çözünürlük (medyan) | 735 × 720 |
| Çözünürlük aralığı | 150×160 – 8649×8000 |
| Kısa kenarı < 224 px | 59 görsel |
| Kare (±%2) oranı | %39,5 |
| Toplam boyut | 958 MB |

**Dosya adları desen kimliği olarak kullanılmamıştır.** Adlar stok görsel ve
ticari kumaş sitelerinden geliyor; aynı deseni gösteren iki dosyanın adı
tamamen farklı olabildiği gibi, farklı desenler benzer adlar taşıyabiliyor.
Ground truth bu nedenle **sentetik dönüşüm sorguları** üzerinden kurulmuştur
(bkz. §4).

### Bilinen gerçek çift

| Etiket | Durum |
|---|---|
| `known-pattern-query` | Veri kümesinde **yok** — katalog dışı sorgu olarak kullanıldı |
| `known-pattern-target` | Veri kümesinde **var** (2.007 görselden biri) |

Bu gerçekçi bir katalog testidir: sorgu katalogda bulunmayan bir çekim, hedef
katalogda duran görsel. **Ters yön** ölçümünde hedef katalogda bulunmadığından
aday havuzuna **yalnızca test amacıyla** eklenmiştir; bu **gerçek katalog
sırası değildir**.

---

## 3. Test edilen modeller

| Model | Kimlik | Revision | Ağırlık SHA-256 | Ağırlık boyutu | Lisans (kod / ağırlık) |
|---|---|---|---|---|---|
| CLIP ViT-B/16 (mevcut production) | `openai/clip-vit-base-patch16` | yerel ONNX export | ONNX SHA-256: `b75f9ea71a29fe3a…` (bu çalışmada ölçüldü) | 329 MB (ONNX) | Lisans teyidi `docs/MODEL_CARD.md`'de **açık madde** |
| **DINOv2 ViT-S/14** | `facebook/dinov2-small` | `ed25f3a31f01632728cabb09d1542f84ab7b0056` | `ae1e99fcefd534ed978cdeb8326f08030c96e28b7a81ffcbc98a857c84d14be1` | 88,2 MB | Apache-2.0 / Apache-2.0 |
| DINOv2 ViT-B/14 | `facebook/dinov2-base` | `f9e44c814b77203eaa57a6bdbbd535f21ede1415` | `d73036b56966966d07975d696bde331762f37297e2f095de8cea0040c3aa0841` | 346,3 MB | Apache-2.0 / Apache-2.0 |

Ağırlıklar yalnızca resmî Hugging Face `facebook/*` deposundan indirildi; toplam
indirme 434 MB. Lisanslar model kartı meta verisinden (`apache-2.0`) okundu.
**Modeller production kaynak koduna eklenmemiştir**; ağırlıklar yalnızca yerel
Hugging Face önbelleğinde durmaktadır.

Bu benchmark için üretilen ONNX dosyaları (repoya **commit edilmemiştir**):

| Dosya | Boyut | SHA-256 (ilk 32) |
|---|---|---|
| DINOv2-S ONNX | 88,4 MB | `fac422cc5359e13740a393d2ba8a7f52…` |
| DINOv2-B ONNX | 346,5 MB | `5233c5aa3f9cb1fa84f6d987a2400868…` |

> **Not:** Mevcut CLIP ONNX dosyasının resmî SHA-256'sı hâlâ kayıtlı değildir
> (bkz. `docs/MODEL_CARD.md` "SHA-256 Doğrulama Yaklaşımı") — bu açık madde bu
> çalışmayla kapanmamıştır.

### Ön işleme (her model kendi resmî değerleriyle)

| | CLIP ViT-B/16 | DINOv2 (S ve B) |
|---|---|---|
| Kısa kenar | 224 | **256** |
| Center crop | 224 | 224 |
| mean | 0,48145 / 0,45783 / 0,40821 | **0,485 / 0,456 / 0,406** |
| std | 0,26863 / 0,26130 / 0,27578 | **0,229 / 0,224 / 0,225** |
| Çıkış | `image_embeds` (projection head) | **CLS token** |
| Normalize | L2 | L2 |
| Embedding boyutu | 512 | **384** (S) / 768 (B) |

DINOv2 ön işleme değerleri modelin kendi `preprocessor_config.json` dosyasından
okunmuştur; CLIP'in normalizasyon sabitleri DINOv2'ye **taşınmamıştır**.
DINOv2 patch boyutu 14'tür (224 = 16×14 uyumlu), ancak CLIP'in "kısa kenar 224"
akışı aynen kullanılamaz — 256'ya ölçekleyip 224 crop gerekir.

---

## 4. Doğrulama yöntemi

Dosya adları desen kimliği taşımadığı için ground truth, **orijinal dosyalara
hiç dokunulmadan** üretilen deterministik sorgu varyasyonlarıyla kuruldu:

- 40 kaynak görsel (birebir kopyası olanlar ve kısa kenarı 400 px altındakiler
  hariç, sabit tohumlu seçim).
- Her kaynaktan 18 varyasyon → **720 sorgu**.
- Her varyasyonun doğru hedefi, kaynağın katalogdaki kendisidir.
- Dönüşümsüz birebir kopya sorgu **metriklere dahil edilmemiştir**.
- Ayna (mirror) ayrı raporlanır, metriklere dahil değildir — "aynı desen"
  sayılıp sayılmayacağı iş kuralı olarak henüz kesinleşmemiştir.

Varyasyonlar: `rot90`, `rot180`, `rot270`, `tilt±15`, `tilt±30`, `crop_center`,
`crop_topleft`, `crop_right`, `scale2x` (yakınlaştırma), `bright+40`,
`bright−35`, `contrast−40`, `hue+60`, `sat0_gray` (tam gri), `perspective`,
`mirror`.

Tüm modeller aynı sorgu görüntüleriyle, aynı Python/ONNX ön işleme boru
hattıyla ölçülmüştür (adil karşılaştırma). Üretimdeki .NET/ImageSharp boru
hattının ölçüm üzerindeki etkisi §8'de ayrıca ele alınmıştır.

---

## 5. Aşama 1 — ön eleme (300 görsellik örneklem)

> **Bu bölüm örneklem sonucudur, tam veri sonucu değildir.**

| Model | R@1 | R@5 | R@20 | R@100 | MRR | p95 |
|---|---:|---:|---:|---:|---:|---:|
| CLIP ViT-B/16 | %88,5 | %94,6 | %96,6 | %98,1 | 0,913 | 6 |
| DINOv2 ViT-S/14 | %94,4 | %98,4 | %99,0 | %99,9 | 0,962 | 2 |
| DINOv2 ViT-B/14 | %95,3 | %99,0 | %99,4 | %99,9 | 0,967 | 1 |

Aşama 1'de hiçbir model elenmedi; üçü de tam veri ölçümüne alındı.

---

## 6. Aşama 2 — TAM VERİ (2.007 görsel, 720 sorgu)

> **Aşağıdaki tüm sayılar tam veri üzerinde fiilen ölçülmüştür.**

| Model | R@1 | R@5 | R@20 | R@100 | MRR | medyan sıra | p95 sıra |
|---|---:|---:|---:|---:|---:|---:|---:|
| CLIP ViT-B/16 | %81,0 | %89,7 | %93,5 | %96,3 | 0,849 | 1 | **40** |
| **DINOv2 ViT-S/14** | **%88,7** | **%95,3** | **%97,8** | **%98,8** | **0,916** | 1 | **4** |
| DINOv2 ViT-B/14 | %89,9 | %96,2 | %98,8 | %99,3 | 0,927 | 1 | 4 |

### Bilinen gerçek çift — tam veri sırası

| Model | Yön 1 (sorgu katalog dışı) | Skor | Yön 2 (ters, hedef test amaçlı eklendi) | Skor |
|---|---:|---:|---:|---:|
| CLIP ViT-B/16 | **1** / 2.007 | 0,9237 | **1** | 0,9237 |
| DINOv2 ViT-S/14 | **1** / 2.007 | 0,9509 | **1** | 0,9509 |
| DINOv2 ViT-B/14 | **1** / 2.007 | 0,9342 | **1** | 0,9342 |

Üç model de bilinen çifti her iki yönde 1. sırada buluyor. **Bu tek çift model
seçimi için yeterli bir kanıt değildir** — ayrım 720 sorgulu dönüşüm setinde
ortaya çıkmaktadır.

Ayrıca üretimdeki .NET/ImageSharp boru hattıyla üretilmiş mevcut CLIP index'i
üzerinden de aynı çift kontrol edildi: her iki yönde 1. sıra, skor 0,9218 —
ancak ikinci sıradaki ilgisiz görselle arasındaki fark yalnızca **0,0060**.

### Varyasyon bazında R@1 (tam veri)

Değerler: R@1 (parantez içinde p95 sırası).

| Varyasyon | CLIP | DINOv2-S | DINOv2-B |
|---|---:|---:|---:|
| **rot90** | %92 (2) | **%100 (1)** | %95 (1) |
| **rot180** | %88 (**419**) | **%100 (1)** | %98 (1) |
| **rot270** | %95 (1) | **%100 (1)** | %90 (2) |
| tilt+15 | %92 (2) | %90 (2) | %95 (1) |
| tilt−15 | %95 (1) | %88 (4) | %92 (2) |
| tilt+30 | %75 (89) | %82 (9) | %90 (7) |
| tilt−30 | %72 (70) | %82 (14) | %85 (4) |
| crop_center | %72 (67) | %75 (11) | %80 (9) |
| crop_topleft | %48 (**921**) | %75 (54) | %72 (18) |
| crop_right | %50 (249) | %65 (140) | %68 (38) |
| scale2x | %45 (316) | %57 (173) | %68 (24) |
| bright+40 | %95 (1) | %98 (1) | %100 (1) |
| bright−35 | %100 (1) | %100 (1) | %100 (1) |
| contrast−40 | %100 (1) | %100 (1) | %100 (1) |
| hue+60 | %92 (2) | %98 (1) | %98 (1) |
| sat0_gray (tam gri) | %72 (23) | **%98 (1)** | **%100 (1)** |
| perspective | %92 (2) | **%100 (1)** | %98 (1) |
| *mirror (metrik dışı)* | *%95 (15)* | *%100 (1)* | *%100 (1)* |

**Öne çıkan bulgular:**

1. **Dönüş dayanıklılığı ham modelde mevcut ve DINOv2-S'te tamdır.** S varyantı
   90°/180°/270° dönüşlerin üçünde de %100 R@1 verdi; ilginç biçimde B varyantı
   bu üç senaryoda S'ten **daha zayıf** (%90–98). Bu, sorgu sırasında dört
   dönüş üretme (TTA) ihtiyacını ortadan kaldırır: index dört katına çıkmaz,
   sorgu dört kat yavaşlamaz.
2. **Renk sıralamayı belirlemiyor.** DINOv2 her iki boyutta da hue kayması, tam
   gri, parlaklık ve kontrast değişimlerinde %98–100 R@1 verdi — iş kuralıyla
   uyumlu. CLIP ise tam gri sorguda **%72**'ye düşüyor (p95 sırası 23): rengi
   kaldırınca CLIP deseni tanıyamıyor, bu da renk bilgisinin CLIP
   embedding'inde baskın olduğunu gösteriyor.
3. **Zayıf kalan tek grup kısmi crop ve ölçek.** Üç modelde de en düşük skorlar
   `crop_right`, `crop_topleft`, `scale2x`. Burada **B varyantı S'ten belirgin
   üstün** (p95 sırası 24–38 vs 140–173).

### Skor dağılımları (yanlış pozitif davranışı)

| Model | Pozitif p05 | Pozitif p50 | En iyi yanlış aday p50 | p95 | Rastgele negatif ort. | p99 |
|---|---:|---:|---:|---:|---:|---:|
| CLIP ViT-B/16 | 0,747 | 0,932 | 0,888 | 0,942 | **0,679** | 0,864 |
| DINOv2 ViT-S/14 | 0,578 | 0,894 | 0,739 | 0,911 | **0,249** | 0,666 |
| DINOv2 ViT-B/14 | 0,578 | 0,886 | 0,719 | 0,913 | **0,192** | 0,651 |

CLIP'te rastgele iki görselin ortalama benzerliği **0,679**; yani ilgisiz
görseller bile yüksek skor alıyor ve pozitif/negatif dağılımları büyük ölçüde
örtüşüyor. DINOv2'de bu değer 0,19–0,25'e düşüyor: **ilgisiz desenler gerçekten
uzakta.** Aynı renk–farklı desen yanlış pozitifleri bu ölçüde CLIP'ten belirgin
biçimde düşüktür.

---

## 7. PyTorch ↔ ONNX sayısal eşitlik

Her iki DINOv2 varyantı, Lens'in mevcut export deseniyle (opset 17, dinamik
batch, `pixel_values` → `image_embeds`) ONNX'e aktarıldı; çıktı CLS token +
L2 normalize olacak şekilde sarmalandı.

| Model | cosine (min) | max mutlak fark | Sıralama eşitliği (720 sorgu) | R@1 torch → ONNX | R@20 torch → ONNX |
|---|---:|---:|---:|---:|---:|
| DINOv2 ViT-S/14 | 1,000000 | 1,02e-06 | **%100 (0/720 sorgu farklı)** | %89,3 → %89,3 | %97,9 → %97,9 |
| DINOv2 ViT-B/14 | 0,999999 | 5,25e-07 | **%100 (0/720 sorgu farklı)** | %90,4 → %90,4 | %98,9 → %98,9 |

ONNX'e geçiş sıralamayı **hiçbir sorguda değiştirmedi**.

---

## 8. Hız, bellek ve boyut

Tüm ölçümler aynı makinede, CPU üzerinde, **tek model bellekteyken** yapıldı.

| | CLIP ViT-B/16 (ONNX) | DINOv2-S (ONNX) | DINOv2-B (ONNX) |
|---|---:|---:|---:|
| Tek görsel p50 | 58,5 ms | 62,0 ms | 134,1 ms |
| Tek görsel p95 | 62,2 ms | **74,2 ms** | 141,8 ms |
| Batch-8 (görsel başına) | 62,8 ms | **34,6 ms** | 98,6 ms |
| ONNX session yükleme | — | 193 ms | 454 ms |
| PyTorch p50 (referans) | — | 116,5 ms | 371,0 ms |
| Model dosyası | 329 MB | **88,4 MB** | 346,5 MB |
| Embedding boyutu | 512 | **384** | 768 |
| 2.007 görsel index (ölçülen) | 130 sn (PyTorch boru hattı) | **81 sn** | 221 sn |

**Türetilmiş tahminler** (ölçülen batch-8 hızından, *ölçüm değildir*):

| | DINOv2-S | DINOv2-B |
|---|---:|---:|
| 5.000 görsel ilk indeksleme | ~2,9 dk | ~8,2 dk |
| Tek yeni görsel ekleme | ~0,06 sn | ~0,13 sn |
| 5.000 görsel index dosyası (JSON) | ~24 MB | ~48 MB |

Karşılaştırma: mevcut CLIP index'i 2.007 görsel için 12,6 MB (512 boyut);
DINOv2-S 384 boyutla yaklaşık **%25 daha küçük** bir index üretir.

**Sorgu süresi:** tek sorgu = 1 embedding + 2.007 satırlık nokta çarpımı.
DINOv2-S için ölçülen p95 embedding süresi **74 ms**; brute-force benzerlik
hesabı bunun yanında ihmal edilebilir. Sıcak sorgu p95 hedefi olan 3 saniyenin
çok altındadır. **Dönüş TTA'sı gerekmediği için bu süre 4 katına çıkmaz.**

**Bellek:** Benchmark prosesinin tepe RAM'i ~5,2 GB ölçüldü, ancak bu değer üç
modeli ve tüm ara embedding'leri aynı anda tutan ölçüm sürecine aittir —
**tek model çalıştıran production için temsili değildir**; production RAM'i
ayrıca ölçülmemiştir.

**ClickOnce paketine etki:** DINOv2-S seçilirse model dosyası 329 MB → 88 MB,
yani paket yaklaşık **241 MB küçülür**. DINOv2-B seçilirse fark ihmal
edilebilir (+17 MB).

> **Ölçüm metodolojisi düzeltmesi:** Ön testte CLIP ONNX için 817 ms/görsel
> ölçülmüştü; o ölçüm üç model aynı anda bellekteyken yapıldığı için geçersizdir.
> Tek model yüklüyken doğru değer 58,5 ms'dir. Yukarıdaki tablo düzeltilmiş
> ölçümleri içerir.

---

## 9. Eşik (minimum benzerlik) analizi

Mevcut varsayılan eşik **%80**'dir. Bu değer CLIP skor dağılımına göre
belirlenmiştir ve **DINOv2'ye doğrudan taşınamaz**. Tam veri üzerinde ölçülen
eşik taraması:

| Eşik | CLIP: pozitiflerin kalan oranı | DINOv2-S: pozitiflerin kalan oranı |
|---:|---:|---:|
| %55 | %99,6 | %96,2 |
| %60 | %99,0 | %94,0 |
| %65 | %98,3 | %91,2 |
| %70 | %97,1 | %86,2 |
| %75 | %94,9 | %77,9 |
| **%80** | %92,9 | **%68,2** |
| %85 | %84,7 | %57,5 |

**Mevcut %80 eşiği DINOv2-S'e olduğu gibi taşınırsa doğru eşleşmelerin yaklaşık
üçte biri (%31,8) listeden elenir.** Bu, model değişiminin en somut kullanıcı
etkisidir.

**Önerilen başlangıç eşiği: %55–60** (pozitiflerin %94–96'sı korunur). Kesin
değer, gerçek katalogda kabul edilebilir yanlış pozitif oranına göre
kalibre edilmelidir.

Ek notlar:

- Ekranda gösterilen "Benzerlik: %XX" değeri bir **olasılık değildir**;
  L2-normalize embedding'lerin nokta çarpımının (kosinüs benzerliği) yüzdeye
  çevrilmiş halidir. Model değişince aynı yüzdenin anlamı değişir.
- Kullanıcının kayıtlı eşik tercihi (`UserSettings.PreferredMaxResults`'ın
  yanındaki eşik girdisi) **eski modele aittir**; model değişiminde ya
  sıfırlanmalı ya da yeni dağılıma göre dönüştürülmelidir. Sessizce taşınması
  yanıltıcıdır.

---

## 10. Elenen ve denenmeyen seçenekler

| Seçenek | Durum | Gerekçe |
|---|---|---|
| CLIP ViT-B/16 (mevcut) | **Pilot adayı olarak elendi** | Tam veride her metrikte geride: R@20 %93,5 (S: %97,8), p95 sıra 40 (S: 4); rastgele negatif ortalaması 0,679 ile pozitif/negatif dağılımları örtüşüyor |
| Renk nötrleştirme (gri temsil) | **Gerekmedi** | DINOv2 renkli girişte gri ve hue sorgularında zaten %100 R@1 |
| 2×2 / 3×3 çoklu crop (tile) | **Gerekmedi** | Tek global embedding kabul hedeflerini karşıladı; tile, index boyutunu ve indeksleme süresini 4–10 kat artırırdı |
| Sorgu-zamanı 4 dönüş (TTA) | **Gerekmedi** | DINOv2-S ham modelde 90°/180°/270° R@1 = %100 |
| Yerel özellik eşleştirme (LightGlue + DISK/ALIKED) | **Bu turda devreye alınmadı** | Global embedding hedefleri tuttu; kısmi crop senaryosu gerçek kullanımda baskın çıkarsa yeniden değerlendirilmeli |
| DINOv3 | **Elendi** | Apache-2.0 değil; özel lisans, yeniden dağıtım şartlı — ClickOnce ile ağırlık dağıtımı hukuk incelemesi gerektirir |
| SigLIP / Marqo-FashionSigLIP | **Elendi** | CLIP ile aynı zaafiyet sınıfı (metin denetimli, kategori odaklı) |
| SuperPoint | **Elendi** | Ağırlık lisansı ticari kullanıma uygun değil |

---

## 11. Pilot önerisi

**Önerilen pilot: DINOv2 ViT-S/14 (`facebook/dinov2-small`), tek global CLS
embedding, çoklu crop yok, TTA yok.**

Gerekçe:

| Ölçüt | DINOv2-S | DINOv2-B | Karar |
|---|---:|---:|---|
| R@20 (tam veri) | %97,8 | %98,8 | Fark 1,0 puan — model seçim kuralındaki 1–2 puan bandı içinde |
| R@100 (tam veri) | %98,8 | %99,3 | Fark 0,5 puan |
| Bilinen çift | 1. sıra (0,9509) | 1. sıra (0,9342) | Eşit; S'in skoru daha yüksek |
| Dönüş (90/180/270) | **%100 / %100 / %100** | %95 / %98 / %90 | **S üstün** |
| Kısmi crop / ölçek | p95 140–173 | **p95 24–38** | **B üstün** |
| Sorgu p95 | **74 ms** | 142 ms | S 1,9× hızlı |
| Model dosyası | **88 MB** | 346 MB | S, ClickOnce paketini ~241 MB küçültür |
| Index boyutu | **384 boyut** | 768 boyut | S yarı yer kaplar |

Kalite farkı kabul bandı içinde, hız ve boyut farkı ise büyük olduğu için
**Small tercih edilmiştir**.

### Başlangıç kabul hedefleriyle karşılaştırma

Bu hedefler pilot hedefleridir; **yönetici tarafından resmî production sınırı
olarak onaylanmamıştır.**

| Hedef | DINOv2-S sonucu | Durum |
|---|---|---|
| Recall@20 ≥ %90 | %97,8 | ✅ |
| Recall@100 ≥ %97 | %98,8 | ✅ |
| Doğru eşleşmenin p95 sırası ≤ 20 | 4 | ✅ |
| Bilinen çift ilk 5'te (tercihen), ilk 20'de (zorunlu) | 1. sıra | ✅ |
| CPU sıcak sorgu p95 ≤ 3 sn | 74 ms | ✅ |
| İlk indeksleme ≤ 2 saat | 2.007 görselde 81 sn; 5.000 için ~2,9 dk (tahmin) | ✅ |
| Aynı renk–farklı desen yanlış pozitifleri CLIP'ten düşük | Rastgele negatif ort. 0,249 (CLIP 0,679) | ✅ |

### Pilot başarısız olursa ikinci seçenek

Gerçek katalogda kısmi crop sorguları baskınsa: **DINOv2 ViT-B/14** (crop/ölçek
p95'i belirgin daha iyi). O da yetmezse global embedding + yerel/geometrik
doğrulama (LightGlue + DISK/ALIKED) iki aşamalı yapı.

---

## 12. Global embedding yeterli mi? Crop/tile veya yerel eşleştirme gerekli mi?

- **Dönüş, renk, ton, parlaklık, perspektif ve konum senaryolarında global
  embedding yeterlidir** (ölçülen: R@1 %100 veya buna yakın).
- **Kısmi crop ve güçlü ölçek değişiminde global embedding yetersizdir**
  (DINOv2-S: `crop_right` %65, `scale2x` %57). Bu senaryolar toplam metrikte
  R@20'yi %97,8'e taşıyacak kadar seyrek kalmıştır, ancak gerçek kullanımda
  sorguların büyük kısmı desenin küçük bir parçasının fotoğrafıysa bu oran
  kabul edilemez hale gelebilir.
- **Bu turda çoklu crop/tile ve yerel eşleştirme gerekmemiştir**; ancak yukarıdaki
  koşul gerçekleşirse ilk devreye alınacak seçenek çoklu crop değil,
  **DINOv2-B** olmalıdır (aynı maliyet artışıyla daha iyi crop davranışı verir).

---

## 13. Production entegrasyonundan önce zorunlu işler

Aşağıdakiler bu çalışmada **yapılmamıştır**; model değişimi kararı onaylanırsa
uygulanması gereken teknik işlerdir.

1. **Index şema sürümlemesi.** Mevcut `ImageIndexEntry` yalnızca yol, dosya
   boyutu, değişiklik zamanı ve embedding tutar. Eklenmesi gerekenler:
   model kimliği, model dosyası SHA-256, ön işleme sürümü, embedding boyutu,
   özellik türü (`CLS`), crop/tile stratejisi, index şema sürümü. Bunlardan
   herhangi biri kayıtlı değerden farklıysa index **tamamen** yenilenmelidir;
   CLIP ve DINOv2 embedding'leri aynı dosyada karışmamalıdır.
2. **Ön işleme ayrımı.** DINOv2 kısa kenar 256 → 224 center crop ve ImageNet
   mean/std kullanır. `ImagePreprocessor` şu an CLIP değerlerine sabittir;
   model başına ön işleme profili gerekir.
3. **Eşik kalibrasyonu ve göç.** Varsayılan %80 yeni modele taşınamaz
   (bkz. §9); kayıtlı kullanıcı tercihi sıfırlanmalı veya dönüştürülmelidir.
4. **Tam yeniden indeksleme.** Paylaşılan index dosyası tüm kullanıcıları
   etkilediği için koordinasyon gerekir.
5. **Alt klasör taraması (ayrı ve bağımsız konu).** `ClassifyDirectory`,
   `Directory.EnumerateFiles(folder)` ile **yalnızca üst dizini** tarar ve
   `RelativePath` alanı gerçek göreli yol değil, yalnızca dosya adıdır. Test
   veri kümesinde alt klasör bulunmadığı için bu ölçümleri etkilemedi; **gerçek
   kataloğun alt klasör içerip içermediği bu çalışmada doğrulanamamıştır.**
6. **Model dosyası dağıtımı.** ONNX dosyası repoya commit edilmez; ClickOnce
   paketine ekleme ve SHA-256 kaydı `docs/CLICKONCE.md` ve
   `docs/MODEL_CARD.md` süreçlerine göre yapılmalıdır.

---

## 14. Ölçülen gerçek / teknik çıkarım / varsayım ayrımı

**Ölçülen gerçekler** (bu makinede fiilen çalıştırıldı):
tüm Recall/MRR/sıra değerleri, bilinen çiftin her iki yöndeki sırası, skor
dağılımları, eşik taraması, PyTorch↔ONNX eşitliği, embedding süreleri, model ve
ONNX dosya boyutları, 2.007 görselin indeksleme süresi, veri envanteri.

**Teknik çıkarımlar** (kaynak kod ve resmî belge incelemesi):
index alanlarının model kimliği/ön işleme sürümü içermemesi; alt klasörlerin
taranmaması; `RelativePath`'in yalnızca dosya adı olması; lisans bilgileri.

**Varsayımlar (test edilmedi):**

- 5.000 görsel için indeksleme süresi ve index dosyası boyutu — ölçülen batch
  hızından **türetilmiş tahminlerdir**.
- Bu ölçümlerin **gerçek üretim kataloğuna genellenebilirliği**. Test veri
  kümesi geniş ve çeşitlidir (barok, floral, geometrik, batik, etnik…); gerçek
  katalog aynı ürün ailesinden birbirine çok benzeyen desenler içeriyorsa
  yanlış pozitif oranı bu ölçümlerden yüksek çıkabilir.
- Production RAM kullanımı (yalnızca çok modelli benchmark prosesi ölçüldü).
- Gerçek .NET/ONNX Runtime entegrasyonunda ImageSharp ön işlemesinin PIL ile
  aynı embedding'i üretip üretmeyeceği. **Bu fark CLIP için ölçüldü ve
  önemsizdir denemez:** aynı görselde .NET index'i ile Python boru hattı
  arasında kosinüs benzerliği 0,949–0,999 aralığında değişti. DINOv2 için bu
  karşılaştırma **yapılmamıştır** ve entegrasyon sırasında doğrulanmalıdır.

---

## 15. Doğrulanamayan noktalar ve riskler

- **Gerçek üretim kataloğu ölçülmedi.** Bu benchmark, üretim benzeri ama farklı
  bir veri kümesiyle yapılmıştır.
- **Gerçek ikinci fotoğraf verisi yok.** Tüm dönüşümler sentetiktir; gerçek
  mağaza/atölye fotoğrafındaki kumaş kırışıklığı, dokuma dokusu ve karma ışık
  koşulları temsil edilmemektedir.
- **Ayna (mirror) davranışı iş kuralı olarak belirsiz.** DINOv2 aynalanmış
  deseni %100 aynı bulur; bunun istenen davranış olup olmadığı kararı
  verilmemiştir.
- **`tilt±30` ve kısmi crop** senaryoları hâlâ en zayıf halkadır.
- **Model lisansı hukuk onayından geçmemiştir.** Apache-2.0 olduğu model kartı
  meta verisinden okunmuştur; şirket/hukuk onayı ayrı bir adımdır.
- **Ölçümler tek makinede, tek CPU yapılandırmasında yapılmıştır.**
