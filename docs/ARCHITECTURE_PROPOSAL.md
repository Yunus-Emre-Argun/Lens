# Mimari Görüş — Lens PoC

Bu doküman, mimari toplantı öncesi teknik görüş hazırlığıdır. **Kod içermez,
nihai karar içermez.** Amaç, alternatifleri ve riskleri netleştirip toplantıda
verimli bir karar süreci sağlamaktır.

---

## 1. En Basit Olası PoC Mimarisi (Genel Çerçeve)

Model/depolama detayından bağımsız olarak, PoC'un akışı büyük ihtimalle
şu genel şekli alacak:

```
[Query görsel] → [Embedding modeli] → [Query vektörü]
                                            │
                     [Kayıtlı görsellerin embedding'leri] (önceden hesaplanmış)
                                            │
                              [Benzerlik hesabı (cosine/dot)]
                                            │
                              [Top-N sonuç (5-10 aday)]
```

Bu çerçevede değişken olan üç şey: (a) hangi model embedding üretiyor,
(b) embedding'ler nerede/nasıl saklanıyor, (c) benzerlik araması nasıl
yapılıyor. Aşağıdaki bölümler bu üç ekseni tartışır.

---

## 2. Image Embedding Yaklaşımı Uygun mu?

Evet, problem tanımına (görsel benzerlik, tam eşleşme garantisi değil,
recall öncelikli) uygun bir yaklaşım. Gerekçe:

- Görseller "temiz, katalog görünümlü" — yani arka plan gürültüsü, açı
  farkı, ışık koşulu gibi klasik "gerçek dünya fotoğrafı" sorunları düşük.
  Bu, embedding tabanlı yaklaşımların güçlü olduğu bir senaryo.
- Klasik yöntemler (perceptual hash, histogram karşılaştırma, SIFT/ORB gibi
  keypoint eşleştirme) de bu görsellerde iyi çalışabilir, ancak "benzer ama
  aynı olmayan" ürünleri (farklı renk/desen varyasyonu) ayırt etmede
  embedding tabanlı (öğrenilmiş) temsillerin genelleme gücü genelde daha
  yüksektir.
- Risk: embedding modelleri genel amaçlı (ImageNet/web-scale) veri ile
  eğitildi; nevresim deseni gibi ince taneli (fine-grained) bir alanda
  ne kadar iyi ayrım yapacakları **kanıtlanmamış** — bu yüzden bölüm 4 ve
  benchmark önerisi önemli.

---

## 3. CLIP vs SigLIP — Neden Aday, Neden Değil

**Neden ikisi de mantıklı aday:**
- İkisi de görsel embedding üretebilen, önceden eğitilmiş, açık ağırlıklı
  modeller; ek fine-tuning olmadan "zero-shot" görsel benzerlik için
  kullanılabilirler.
- İkisi de CPU üzerinde (yavaş da olsa) çalışabilecek boyutlarda base/varyant
  seçenekleri sunuyor.
- İkisi de topluluk desteği, hazır kütüphane (örn. `transformers`,
  `open_clip`) açısından olgun.

**Bilinmeyen / test edilmesi gereken noktalar (ikisi için de):**
- Her iki model de esas olarak **görsel-metin eşleştirme** için eğitildi,
  saf görsel-görsel benzerlik için değil. Görsel embedding'lerinin
  desen/renk gibi ince farkları ne kadar iyi ayırt ettiği görev-spesifik
  bir sorudur, model kartlarından çıkarılamaz.
- SigLIP daha yeni bir eğitim hedefi (sigmoid loss) kullanıyor olması,
  bu problem için otomatik olarak daha iyi olacağı anlamına gelmez —
  bu proje bağlamında **test edilmeden varsayılamaz** (gereksinimde de
  açıkça belirtildi).
- Model boyutu (base/large) ile CPU'da hız/kalite dengesi ayrıca
  ölçülmeli.

Sonuç: İkisi de meşru aday; hangisinin bu spesifik veri (nevresim
desen/renk ayrımı) üzerinde daha iyi recall verdiği yalnızca küçük bir
benchmark ile (bkz. Bölüm 8) belirlenebilir.

---

## 4. Aynı Kategori İçinde Renk/Desen Ayrımı — Riskler

Nevresimler aynı üst kategoride (yatak tekstili) olduğu için genel amaçlı
embedding modelleri şu risklere açık olabilir:

- **Kategori baskınlığı:** Model "bu bir nevresim/kumaş" bilgisini çok
  güçlü, "hangi desen/renk" bilgisini daha zayıf kodlayabilir. Bu durumda
  tüm nevresimler embedding uzayında birbirine çok yakın çıkar ve ayrım
  gücü düşer.
- **Renk vs desen ağırlığı dengesizliği:** Model, renk farklarını desen
  farklarından daha güçlü (veya tam tersi) yakalayabilir. Örn. aynı desen
  farklı renk varyasyonları, farklı desen ama benzer genel renk paletine
  sahip iki üründen daha "uzak" ya da "yakın" çıkabilir — bu, kullanım
  amacına göre istenen ya da istenmeyen bir davranış olabilir (netleşmeli).
- **Doku/ölçek detayları:** Görsel modeller genelde görseli düşük
  çözünürlüğe indirger (örn. 224x224 veya 384x384 piksel). Küçük desen
  tekrarları veya ince doku farkları bu küçültmede kaybolabilir.
- **Kompozisyon/crop etkisi:** Katalog görselinde ürünün kadrajdaki
  konumu, çekim açısı gibi farklar (query farklı crop olduğunda) embedding'i
  gerçek ürün farkından daha çok etkileyebilir — bu bir "yanlış negatif"
  (recall) riskidir ve proje önceliğiyle doğrudan çelişir.

Bu riskler **11 görsel + gerçek query verisi olmadan tam olarak ölçülemez**
(bkz. Bölüm 6-7); bu nedenle mimari toplantıda bir "kabul edilebilir risk"
çerçevesi konuşulmalı.

---

## 5. Query Görseli Crop/Resize Olduğunda Hangi Testler Yapılmalı

Minimum test seti (gerçek ikinci fotoğraf yokluğunda dahi anlamlı olan):

1. **Kimlik testi:** Kayıtlı görselin embedding'i ile birebir kendisi
   sorgulandığında top-1 kendisi mi çıkıyor mu? (Sistem sağlığı kontrolü,
   asıl "gerçek dünya" testi değil.)
2. **Resize testi:** Aynı görsel farklı çözünürlüklere indirgenip/
   büyütülüp sorgulanınca top-1/top-3 içinde doğru ürün kalıyor mu?
3. **Crop testi:** Görselin kenarlarından belirli oranlarda (örn. %5, %15,
   %30) kırpılmış versiyonları sorgulanınca doğru ürün top-3/top-5 içinde
   kalıyor mu?
4. **Hafif fotometrik değişim testi:** Parlaklık/kontrast/renk doygunluğu
   hafifçe değiştirildiğinde sonuç ne kadar stabil?
5. **Ayırt edicilik testi:** Sorgu, kayıtlı 11 üründen biriyle **hiç
   ilgisi olmayan** başka bir görsel olduğunda sistem yanlışlıkla yüksek
   güvenle bir eşleşme "önermiyor" mu (bariz false positive var mı)?

Bu testlerin sonucu **kesin bir doğruluk oranı** olarak sunulmamalı;
"bu küçük örneklemde gözlenen eğilim" olarak raporlanmalı (bkz. Bölüm 6).

---

## 6. 11 Görselle Neyi Ölçebiliriz, Neyi Ölçemeyiz

**Ölçebileceklerimiz:**
- Sistem uçtan uca çalışıyor mu (pipeline doğruluğu)?
- Basit crop/resize/fotometrik varyasyonlarda top-1/top-N doğru ürünü
  buluyor mu (küçük, gösterge niteliğinde bir sinyal)?
- CPU'da bir arama isteğinin yaklaşık ne kadar sürdüğü (bkz. Bölüm 11) —
  ama 11 görsellik bir arşivde arama süresi, ~1.000 görsellik gerçek
  ölçekte doğrusal ölçeklenmeyebilir (özellikle I/O ve indeksleme etkisiyle).
- İki modelin (CLIP/SigLIP) bu 11 görsel + sentetik varyasyon üzerinde
  **göreli** davranış farkı (biri diğerine göre daha mı stabil).

**Ölçemeyeceklerimiz (istatistiksel olarak anlamlı biçimde):**
- Gerçek recall/precision oranı (11 örnek istatistiksel anlamlılık için
  yetersiz; %90 ya da %70 gibi bir rakam üretmek yanıltıcı olur).
- Görsel olarak birbirine çok yakın iki farklı ürün olduğunda sistemin
  ayrım gücü (11 üründe böyle "zor" bir çift olmayabilir/olabilir, bilinmiyor).
- Gerçek saha koşullarında (farklı kamera, farklı ışık, kırışık kumaş vb.
  gerçek ikinci fotoğraflar) performans — çünkü böyle veri yok.
- Ölçek büyüdükçe (11 → ~1.000) yanlış pozitif oranının nasıl değişeceği.

Bu nedenle PoC sonunda üretilecek rakamlar "kanıt" değil, "yön gösteren
erken sinyal" olarak sunulmalı.

---

## 7. Gerçek İkinci Fotoğrafların Olmaması — Test Açısından Etkisi

Bu, PoC'un en önemli metodolojik kısıtı:

- Sentetik varyasyonlar (crop/resize/brightness) yalnızca **geometrik ve
  fotometrik** değişimleri simüle eder. Gerçek ikinci bir fotoğrafta ayrıca
  şunlar olur: farklı kamera/lens, farklı ışık kaynağı ve renk sıcaklığı,
  kumaşın farklı şekilde serilmiş/kırışık olması, gölge, perspektif
  distorsiyonu, arka plan farkı. Bunların hiçbiri sentetik testte yoktur.
- Dolayısıyla sentetik testten "iyi" sonuç almak, sistemin gerçek saha
  koşullarında da iyi çalışacağının **kanıtı değildir** — sadece bir alt
  küme riski elediğini gösterir.
- Bu boşluk, mimari toplantıda açıkça konuşulmalı: yönetici demoyu
  gördükten sonra "gerçek ikinci fotoğraf" örnekleri sağlayabilir mi?
  Bu, PoC sonrası doğrulama için kritik olur.

**Sonuç:** Sentetik testler *anlamsız değildir* (temel sağlamlık/regresyon
kontrolü için faydalıdır) ama *yeterli değildir* (gerçek dünya doğrulaması
yerine geçmez). Bu ayrım demo sunumunda yönetime de açıkça belirtilmeli.

---

## 8. CLIP vs SigLIP İçin Adil Küçük Bir Benchmark Tasarımı

Adillik için önerilen minimum ilkeler (henüz uygulanmadı, sadece tasarım):

1. **Aynı ön işleme:** Her iki model için de görsellerin okunma, boyutlandırma
   ve normalize edilme adımları kendi resmi ön işleme gereksinimlerine göre
   yapılmalı (birine özel bir ön işleme avantajı/dezavantajı yaratmadan).
2. **Aynı test seti:** 11 orijinal görsel + üzerlerinden türetilen aynı
   sentetik varyasyon seti (aynı crop oranları, aynı resize hedefleri, aynı
   parlaklık değişimleri) her iki modele de aynen uygulanmalı.
3. **Aynı benzerlik metriği:** Örn. cosine similarity, ikisi için de aynı
   şekilde hesaplanmalı.
4. **Karşılaştırılabilir model boyutu:** Mümkünse her iki model de "base"
   (ya da her ikisi de "large") gibi benzer parametre sınıfından seçilmeli;
   büyük/küçük model karşılaştırması yapılmamalı (adil olmaz).
5. **Ölçülecek metrikler:**
   - Top-1 / Top-3 / Top-5 doğru ürünü bulma oranı (11 örnek üzerinde,
     "gösterge" niteliğinde, istatistiksel iddia olmadan).
   - Her varyasyon türü (crop/resize/brightness) için ayrı ayrı sonuç
     (hangi model hangi bozulma türüne daha dayanıklı).
   - CPU üzerinde tek görsel için embedding üretme süresi (ms).
   - Bariz false-positive gözlemi (alakasız görsel yüksek benzerlikle
     eşleşiyor mu).
6. **Raporlama:** Sonuçlar "Model A, bu 11 örnek + sentetik varyasyonda,
   Model B'ye göre X testinde daha stabil görünüyor" biçiminde, kesin
   genel geçer bir üstünlük iddiası olmadan sunulmalı.

---

## 9. Embedding Depolama — İlk PoC İçin

İlk PoC ölçeğinde (11 → belki birkaç yüz görsel) en basit çözüm yeterlidir:
görsel başına hesaplanan embedding vektörünün, bir dosya adı/ürün kimliği
ile birlikte düz bir dosyada (örn. tek bir serileştirilmiş dosya veya basit
bir tablo) saklanması. Arama sırasında tüm embedding'ler belleğe yüklenip
sorgu vektörüyle karşılaştırılabilir (brute-force / tam tarama).

Bu yaklaşımın PoC için yeterli olma nedeni: birkaç yüz-bin mertebesindeki
vektör sayısında, modern bir CPU'da tam tarama (brute-force) karşılaştırması
milisaniyeler mertebesinde sürer — bkz. Bölüm 10.

**Açık kalan nokta:** Kesin dosya biçimi (hangi kütüphane/format) bir
teknoloji seçimi olduğu için burada karara bağlanmıyor; sadece "basit,
tam-tarama yapılabilir bir yapı yeterli" ilkesi öneriliyor.

---

## 10. ~1.000 Görsel İçin Vector Database Gerçekten Gerekli mi?

Kısa cevap: **muhtemelen hayır**, ama bu iddia doğrulanmalı, varsayılmamalı.

Gerekçe:
- Vector database'ler (örn. approximate nearest neighbor indeksleri) asıl
  değerini **on binler-milyonlar** mertebesinde vektör olduğunda, tam
  taramanın (brute-force) pratik olmadığı durumlarda gösterir.
- ~1.000 vektörlük (embedding boyutu tipik olarak birkaç yüz-birkaç bin
  boyutlu) bir küme için brute-force cosine similarity hesabı, modern
  bir CPU'da tipik olarak milisaniyeler-onlarca milisaniye mertebesindedir
  — 5 saniyelik hedefin çok altında kalması beklenir.
- Vector database eklemek bu ölçekte ek altyapı karmaşıklığı (kurulum,
  bakım, bağımlılık) getirir, buna karşılık ölçülebilir bir performans
  faydası **bu ölçekte** beklenmiyor.

**Ama:** Bu bir tahmin/mühendislik muhakemesidir, ölçülmüş bir sonuç değil.
Mimari toplantıda "gerçekten gerekli değil" demeden önce, PoC sırasında
basit bir tam-tarama aramasının gerçek süresi ölçülüp bu varsayım
doğrulanmalı (bkz. Bölüm 11). Eğer gelecekte ölçek 1.000'den çok daha
büyük bir yere (örn. yüz binler) gidecekse, bu karar yeniden değerlendirilir
— ama bu, şu anki PoC kapsamının dışındadır.

---

## 11. CPU-Only Ofis Bilgisayarında Riskler

- **Embedding üretme süresi:** Model boyutuna bağlı olarak CPU'da tek
  görsel için embedding çıkarma süresi, GPU'ya göre kayda değer ölçüde
  (kabaca 5-20x, kesin oran modele/donanıma bağlı) daha yavaş olabilir.
  Bu, 5 saniyelik toplam hedefi query tarafında riske atabilir.
- **Model yükleme süresi:** Uygulama her açıldığında (veya her istek
  başına, mimariye bağlı) modelin belleğe yüklenmesi zaman alır; bu,
  "arama süresi" ile "uygulama başlatma süresi" ayrımının net yapılmasını
  gerektirir (5 saniye hedefi muhtemelen "arama" için, "ilk açılış" için
  değil — bu netleştirilmeli).
- **Bellek kullanımı:** Ofis bilgisayarlarında RAM sınırlı olabilir; model
  + embedding kümesi + uygulama belleği toplamının makul kalması gerekir.
- **Donanım çeşitliliği:** Farklı ofis bilgisayarları farklı CPU
  performansına sahip olabilir; "5 saniye" hedefinin hangi referans
  donanımda ölçüleceği netleşmeli.
- Not: Bu geliştirme makinesinde bir NVIDIA GPU tespit edildi (RTX 5060 Ti),
  ancak bu **hedef ofis ortamını temsil etmiyor** olabilir — PoC ölçümleri
  CPU-only senaryoyu da ayrıca içermeli.

---

## 12. 5 Saniye Hedefi İçin Hangi Ölçümler Alınmalı

Toplam "arama süresi" en az şu bileşenlere ayrılarak ölçülmeli:

1. Görsel okuma/decode süresi.
2. Query görseli için embedding üretme süresi (asıl darboğaz burada
   beklenir, CPU'da).
3. Kayıtlı embedding'lerin bellekte hazır olup olmadığı (önceden
   yüklenmiş mi, her sorguda diskten mi okunuyor).
4. Benzerlik hesaplama + sıralama (top-N seçme) süresi.
5. (Varsa) sonuçların arayüzde gösterilmesi için görsel yükleme süresi.

Bu ayrım, "5 saniyeyi nerede kaybediyoruz" sorusuna kod yazılmadan önce
netlik kazandırır ve hangi bileşenin optimize edilmesi gerektiğini
gösterir (örn. embedding üretimi yavaşsa model küçültme/quantization
gündeme gelir; sıralama yavaşsa bu ölçekte beklenmez).

---

## 13. İlk Demo ile Gelecekteki Production Mimarisi Nasıl Ayrılmalı

Öneri ilkesi: PoC'ta alınan **her** basitleştirme kararı açıkça
"bu bir PoC kısayoludur, production'da yeniden değerlendirilmeli" diye
işaretlenmeli. Örnek ayrımlar (henüz karar değil, sadece kategori örneği):

| Konu | PoC'ta olası basit yaklaşım | Production'da yeniden değerlendirilecek |
|------|------------------------------|-------------------------------------------|
| Veri kaynağı | Klasördeki statik görseller | Gerçek fabrika DB entegrasyonu |
| Arama yöntemi | Tam tarama (brute-force) | Ölçek büyürse indeksleme ihtiyacı |
| Kullanıcı | Tek kullanıcı, login yok | Çoklu kullanıcı, kimlik doğrulama |
| Model dağıtımı | Yerel, tek makine | Paylaşımlı servis mi, yerel mi (belirsiz) |
| Test verisi | Sentetik varyasyonlar | Gerçek ikinci fotoğraflarla doğrulama |

Bu tablo, toplantıda "bu bir demo kısayolu, kalıcı mimari kararı değil"
ayrımını netleştirmek için bir başlangıç noktasıdır; production mimarisi
bu toplantının kapsamı dışındadır.

---

## 14. Alternatifler

Aşağıdaki 2-3 alternatif, PoC için değerlendirilebilecek genel yaklaşımlardır.
Hiçbiri henüz seçilmiş değildir.

### Alternatif A — Tam Yerel, Dosya Tabanlı, Tam Tarama Arama

Model yerel makinede çalışır (CPU), embedding'ler basit bir dosyada
saklanır, arama brute-force cosine similarity ile yapılır. Dış servis yok.

- **Avantaj:** En düşük karmaşıklık; dış bağımlılık/izin/maliyet sorunu
  yok; veri fabrika dışına çıkmaz (olası gizlilik kaygısını baştan çözer);
  1-2 günlük süreye en uygun seçenek.
- **Dezavantaj:** CPU'da model çıkarım hızı sınırlı olabilir; büyük ölçekte
  (çok ileri fazda) yeniden mimarı gerekebilir.
- **Risk:** 5 saniye hedefi, seçilecek model boyutuna bağlı olarak zorlanabilir
  (ölçülmeden bilinmiyor).
- **PoC süresine etkisi:** Düşük risk, en hızlı uygulanabilir seçenek.

### Alternatif B — Yerel Çalışma + Dış AI Servisi Opsiyonu (Hibrit, Sadece Değerlendirme)

Embedding üretimi için hem yerel model hem de bir dış AI servisi (örn.
bulut tabanlı embedding API) seçeneği değerlendirilir; hangisinin
kullanılacağı sonradan karara bağlanır.

- **Avantaj:** Dış servisler bazen daha güçlü/güncel modellere erişim
  sağlayabilir; yerel donanım kısıtını aşabilir.
- **Dezavantaj:** Veri (ürün görselleri) fabrika dışına çıkar — bu, gizlilik/
  güvenlik açısından **yönetim onayı gerektiren** bir konu ve şu an
  netleşmemiş (bkz. Open Question #4, DECISIONS.md). Ayrıca internet
  bağlantısı gereksinimi, maliyet ve gecikme (latency) belirsizlikleri
  ekler.
- **Risk:** Onay alınmadan bu yönde ilerlemek, projeyi geçersiz kılacak bir
  varsayım riski taşır.
- **PoC süresine etkisi:** Onay bekleme süresi nedeniyle 1-2 günlük hedefi
  tehlikeye atabilir; bu nedenle PoC için **önerilmez**, sadece gelecekte
  değerlendirilecek bir seçenek olarak not edilir.

### Alternatif C — Yerel Çalışma + Basit Bir İndeks Kütüphanesi (Erken Optimizasyon)

Alternatif A ile aynı, ancak baştan bir yaklaşık en-yakın-komşu (ANN)
kütüphanesi/vector database eklenir.

- **Avantaj:** İleride ölçek büyürse yeniden yazma ihtiyacı azalır.
- **Dezavantaj:** ~1.000 (hatta muhtemelen çok daha fazla) görsel ölçeğinde
  performans faydası kanıtlanmamış bir ek karmaşıklık; kurulum/bağımlılık
  yükü artar; "1-2 günlük PoC için overengineering yapma" ilkesiyle çelişir.
- **Risk:** Gereksiz erken optimizasyon, demo süresini uzatabilir ve asıl
  soruyu (model seçimi/doğruluk) gölgeleyebilir.
- **PoC süresine etkisi:** Orta-yüksek risk; süreyi uzatma ihtimali var.

---

## 15. Tavsiye (Nihai Karar Değildir)

Mevcut bilgiye dayanarak **Alternatif A** (tam yerel, dosya tabanlı,
brute-force arama) PoC için en makul başlangıç noktası görünüyor. Gerekçe:

- 1-2 günlük süreye en uygun, geri alınabilir ve en az varsayım gerektiren
  seçenek.
- ~1.000 vektör ölçeğinde brute-force aramanın performans yeterliliği,
  mühendislik muhakemesiyle (Bölüm 10) makul görünüyor; ancak bu **ölçülerek
  doğrulanmalı**, PoC'un bir parçası olarak.
- Dış servis (Alternatif B) seçeneği, onay ve veri gizliliği belirsizliği
  nedeniyle PoC aşamasında riskli; bu yüzden şimdilik önerilmiyor.

**Bu tavsiyeyi etkileyebilecek belirsizlikler:**
- Gerçek ofis donanımının CPU performansı bilinmiyor; ölçüm sonrası
  brute-force yaklaşımının 5 saniye hedefini karşılamadığı görülürse bu
  tavsiye değişebilir.
- Dış AI servisi kullanımına yönetim onay verirse, Alternatif B ileri
  fazda yeniden değerlendirilebilir.
- CLIP/SigLIP karşılaştırması henüz yapılmadı; hangi modelin seçileceği
  bu tavsiyeyi değiştirmez (her iki model de Alternatif A ile uyumlu),
  ancak model boyutu CPU performans riskini etkiler.

Bu tavsiye, kullanıcı ve Tech Lead/CTO onayı olmadan uygulamaya alınmayacaktır
(bkz. `CLAUDE.md`, `docs/DECISIONS.md`).

---

## 16. Questions for Architecture Meeting

Yalnızca gerçekten teknoloji/mimari kararını değiştirecek kritik sorular:

1. **Dış AI servislerine izin var mı?** (Ürün görselleri fabrika dışına
   çıkabilir mi?) — Alternatif A vs B seçimini doğrudan belirler.
2. **5 saniye hedefi hangi referans donanımda ölçülecek?** (Gerçek ofis
   bilgisayarı özellikleri nedir — en azından yaklaşık CPU sınıfı?)
3. **Yönetici, aynı ürüne ait gerçek ikinci fotoğraf(lar) sağlayabilir mi?**
   (PoC sonrası doğrulama için kritik; sentetik test tek başına yeterli değil.)
4. **~1.000 görsel rakamı ne kadar kesin bir hedef?** Yakın gelecekte çok
   daha büyük bir ölçek (örn. on binler) bekleniyor mu? (Vector database
   kararını doğrudan etkiler.)
5. **Gerçek fabrika veritabanına entegrasyon için kabaca zaman ufku nedir?**
   (PoC mimarisinin ne kadar "geçici" tasarlanacağını etkiler.)
