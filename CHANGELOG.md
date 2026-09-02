# Changelog

Bu doküman [Keep a Changelog](https://keepachangelog.com/) biçimini takip
eder. Aşağıdaki geçmiş girdileri, mevcut git commit geçmişi ve
`docs/ROADMAP.md`'deki faz kayıtlarından **geriye dönük** doldurulmuştur —
bu proje henüz tag tabanlı bir release süreci kullanmadığı için sürüm
numarası yerine faz adı ve tarih kullanılmıştır. Buradan sonrası
`docs/RELEASE_PROCESS.md`'de önerilen tag tabanlı release sürecine göre
güncellenmelidir.

## [Faz 4E] — 2026-09-02 — Reliability Hardening

### Eklendi
- Geçici dosya/network hatasında eski sağlam index kaydının korunması.
- Bozuk/uyumsuz cache dosyası için güvenli recovery (crash yok).
- UNC/network operasyonlarında UI freeze/crash riskinin azaltılması.
- Büyük/aşırı çözünürlüklü görsel için resource guard
  (`Lens.Core.Ai.ImageResourceLimits`, ~50MB/~50MP).
- PDF/ZIP/TXT gibi görsel olmayan dosyalar için "Desteklenmeyen dosya türü"
  görünürlüğü.
- `AlertWindow` (native `MessageBox` yerine tutarlı özel uyarı penceresi).
- `Lens.AiProof hardeningtest` modu — 29 fonksiyonel doğrulama testi.

Detay: `docs/ROADMAP.md` FAZ 4E, `docs/DECISIONS.md` #55-59.

## [Faz 4D] — 2026-09-01/02 — UI/UX Polish

### Eklendi
- Top-5 → Top-10 sonuç gösterimi.
- Query/karşılaştırma alanı, "Yeni Arama" butonu, minimal "⋮" menü.
- Büyük görsel önizleme/zoom penceresi (`ImagePreviewWindow`).
- Sürükle-bırak (drag & drop) query seçimi, drag preview, görsel geri
  bildirim (accent border, %100 eşleşme vurgusu, kopyalanabilir dosya adları).

Detay: `docs/ROADMAP.md` FAZ 4D, `docs/DECISIONS.md` #47-48, #52-54.

## [Faz 4B/4C] — 2026-09-01 — Robust Indexing & Logging

### Eklendi
- Dosya sınıflandırması (`SupportedImage`/`UnsupportedImageFormat`/`NonImage`),
  `IndexUpdateStats`/`IndexFileIssue` veri modeli.
- Search-before-refresh: 30 saniyelik freshness TTL ile otomatik incremental
  güncelleme.
- Kendi kodu ile dosya tabanlı logging (`ILensLogger`/`FileLogger`,
  `%LocalAppData%\Lens\logs\`, 30 gün retention).

Detay: `docs/ROADMAP.md` FAZ 4B, FAZ 4C.

## [Faz 4A] — 2026-09-01 — Configuration & Storage Architecture

### Eklendi
- Index/cache'in ürün klasöründen `%LocalAppData%\Lens\cache\<hash>\`'e
  taşınması, atomic write.
- Admin default (`appsettings.json`) / kullanıcı override (`%LocalAppData%\Lens\config\`)
  ayrımı.

Detay: `docs/ROADMAP.md` FAZ 4A, `docs/DECISIONS.md` #39-45.

## [Faz 3] — 2026-09-01 — C#/.NET WPF MVP

### Eklendi
- Lens Desktop uygulamasının ilk sürümü: klasör seçme, CLIP ONNX embedding
  (.NET/ONNX Runtime), persistent JSON index, Top-5 sonuç gösterimi
  (sonradan Faz 4D'de Top-10'a çıkarıldı).
- İlk demo dağıtım rehberi (`docs/DEMO_DEPLOYMENT_GUIDE.md`).

## [Faz 1-2] — 2026-08-31 — Model Değerlendirme (Python Benchmark)

### Eklendi
- CLIP vs SigLIP karşılaştırma aracı (Python), 11 gerçek ürün görseli + 55
  sentetik varyasyon üzerinde benchmark. Bu araç, uygulamanın runtime'ı
  değildir — yalnızca model seçim kararını desteklemek için kullanılmıştır.
- İlk proje/mimari hazırlık dokümanları (`docs/PROJECT_CONTEXT.md`,
  `docs/ARCHITECTURE_PROPOSAL.md`, `CLAUDE.md`).

---

## [Unreleased]

Bu bölüm, repo handover/release-hazırlığı çalışmasının (dokümantasyon,
`LICENSE`/`CONTRIBUTING`/`SECURITY`/`THIRD_PARTY_NOTICES`, `docs/ARCHITECTURE.md`,
`docs/MODEL_CARD.md` vb.) eklendiği bu turu kapsar — uygulama davranışında
bir değişiklik yoktur.
