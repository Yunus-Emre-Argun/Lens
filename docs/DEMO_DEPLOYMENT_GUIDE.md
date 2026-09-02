# Lens — Demo Deployment Guide

Bu doküman, Lens MVP'yi başka bir Windows bilgisayarında (özellikle yönetici
bilgisayarında) nasıl çalıştıracağımızı anlatır. Teknik rapor değildir —
kısa ve uygulamaya yöneliktir.

---

## 1. Demo için gerekenler

**Yönetici bilgisayarına kaynak kod götürülmez.**

Sadece ilgili publish klasörünün **TAMAMI** kopyalanır (`publish/Lens.Desktop-win-x64/`
veya yönetici dağıtımı için `publish/Lens.Desktop-win-x64-manager/` —
ikisi de aynı self-contained Release yapılandırmasıyla üretilir).
Yalnızca `Lens.Desktop.exe` dosyasını kopyalamak **yeterli değildir** —
uygulama çalışmaz.

Publish klasöründe birlikte bulunması gerekenler:

- `Lens.Desktop.exe`
- Gerekli .NET runtime dosyaları (self-contained publish olduğu için dahil)
- Gerekli DLL'ler (ONNX Runtime, ImageSharp, WPF çalışma zamanı vb.)
- ONNX model dosyası (`models/clip-vision-b16-openai.onnx`)
- Uygulamanın ihtiyaç duyduğu diğer runtime dosyaları

Gerçek ürün görselleri publish paketine **dahil değildir**. Kullanıcı kendi
ürün klasörünü uygulama içinden seçer.

---

## 2. Yönetici bilgisayarında çalıştırma

1. İlgili publish klasörünü hedef Windows bilgisayarına kopyala.
2. `Lens.Desktop.exe` dosyasını çalıştır.
3. **"Ürün Klasörü Seç"** ile fabrika ürün/desen klasörünü seç (veya
   `appsettings.json` içinde `AdminDefaultProductDirectory` önceden
   ayarlanmışsa açılışta otomatik yüklenir).
4. **"İndeksi Güncelle / Klasörü Tara"** ile ürünleri indeksle.
5. İlk indekslemede ürün görsellerinin embedding'leri oluşturulur.
6. Sonraki çalıştırmalarda değişmeyen görseller tekrar embed edilmez.
7. **"Sorgu Görseli Seç"** ile (veya sürükle-bırak ile) aranacak görseli seç.
8. **"Ara"** butonuna bas.
9. Top-10 sonuçlarını incele; bir sonuca tıklayarak query görseliyle yan
   yana karşılaştır.
10. Nihai kararı kullanıcı verir — sistem kesin eşleşme iddia etmez.

---

## 3. Yeni ürün görselleri geldiğinde

Örnek: yönetici ilk seferde 200 ürün görseli verirse:

```
200 görsel → ilk indeksleme → embedding'ler oluşturulur → local cache'e kaydedilir
```

Daha sonra klasöre 20 yeni ürün eklenirse:

- Sadece yeni 20 görsel embed edilir.
- Değişmeyen 200 görsel yeniden hesaplanmaz.
- Değişmiş dosyalar yeniden embed edilir.
- Silinmiş dosyalar index'ten çıkarılır (yalnızca dosya klasörde gerçekten
  artık görünmüyorsa; geçici bir okuma hatasında eski kayıt korunur — bkz.
  §4).

Persistent index/cache dosyası ürün klasörünün İÇİNDE **değil**,
`%LocalAppData%\Lens\cache\<klasör-hash'i>\index.json` altında tutulur
(ürün dizini path'inden türetilen bir hash ile) — paylaşılan ağ klasörünün
Lens'e özel dosyalarla kirlenmemesi ve birden fazla kullanıcının aynı
dosyaya eşzamanlı yazmaması için (bkz. `docs/DECISIONS.md` #39, #44).
Aynı ürün klasörüne tekrar dönüldüğünde aynı cache otomatik olarak
kullanılır.

---

## 4. Güvenilirlik davranışı ve limitler

- **Geçici hata ≠ silme:** Ağ klasörüne kısa süreli erişim kaybı, dosya
  kilidi veya izin sorunu nedeniyle bir görsel o an okunamazsa, önceki
  başarılı indeksleme kaydı korunur — ürün aramadan düşmez. Yalnızca dosya
  gerçekten klasörden kaldırılmışsa index'ten çıkarılır.
- **Bozuk/uyumsuz cache dosyası** (örn. yarım kalmış bir yazımdan) uygulamayı
  çökertmez; sessizce yok sayılıp normal ilk-kullanım akışıyla yeniden
  oluşturulur.
- **Ağ/UNC klasörüne erişilemediğinde** uygulama donmaz veya kapanmaz;
  "Ürün klasörüne şu anda ulaşılamıyor" gibi bir durum mesajı gösterilir,
  bağlantı geri geldiğinde kullanıcı tekrar deneyebilir.
- **Büyük/aşırı çözünürlüklü görsel limiti:** ~50 MB dosya boyutu veya ~50
  megapiksel üzerindeki görseller (indexing, sorgu ve büyük önizleme/zoom
  için) belleği aşırı tüketmemek amacıyla reddedilir — normal katalog/telefon
  fotoğrafları bundan etkilenmez. Sınırı aşan bir ürün görseli "Görsel
  boyutu desteklenen sınırı aşıyor" olarak işaretlenir (eski kaydı varsa
  korunur); sorgu olarak seçilirse arama başlamadan aynı mesajla reddedilir.
- **Görsel olmayan dosyalar** (`.pdf`, `.zip`, `.txt`, `.csv` vb.) ürün
  klasöründe bulunabilir; sessizce yok sayılmaz, "Desteklenmeyen dosya türü"
  olarak işaretlenip görünür kalır, ama decode denenmez ve indekslemeyi
  durdurmaz.

Detay ve gerekçeler için bkz. `docs/ROADMAP.md` FAZ 4E ve `docs/DECISIONS.md`
#55-59.

---

## 5. Kurulum gereksinimleri

Self-contained Windows publish kullanıldığı için hedef bilgisayarda normal
şartlarda şunların kurulu olması **gerekmez**:

- Python
- Visual Studio
- Geliştirme ortamı
- .NET SDK

---

## 6. Mevcut doğrulama sonuçları

Doğrulanmış mevcut stress test verisi:

| | |
|---|---|
| Ürün havuzu | 11 gerçek ürün + 177 distractor = **188 aday** |
| Test sorgusu | 55 sentetik query (crop, downscale/upscale, jpeg, brightness, contrast) |
| Top-1 | %98.2 |
| Top-3 | %100 |
| Top-5 | %100 |
| Ortalama query süresi | ~51 ms |
| 177 yeni görselin ilk indekslemesi | ~10.5 sn |
| İkinci çalıştırmada cache-hit | yeni=0, değişmeyen=177, ~0.02 sn |

Not: Bu ölçüm Top-5 doğruluğu içindir (Faz 2/3C benchmark metodolojisi);
UI'da gösterilen sonuç sayısı Faz 4D'den beri **Top-10**'dur (bkz. §2, §8).

**Önemli:** Bu sonuçlar yalnızca mevcut test datasetine (188 aday) aittir.
"1000 üründe de %100 çalışır" gibi bir garanti değildir — **"188 aday ürün
üzerindeki mevcut testlerde gözlenen davranış"** olarak okunmalıdır. Gerçek
~5000 görsellik ölçek doğrulaması henüz yapılmadı (bkz. `docs/ROADMAP.md`
FAZ 4F).

---

## 7. Smart App Control notu

Geliştirme bilgisayarında, Windows Smart App Control'un imzasız geliştirme
binary'lerini bir süre engellediği gözlemlendi. Kullanıcı Smart App
Control'u manuel olarak kapattıktan sonra uygulama ve testler normal
çalıştı.

Bu, uygulamanın normal çalışma şartı **değildir**. Hedef bilgisayarda
`Lens.Desktop.exe` çalışmazsa veya "Application Control policy has blocked
this file" gibi bir hata görülürse, bunun Windows güvenlik / Application
Control politikasıyla ilişkili olabileceği bilinmelidir. Bu durumda IT/BT
ile birlikte değerlendirilmelidir.

---

## 8. MVP kapsamında olanlar

- Windows Desktop uygulaması
- C# / .NET
- WPF arayüz
- Local klasörden ürün görselleri alma
- CLIP ONNX embedding (openai/clip-vit-base-patch16)
- Persistent local index/cache (`%LocalAppData%\Lens\cache\`)
- Incremental index update (yeni/değişen/silinen dosya tespiti)
- Cosine similarity ile arama
- Top-10 görsel sonuç + query/sonuç karşılaştırma
- Similarity score gösterimi
- Sürükle-bırak, büyük önizleme/zoom, kalıcı log dosyası
- Geçici hata/bozuk cache/aşırı büyük görsel için güvenli davranış (bkz. §4)

---

## 9. MVP kapsamında olmayanlar

Bugünkü MVP'de **yok**:

- Login / authorization
- Fabrika DB entegrasyonu
- Yeni ürün ekleme / CRUD ekranı
- Text search
- Vector database
- Cloud
- Dış AI API

**Production hedefinde planlı** (bugün değil):

- Kullanıcı girişi + yetkilendirme
- Görsel kaynağı olarak local klasör + fabrika DB (ikisi birden)
- Lens uygulaması üzerinden yeni ürün ekleme
