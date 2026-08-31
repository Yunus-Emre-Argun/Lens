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

---

## Not Yet Decided (Henüz Alınmamış Kararlar)

Bu konularda **kesin hüküm verilmemiştir**. Mimari toplantıda netleştirilmesi
gerekir.

| # | Konu | Neden Açık |
|---|------|------------|
| 1 | Model seçimi: CLIP mi SigLIP mi, yoksa başka bir model mi | Gerçek veri üzerinde test edilmedi |
| 2 | Embedding depolama biçimi (düz dosya / numpy / SQLite vb.) | Ölçek ve erişim paterni netleşmeden karar verilemez |
| 3 | Vector database kullanılıp kullanılmayacağı | ~1.000 görsel ölçeğinde gerçekten gerekli olduğu kanıtlanmadı |
| 4 | Dış AI servislerine (cloud API) izin verilip verilmeyeceği | Yönetimle netleştirilmedi (veri güvenliği/maliyet bilinmiyor) |
| 5 | Gerçek fabrika veritabanı DBMS türü | "SQL tabanlı olduğu düşünülüyor" — kesin değil |
| 6 | Çoklu kullanıcı / eşzamanlılık gereksinimleri | İleri faz, detay netleşmedi |
| 7 | Arayüz teknolojisi (örn. Python GUI framework, .NET vb.) | Henüz değerlendirilmedi |
| 8 | Test/benchmark metodolojisi (sentetik varyasyonların temsil gücü) | Gerçek ikinci fotoğraf verisi yok |

---

## Later Phase (Şimdilik Karar Gerektirmeyen)

- Text search mimarisi
- Login / kullanıcı yönetimi
- Raporlama
- Gerçek fabrika DB entegrasyon detayları
- Çoklu kullanıcı ölçeklenmesi

---

## Karar Alma Süreci

Yukarıdaki "Not Yet Decided" maddeleri, kullanıcı ve/veya Tech Lead/CTO ile
yapılacak mimari toplantı sonrası bu tabloya "Confirmed" olarak taşınacaktır.
Onay olmadan hiçbir madde implementasyona esas alınmaz (bkz. `CLAUDE.md`).
