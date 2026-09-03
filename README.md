# Lens

Bir fabrikanın daha önce ürettiği nevresim ürünlerini, yeni bir ürün görseli
verildiğinde geçmiş görseller arasından **görsel benzerlik** yoluyla bulmaya
yarayan bir Windows masaüstü uygulaması.

## Amaç

Kullanıcı bir ürün görseli verir (dosya seçerek veya sürükle-bırak ile) ve
bir minimum benzerlik (%) eşiği girer. Sistem, seçilen ürün klasöründeki
görseller arasından bu eşiği geçen (en fazla **15**) adayı (similarity
score ile) azalan sırada listeler. Sistem kesin "bu üründür" kararı vermez;
nihai kararı kullanıcı verir. Öncelik **recall**'dır — doğru ürünü kaçırmak,
birkaç fazla aday göstermekten daha kötü kabul edilir.

## Güncel Durum

**Bu proje henüz production onayı almamıştır.** FAZ 4A-4E (config/cache
mimarisi, dosya tarama/hata toleransı, logging, UI, reliability hardening)
tamamlandı; ardından bir "manager requirement paketi" turu (2026-09-03)
minimum benzerlik eşiği + en fazla 15 sonuç, shared/paylaşılan index +
tek-yazarlı kilit, sabit 50MB/50MP reddinin kaldırılması ve auto-index
checkbox'ını ekledi (detay: `docs/DECISIONS.md` #60-65). FAZ 4F (gerçek
~5000 görsellik veri setinde doğrulama), FAZ 4G (fabrika rollout) ve gerçek
UNC/SMB üzerinde manuel lock/atomic-save kabul testi henüz yapılmadı.
Detay: [`docs/ROADMAP.md`](docs/ROADMAP.md), [`docs/PRODUCTION_CHECKLIST.md`](docs/PRODUCTION_CHECKLIST.md).

Final AI model seçimi (CLIP) hâlâ **provisional/reversible** bir karardır,
production için kesin değildir — bkz. [`docs/DECISIONS.md`](docs/DECISIONS.md)
"Not Yet Decided" bölümü.

## Güncel Mimari (özet)

```
WPF UI → Ürün klasörü (local / UNC) → ImageIndex (dosya tarama + hata toleransı)
       → ClipEmbedder (ImageSharp preprocessing → CLIP ONNX, CPU)
       → <ÜrünKlasörü>\.lens\index.json (shared, atomic write, tek-yazarlı kilit)
       → Cosine similarity (brute-force) → eşiği geçen en fazla 15 sonuç
```

Detaylı veri akışı ve bileşen sorumlulukları için:
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Gereksinimler

- **Çalıştırmak için:** Windows 10/11 x64. Self-contained publish kullanıldığı
  için hedef makinede .NET runtime kurulu olması **gerekmez**.
- **Geliştirmek için:** Windows, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0),
  Git. Python yalnızca `benchmark/` altındaki model-export/değerlendirme
  araçları için gerekir (uygulamanın runtime'ı değildir).

## Geliştirme Ortamında Çalıştırma (hızlı özet)

```
git clone <repo-url>
cd Lens
dotnet build Lens.sln
dotnet run --project src/Lens.Desktop
```

CLIP ONNX modeli repoda **yoktur** (bkz. aşağıdaki "ONNX Modeli" bölümü) —
model dosyası elle yerleştirilmeden uygulama embed/arama işlevini kullanamaz.
Adım adım kurulum (model yerleştirme, örnek veri, appsettings.json, test):
[`docs/DEVELOPMENT_SETUP.md`](docs/DEVELOPMENT_SETUP.md).

## ONNX Modeli Neden Repoda Değil

`models/clip-vision-b16-openai.onnx` (CLIP vision encoder, `openai/clip-vit-base-patch16`'dan
export edilmiş) büyük bir binary dosya olduğu ve model dağıtım/lisans yaklaşımı
ayrıca değerlendirileceği için `.gitignore` ile commit dışı bırakılmıştır
(bkz. `docs/DECISIONS.md` #28). Model kaynağı, export yöntemi, giriş/çıkış
boyutları, lisans ve doğrulama bilgileri için: [`docs/MODEL_CARD.md`](docs/MODEL_CARD.md).

## Config, Index ve Log Konumları

**[Faz 1, 2026-09-03]** Embedding index'i artık ürün klasörünün kendi
içindedir (paylaşılan, `.lens/index.json`) — config ve log dosyaları hâlâ
`%LocalAppData%\Lens\` altındadır:

| Ne | Nerede |
|---|---|
| Admin varsayılan ürün dizini | `appsettings.json` (exe yanında, salt-okunur şablon) |
| Kullanıcı override / config / auto-index tercihi | `%LocalAppData%\Lens\config\` |
| **Embedding index (shared)** | `<Ürün Klasörü>\.lens\index.json` (+ `.lens\index.lock` — tek-yazarlı kilit dosyası) |
| Log dosyaları | `%LocalAppData%\Lens\logs\lens-yyyyMMdd.log` (30 gün retention) |

Detay: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md), [`docs/DECISIONS.md`](docs/DECISIONS.md) #61-62.

## UNC Ürün Klasörü Kullanımı

Ürün klasörü yerel bir dizin veya bir ağ paylaşımı (UNC, `\\sunucu\pay\...`)
olabilir. UNC erişimi yavaş/kesintili olabileceğinden, açılış ve indeksleme
öncesi kontroller arka planda (`Task.Run`) çalışır — UI donmaz; geçici bir
ağ hatasında önceki geçerli index korunur (bkz. `docs/DECISIONS.md` #55, #57).

## Build / Publish (özet)

```
dotnet build Lens.sln -c Release
dotnet publish src/Lens.Desktop/Lens.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/<hedef-klasör>
```

Publish çıktısının **tamamı** (exe + .NET runtime + ONNX Runtime + model +
`appsettings.json`) hedef makineye kopyalanmalıdır — yalnızca `.exe` yeterli
değildir. Detaylı adımlar, önkoşul kontrolleri ve rollback yaklaşımı için:
[`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) (genel release süreci) ve
[`docs/DEMO_DEPLOYMENT_GUIDE.md`](docs/DEMO_DEPLOYMENT_GUIDE.md) (ilk pilot/demo'ya özel).

## Bilinen Production Kısıtları

- Gerçek ~5000 görsellik veri setinde ölçüm henüz yapılmadı (FAZ 4F, bekliyor).
- Çoklu Lens örneği / eşzamanlı yazım koruması yok (bilinçli olarak ertelendi).
- `SixLabors.ImageSharp` MIT değil, Six Labors Split License ile lisanslanıyor —
  ticari kullanım için lisans uygunluğu **teyit edilmelidir**
  (bkz. [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md)).
- Code signing / Application Control (Smart App Control) engeli fabrika
  workstation'larında henüz doğrulanmadı (bkz. `docs/DEMO_DEPLOYMENT_GUIDE.md`).
- Diğer bilinçli olarak ertelenen maddeler ve açık kararlar için:
  [`docs/PRODUCTION_CHECKLIST.md`](docs/PRODUCTION_CHECKLIST.md).

## Kapsam Dışı (Bugünkü Uygulamada)

- Text search
- Login / kullanıcı yönetimi / yetkilendirme
- Ürün ekleme ekranı / CRUD / DB'ye yazma
- Fabrika veritabanı entegrasyonu (kalıcı olarak kapsam dışı — bkz. DECISIONS.md #31)
- Kullanıcı geçmişi, raporlama

## Dokümantasyon

| Doküman | İçerik |
|---|---|
| [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) | Güncel mimari ve veri akışı |
| [`docs/MODEL_CARD.md`](docs/MODEL_CARD.md) | CLIP ONNX modeli hakkında her şey |
| [`docs/DEVELOPMENT_SETUP.md`](docs/DEVELOPMENT_SETUP.md) | Sıfırdan geliştirme ortamı kurulumu |
| [`docs/DEPLOYMENT.md`](docs/DEPLOYMENT.md) | Genel release/dağıtım süreci |
| [`docs/DEMO_DEPLOYMENT_GUIDE.md`](docs/DEMO_DEPLOYMENT_GUIDE.md) | İlk pilot/demo dağıtımı |
| [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md) | Gereksinimlerin tarihçesi |
| [`docs/DECISIONS.md`](docs/DECISIONS.md) | Alınan/alınmamış tüm kararlar |
| [`docs/ROADMAP.md`](docs/ROADMAP.md) | Faz faz uygulama geçmişi ve durumu |
| [`docs/PRODUCTION_REQUIREMENTS.md`](docs/PRODUCTION_REQUIREMENTS.md) | Production gereksinimleri |
| [`docs/PRODUCTION_CHECKLIST.md`](docs/PRODUCTION_CHECKLIST.md) | Rollout öncesi açık maddeler |
| [`docs/DATA_PRIVACY.md`](docs/DATA_PRIVACY.md) | Veri işleme/gizlilik notu |
| [`docs/RELEASE_PROCESS.md`](docs/RELEASE_PROCESS.md) | GitHub/release süreci önerisi |
| [`docs/ARCHITECTURE_PROPOSAL.md`](docs/ARCHITECTURE_PROPOSAL.md) | Tarihsel mimari toplantı hazırlığı (karar içermez) |
| [`CONTRIBUTING.md`](CONTRIBUTING.md) | Katkı/geliştirme kuralları |
| [`SECURITY.md`](SECURITY.md) | Güvenlik açığı bildirimi |
| [`CHANGELOG.md`](CHANGELOG.md) | Sürüm geçmişi |
| [`CLAUDE.md`](CLAUDE.md) | AI-asistan çalışma kuralları |
