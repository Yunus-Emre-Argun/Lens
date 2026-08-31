# Proje Bağlamı — Lens

Bu doküman, projenin şu ana kadar konuşulmuş gereksinimlerini ve kısıtlarını
kaydeder. Amaç: gelecekteki çalışma turlarında ve mimari toplantıda ortak bir
referans noktası olmak.

Notasyon: **Confirmed** = kullanıcı tarafından açıkça belirtildi.
**Open Question** = henüz netleşmedi, karar bekliyor.
**Later Phase** = ilk PoC/MVP kapsamı dışında, ileride değerlendirilecek.

---

## 1. Problem Tanımı

**Confirmed**
- Fabrika, daha önce ürettiği nevresim ürünlerine ait görselleri arşivliyor.
- Kullanıcı yeni bir ürün görseli verecek; sistem geçmiş görseller arasında
  aynı veya en benzer ürünleri bulacak.
- Öncelik: **recall**. Doğru ürünü kaçırmak, birkaç fazla aday göstermekten
  daha kötü bir sonuç kabul ediliyor.
- Sistem kesin "bu üründür" kararı vermeyecek; ~5-10 aday listeleyecek.
- Nihai kararı kullanıcı (fabrika çalışanı) verecek.

---

## 2. MVP / PoC Kapsamı

**Confirmed**
- Hedef: 1-2 gün içinde yöneticilere çalışan bir demo göstermek.
- İlk versiyon sadece **görselle arama** içerir.
- Demo, klasörde bulunan mevcut görseller üzerinden çalışabilir
  (gerçek DB entegrasyonu zorunlu değil).

**Later Phase**
- Text search.
- Login / kimlik doğrulama.
- Kullanıcı geçmişi.
- Raporlama.
- Gerçek fabrika veritabanı entegrasyonu.

---

## 3. Mevcut Veri

**Confirmed**
- Şu anda 11 adet nevresim görseli mevcut; her biri farklı bir ürüne ait.
- Görseller temiz, katalog/dijital tasarım görünümünde (telefon fotoğrafı değil).
- Ürünler arası temel ayrım: **desen ve renk**.
- Ölçü (boyut) bilgisi görselden belirgin değil.
- İleride yönetici daha fazla görsel sağlayabilir; görseller klasör halinde
  veya ileride SQL tabanlı fabrika veritabanından gelebilir.

**Open Question**
- İleride kaç görsele çıkılacağı net değil (yaklaşık ~1.000 rakamı bir
  ölçek varsayımı olarak konuşuldu, kesin taahhüt değil).

---

## 4. Query (Arama) Görseli

**Confirmed**
- Aranan görsel, kayıtlı görselin birebir aynı dosyası olmak zorunda değil.
- Farklı çözünürlük, crop veya küçük görsel değişiklikler olabilir.

**Open Question**
- Aynı ürüne ait gerçek ikinci fotoğraflar şu anda yok. Bu nedenle PoC
  sırasında test amaçlı kontrollü/sentetik varyasyonlar (crop, resize,
  parlaklık vb.) kullanılması düşünülüyor — ama bu varyasyonların gerçek
  saha koşullarını ne kadar temsil ettiği bilinmiyor (bkz. ARCHITECTURE_PROPOSAL.md).

---

## 5. Platform

**Confirmed**
- Windows masaüstü uygulaması hedefleniyor.
- Sadece fabrika içinde kullanılacak.
- Merkezi fabrika veritabanı mevcut; SQL tabanlı olduğu düşünülüyor ama
  tam DBMS henüz bilinmiyor.

**Open Question**
- İleride birden fazla kullanıcı senaryosu var, ama eşzamanlılık/çoklu
  erişim gereksinimleri netleşmedi.

**Later Phase**
- Gerçek DB entegrasyonu.

---

## 6. Performans

**Confirmed**
- Arama sonucu tercihen 5 saniyeden uzun sürmemeli.
- Uygulama normal bir ofis bilgisayarında çalışacak; güçlü GPU garanti değil.
- Local-first yaklaşımı şu an tercih edilen aday, ama kesin mimari karar değil.

**Open Question**
- Dış AI servislerinin (cloud API vb.) kullanımına izin verilip verilmeyeceği
  yönetimle netleştirilmedi.

---

## 7. AI / Model

**Confirmed**
- Şu ana kadar konuşulan adaylar: CLIP, SigLIP.
- Hiçbir model henüz seçilmedi.
- "Daha yeni olduğu için SigLIP" gibi varsayımlar yapılmayacak; modeller
  gerçek veri üzerinde test edilerek karşılaştırılacak.

---

## 8. Geliştirme Ortamı (Bu Makine — Gözlem)

**Confirmed (bu turda read-only olarak tespit edildi)**
- İşletim sistemi: Windows 11 Pro (Build 26200), x64.
- Git: 2.55.0 kurulu, proje klasörü artık bir git repo (`git init` yapıldı).
- Python: 3.10.9 kurulu (`C:\Users\win11\AppData\Local\Programs\Python\Python310`).
  `pip` 26.2.1 mevcut.
- Bu geliştirme makinesinde bir NVIDIA GPU (RTX 5060 Ti, ~16 GB VRAM,
  driver 610.74) tespit edildi.

**Önemli not**
- Bu makinede GPU bulunması, hedef fabrika/ofis bilgisayarlarında da GPU
  olacağı anlamına gelmez. Gereksinimde "güçlü GPU garanti değil" açıkça
  belirtildiği için, mimari CPU-only senaryoyu esas almalı; GPU varsa bonus
  hız kazancı olarak değerlendirilmeli.
