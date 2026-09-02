# GitHub / Release Süreci Önerisi

Bu doküman bir **öneridir** — hiçbir GitHub ayarı (branch protection, Actions
secrets vb.) bu doküman yazılırken değiştirilmemiştir. Uygulanması için repo
sahibinin/yöneticinin onayı ve GitHub üzerinde ayrıca yapılandırma gerekir.

## 1. `main` Branch Koruması

Önerilen ayarlar (GitHub → Settings → Branches):

- Doğrudan `main`'e push yasaklanır; tüm değişiklikler PR ile gelir.
- PR merge edilmeden önce en az 1 onay (review) zorunlu kılınır.
- (CI eklenirse) build/test check'lerinin yeşil olması merge şartı yapılır.

## 2. Pull Request Review

- Her PR, `CONTRIBUTING.md`'deki kontrol listesini (build 0/0, ilgiliyse
  `hardeningtest`) karşılamalı.
- Mimari/dependency/model kararı içeren PR'lar, `docs/DECISIONS.md`'ye yeni
  bir satır eklemeli (bkz. `CLAUDE.md` kural 3).

## 3. GitHub Actions — Build/Test Önerisi (henüz eklenmedi)

Önerilen minimal workflow (yalnızca tanım, `.github/workflows/*.yml` olarak
**eklenmemiştir** — bu turda yalnızca öneri istendi):

```yaml
# ÖNERİ — henüz repoya eklenmedi
name: build
on: [push, pull_request]
jobs:
  build:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'
      - run: dotnet build Lens.sln -c Release
```

**Model gerektirmeyen test stratejisi:** CI ortamında `models/clip-vision-b16-openai.onnx`
mevcut olmayacağı için (repoya commit edilmiyor, bkz. `docs/MODEL_CARD.md`),
`Lens.AiProof hardeningtest`'in **tamamı CI'da çalıştırılamaz** (Grup B/C/D
gerçek `ClipEmbedder` örneği oluşturur). Öneri:

- `dotnet build` her PR'da çalıştırılabilir (model gerekmez) — derleme
  hatalarını, tip hatalarını yakalar.
- Modelsiz çalışabilen kısımlar (örn. `hardeningtest` Grup A — bozuk/geçersiz
  cache doğrulaması, `ImageIndex.Load` üzerinde) ayrı bir CI-dostu test
  moduna ayrılabilir — bu bir **kod değişikliği önerisidir**, bu handover
  turunda uygulanmamıştır, ayrı bir onay/iş olarak ele alınmalıdır.
- Modeli gerektiren testler (Grup B/C/D) yalnızca model dosyasının mevcut
  olduğu bir self-hosted runner'da veya manuel/lokalde çalıştırılmaya devam
  edebilir.

## 4. Dependency Vulnerability Kontrolü

- `dotnet list package --vulnerable --include-transitive` düzenli aralıklarla
  (veya CI'a eklenirse her PR'da) çalıştırılması önerilir.
- Alternatif/ek: GitHub'ın yerleşik **Dependabot** özelliği (Settings →
  Code security) etkinleştirilerek NuGet bağımlılıkları için otomatik
  güvenlik uyarıları alınabilir.

## 5. Tag Tabanlı Release Süreci

Önerilen akış:

1. `main` üzerinde bir release'e hazır nokta belirlenir.
2. `docs/DEPLOYMENT.md`'deki adımlarla publish üretilir.
3. Publish çıktısı doğrulanır (ön-kontrol listesi, `docs/DEPLOYMENT.md` §2).
4. Bir git tag oluşturulur, örn. `v0.5.0-faz4e` (semver zorunlu değil, faz
  numarasıyla da etiketlenebilir — netleşene kadar).
5. GitHub Releases sayfasında bu tag için bir release notu açılır;
  `CHANGELOG.md`'nin ilgili bölümü buraya kopyalanır/link verilir.

## 6. Release Kaydında Bulunması Önerilenler

Her release notunda/kayıtta şu 4 bilgi birlikte tutulmalı (izlenebilirlik):

| Bilgi | Kaynak |
|---|---|
| Commit ID | `git rev-parse HEAD` |
| Uygulama sürümü | `.csproj`'a eklenecek bir `<Version>` alanı (şu an tanımlı değil — öneri) |
| Model SHA-256 | `sha256sum models/clip-vision-b16-openai.onnx` (bkz. `docs/MODEL_CARD.md`) |
| Publish klasörü SHA-256 (manifest) | Her dosyanın hash'i, örn. bu handover turunda kullanılan yöntem: `find publish/<klasör> -type f -exec sha256sum {} \; | sort -k2 > manifest.sha256` |

Bu 4 bilgi birlikte, "hangi kaynak koddan, hangi modelle, hangi tam publish
içeriğiyle üretildi" sorusunu kesin olarak cevaplar — rollback ve destek
taleplerinde kritik.

## 7. Authenticode İmzalama

Şu an publish çıktısı imzasızdır (bkz. `docs/DEPLOYMENT.md` §6). Production
rollout öncesi bir code-signing sertifikası edinilip `Lens.Desktop.exe`'nin
imzalanması önerilir — bu hem Application Control/Smart App Control
engellerini azaltır hem de dağıtılan binary'nin bütünlüğünü doğrulanabilir
kılar. Sertifika tedariki/süreci bu dokümanın kapsamı dışındadır (organizasyon
kararı).

## 8. Bu Önerilerin Durumu

Hiçbiri bu turda **uygulanmamıştır** — hepsi öneri niteliğindedir. Uygulanması
istenirse ayrı, onaylanmış adımlar olarak ele alınmalıdır (bkz. `CLAUDE.md`
kural 1, 3).
