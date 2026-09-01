# Lens — Demo Deployment Guide

Bu doküman, Lens MVP'yi başka bir Windows bilgisayarında (özellikle yönetici
bilgisayarında) nasıl çalıştıracağımızı anlatır. Teknik rapor değildir —
kısa ve uygulamaya yöneliktir.

---

## 1. Demo için gerekenler

**Yönetici bilgisayarına kaynak kod götürülmez.**

Sadece `publish/Lens.Desktop-win-x64/` klasörünün **TAMAMI** kopyalanır.
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

1. `publish/Lens.Desktop-win-x64/` klasörünü hedef Windows bilgisayarına kopyala.
2. `Lens.Desktop.exe` dosyasını çalıştır.
3. **"Ürün Klasörü Seç"** ile fabrika ürün/desen klasörünü seç.
4. **"İndeksi Güncelle / Klasörü Tara"** ile ürünleri indeksle.
5. İlk indekslemede ürün görsellerinin embedding'leri oluşturulur.
6. Sonraki çalıştırmalarda değişmeyen görseller tekrar embed edilmez.
7. **"Sorgu Görseli Seç"** ile aranacak görseli seç.
8. **"Ara"** butonuna bas.
9. Top-5 sonuçlarını incele.
10. Nihai kararı kullanıcı verir — sistem kesin eşleşme iddia etmez.

---

## 3. Yeni ürün görselleri geldiğinde

Örnek: yönetici ilk seferde 200 ürün görseli verirse:

```
200 görsel → ilk indeksleme → embedding'ler oluşturulur → .lens_index.json'a kaydedilir
```

Daha sonra klasöre 20 yeni ürün eklenirse:

- Sadece yeni 20 görsel embed edilir.
- Değişmeyen 200 görsel yeniden hesaplanmaz.
- Değişmiş dosyalar yeniden embed edilir.
- Silinmiş dosyalar index'ten çıkarılır.

Persistent index/cache dosyası, seçilen ürün klasörünün içinde
**`.lens_index.json`** olarak tutulur.

---

## 4. Kurulum gereksinimleri

Self-contained Windows publish kullanıldığı için hedef bilgisayarda normal
şartlarda şunların kurulu olması **gerekmez**:

- Python
- Visual Studio
- Geliştirme ortamı
- .NET SDK

---

## 5. Mevcut doğrulama sonuçları

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

**Önemli:** Bu sonuçlar yalnızca mevcut test datasetine (188 aday) aittir.
"1000 üründe de %100 çalışır" gibi bir garanti değildir — **"188 aday ürün
üzerindeki mevcut testlerde gözlenen davranış"** olarak okunmalıdır.

---

## 6. Smart App Control notu

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

## 7. MVP kapsamında olanlar

- Windows Desktop uygulaması
- C# / .NET
- WPF arayüz
- Local klasörden ürün görselleri alma
- CLIP ONNX embedding (openai/clip-vit-base-patch16)
- Persistent local index/cache
- Incremental index update (yeni/değişen/silinen dosya tespiti)
- Cosine similarity ile arama
- Top-5 görsel sonuç
- Similarity score gösterimi

---

## 8. MVP kapsamında olmayanlar

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
