# Production Rollout Checklist

Bu doküman, `docs/DECISIONS.md` "Not Yet Decided", `docs/ROADMAP.md`'nin
"bilerek ertelenen" maddeleri ve dağınık halde duran manuel-doğrulama
kalemlerini **tek bir aksiyon listesinde** toplar. FAZ 4G (production
rollout) öncesi bu listenin gözden geçirilmesi önerilir.

## Karar Bekleyen Açık Sorular

Kaynak: `docs/DECISIONS.md` "Not Yet Decided".

- [ ] Production için final model seçimi: CLIP mi, SigLIP mi, başka bir model
  mi? (MVP'de CLIP provisional — `docs/DECISIONS.md` #20)
- [ ] Dış AI servislerine (cloud API) izin verilip verilmeyeceği.
- [ ] Çoklu kullanıcı / eşzamanlılık gereksinimleri netleşmedi.
- [ ] Test/benchmark metodolojisinin (sentetik varyasyonlar) gerçek saha
  koşullarını ne kadar temsil ettiği doğrulanmadı.
- [ ] Aynı desen farklı renk = aynı ürün mü sayılmalı? (production için
  kesin değil, MVP'de geçici "evet" kabul edildi — `docs/DECISIONS.md` #25)

## Bilinçli Olarak Ertelenen Teknik Maddeler

Kaynak: `docs/ROADMAP.md` FAZ 4E "bilerek ertelenenler".

- [ ] Model/preprocessing cache versioning (model değişince cache'in
  otomatik geçersiz sayılması).
- [ ] Cache içeriğinin SHA-256 ile doğrulanması.
- [ ] Aynı anda birden fazla Lens örneğine karşı dosya kilidi/mutex.
- [ ] Atomic-write'a ek "backup dosyası" geliştirmesi.
- [ ] Dosya kimliği için boyut+zaman damgası yerine tam dosya hash'i.
- [ ] Log gizliliği/redaction refactor'ü.
- [ ] `MainWindow`'un büyük refactor'ü / MVVM dönüşümü.
- [ ] Bağımlılık/.NET sürüm yükseltmeleri.
- [ ] Code signing.
- [ ] Installer/MSI.
- [ ] Vector database (yalnızca ölçek çok büyürse, bkz. `docs/DECISIONS.md` #32).

## Lisans / Uyumluluk

- [ ] **`SixLabors.ImageSharp` (Six Labors Split License) ticari kullanım
  uygunluğu** — şirket ölçeğine göre ücretsiz kullanım sınırının aşılıp
  aşılmadığı teyit edilmeli (bkz. `THIRD_PARTY_NOTICES.md`). **Yüksek öncelik**
  — bu netleşmeden production dağıtımı yapılmamalıdır.
- [ ] CLIP modelinin lisansı teyit edilmeli (bkz. `docs/MODEL_CARD.md`).
- [ ] `LICENSE` dosyasındaki telif hakkı sahibi alanı doldurulmalı.
- [ ] Resmi bir release'in model dosyası SHA-256'sı kaydedilmeli
  (bkz. `docs/MODEL_CARD.md` "SHA-256 Doğrulama Yaklaşımı").

## Manuel Doğrulama Gereken Öğeler (Faz 4E sonrası, otomasyonla test edilemedi)

- [ ] Gerçek bir UNC pay üzerinde açılış + "İndeksi Güncelle" (ağ kesintisi
  simülasyonu dahil).
- [ ] Sürükle-bırak görsel geri bildirimi (accent border, fareyi takip eden
  drag-preview) — gerçek OS-seviyesi davranış otomasyonla doğrulanamadı.
- [ ] Çok büyük bir görseli sürükleyip drop zone üzerinde birkaç saniye
  tutma (drop etmeden) — bağımsız code review'da MEDIUM bulgu: drag-hover
  thumbnail yükleyicisi resource guard'ı çağırmıyor (bkz. `docs/ROADMAP.md`
  FAZ 4E, review notu).
- [ ] Büyük önizleme/zoom (çift tık, tekerlek zoom, pan, ESC).
- [ ] Top-10 seçim rengi + query/sonuç karşılaştırma akışı.
- [ ] `publish/` klasörünü hedef makineye kopyalayıp çalıştırma,
  `appsettings.json`'a gerçek UNC path girme.

## Ölçek Doğrulaması (FAZ 4F, henüz yapılmadı)

- [ ] Gerçek ~5000 görsellik veri setiyle ilk indeksleme süresi/bellek ölçümü.
- [ ] ~5000 aday havuzunda query süresi (5 saniye hedefiyle karşılaştırma).
- [ ] Brute-force cosine similarity'nin bu ölçekte yeterliliğinin somut
  sayılarla doğrulanması.

## Rollout (FAZ 4G, henüz yapılmadı)

- [ ] Gerçek UNC path ile uçtan uca test.
- [ ] Hedef workstation'da Application Control (Smart App Control vb.)
  engeli olup olmadığının tespiti.
- [ ] Mapped drive (`Z:\...`) senaryosunun manuel doğrulanması (alternatif
  olarak).

---

Bu liste `docs/DECISIONS.md`, `docs/ROADMAP.md` ve `docs/PRODUCTION_REQUIREMENTS.md`'nin
**yerine geçmez** — bu belgelerin dağınık açık maddelerini eyleme geçirilebilir
tek bir yerde toplar. Bir madde kapandığında hem burada hem kaynak dokümanda
işaretlenmelidir.
