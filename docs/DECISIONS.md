# Kararlar — Lens

Bu doküman alınmış ve henüz alınmamış mimari/teknik kararları ayrı ayrı listeler.
Alınmamış kararlarda kesin hüküm verilmez; alternatifler `ARCHITECTURE_PROPOSAL.md`
içinde tartışılır.

---

## Confirmed (Alınmış Kararlar)

| # | Karar | Not |
|---|-------|-----|
| 1 | Proje klasörü git ile versiyonlanacak | `git init` bu turda yapıldı |
| 2 | İlk PoC kapsamı yalnızca görselle arama | Text search, login vb. dahil değil |
| 3 | İlk demo, klasördeki mevcut görsellerle çalışabilir | Gerçek DB entegrasyonu ilk demo için zorunlu değil |
| 4 | Hedef platform Windows masaüstü | Fabrika içi kullanım |
| 5 | Model karşılaştırması varsayıma değil ölçüme dayanacak | CLIP/SigLIP arasında moda/yenilik gerekçesiyle seçim yapılmayacak |
| 6 | **[Faz 1]** PoC mimarisi local-first | Dış AI servisi kullanılmayacak (PoC kapsamında) |
| 7 | **[Faz 1]** Uygulama dili Python | — |
| 8 | **[Faz 1]** Görsel kaynağı klasör tabanlı | Gerçek DB entegrasyonu sonraki faz |
| 9 | **[Faz 1]** Pipeline: görsel → embedding → cosine similarity → Top-5 sonuç | Önceki "5-10 aday" ifadesi Top-5 olarak netleşti |
| 10 | **[Faz 1]** Nihai kararı kullanıcı verir | Sistem kesin eşleşme iddia etmez |
| 11 | **[Faz 1]** ~1.000 görsel ölçeğinde vector database kullanılmayacak, brute-force arama yapılacak | ARCHITECTURE_PROPOSAL.md Bölüm 10'daki tahmin onaylandı; CPU'da ayrıca ölçülecek |
| 12 | **[Faz 1]** GUI bu fazda yok | Sonraki faz konusu |
| 13 | **[Faz 1]** Gerçek fabrika DB entegrasyonu sonraki faz | Değişmedi |

---

## Not Yet Decided (Henüz Alınmamış Kararlar)

Bu konularda **kesin hüküm verilmemiştir**.

| # | Konu | Neden Açık |
|---|------|------------|
| 1 | Model seçimi: CLIP mi SigLIP mi, yoksa başka bir model mi | Faz 2 konusu — özellikle **desen** benzerliği açısından küçük bir benchmark ile test edilecek |
| 2 | Embedding depolama biçimi (düz dosya / numpy / SQLite vb.) | Ölçek ve erişim paterni netleşmeden karar verilemez |
| 3 | Dış AI servislerine (cloud API) izin verilip verilmeyeceği | PoC kapsamında kullanılmayacağı netleşti (karar #6); ileri faz için hâlâ açık |
| 4 | Gerçek fabrika veritabanı DBMS türü | "SQL tabanlı olduğu düşünülüyor" — kesin değil |
| 5 | Çoklu kullanıcı / eşzamanlılık gereksinimleri | İleri faz, detay netleşmedi |
| 6 | Arayüz teknolojisi (örn. Python GUI framework, .NET vb.) | Henüz değerlendirilmedi |
| 7 | Test/benchmark metodolojisi (sentetik varyasyonların temsil gücü) | Gerçek ikinci fotoğraf verisi yok |
| 8 | **[Faz 1]** Aynı desen farklı renk aynı ürün mü sayılmalı? | Kesin değil; bu yüzden Faz 2 testlerinde agresif renk/hue değişimi **yapılmayacak** |

---

## Later Phase (Şimdilik Karar Gerektirmeyen)

- Text search mimarisi
- Login / kullanıcı yönetimi
- Raporlama
- Gerçek fabrika DB entegrasyon detayları
- Çoklu kullanıcı ölçeklenmesi
- GUI (arayüz teknolojisi ve tasarımı)
- Vector database (ölçek ~1.000'den çok büyürse yeniden değerlendirilecek)
- Dış AI servisi kullanımı (yönetim onayı verirse)

---

## Karar Alma Süreci

Yukarıdaki "Not Yet Decided" maddeleri, kullanıcı ve/veya Tech Lead/CTO ile
yapılacak mimari toplantı sonrası bu tabloya "Confirmed" olarak taşınacaktır.
Onay olmadan hiçbir madde implementasyona esas alınmaz (bkz. `CLAUDE.md`).
