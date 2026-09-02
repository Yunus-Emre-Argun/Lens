# Katkı / Geliştirme Kuralları

Bu proje üzerinde çalışırken uyulması gereken kurallar `CLAUDE.md`'de
tanımlıdır — bu doküman onu **tekrar etmez**, pratik/GitHub-akışı tarafını
özetler. Çelişki durumunda `CLAUDE.md` esastır.

## Temel İlkeler (özet — detay: `CLAUDE.md`)

- **Önce plan, sonra kod.** Belirsiz/büyük bir değişiklik öncesi kısa bir
  yaklaşım özeti paylaşılır, onay beklenir.
- **Kapsamı kendiliğinden genişletme.** İstenen iş yapılır, "madem
  buradayım" eklemeleri yapılmaz.
- **Mimari/dependency kararları onaysız alınmaz.** Model seçimi, yeni bir
  NuGet/pip paketi, depolama biçimi değişikliği gibi kararlar
  `docs/DECISIONS.md`'de "Confirmed" olmadan uygulamaya alınmaz.
- **Küçük, geri alınabilir değişiklikler.** Büyük tek seferlik commit'lerden
  kaçınılır.
- **Secrets commit edilmez.** API anahtarı, gerçek UNC yolu, kimlik bilgisi
  vb. hiçbir şekilde repoya girmez (bkz. `SECURITY.md`, `docs/DATA_PRIVACY.md`).

## Branch / Commit / PR Akışı

- `main` korumalı kabul edilir (bkz. `docs/RELEASE_PROCESS.md`) — doğrudan
  push yerine bir feature branch + PR akışı önerilir.
- Commit mesajları: kısa, "ne" değil "neden" odaklı bir özet satırı; gerekirse
  detay gövdede.
- Bir PR açmadan önce:
  ```
  dotnet build Lens.sln
  ```
  0 warning/0 error vermeli. Değişiklik indeksleme/cache/embedding mantığını
  etkiliyorsa:
  ```
  dotnet run --project src/Lens.AiProof -- hardeningtest
  ```
  da çalıştırılıp sonucu PR açıklamasına eklenmelidir.
- PR açıklamasında: ne değişti, hangi dosyalar etkilendi, hangi testler
  çalıştırıldı, `docs/DECISIONS.md`'ye eklenmesi gereken yeni bir karar var mı.

## Kod Stili

Ayrı bir linter/formatter yapılandırması yoktur (MVP/PoC kapsamı). Mevcut
dosyalardaki stille tutarlı kalın: Türkçe kod yorumları (mevcut konvansiyon),
`Nullable` etkin, `ImplicitUsings` etkin (bkz. `.csproj` dosyaları).

## Test Verisi

Gerçek fabrika ürün görselleri, gerçek UNC yolları veya tanımlanabilir
müşteri/kişi bilgisi içeren dosyalar **hiçbir şekilde** commit edilmez. Test
için `docs/DEVELOPMENT_SETUP.md` §4'teki gibi kendi örnek görsellerinizi
kullanın.

## Yeni Bir Bağımlılık Eklemek İstiyorsanız

`CLAUDE.md` kural 6 gereği: neden gerekli olduğunu, alternatiflerini ve
maliyetini (kurulum boyutu, **lisans**, bakım yükü) kısaca açıklayıp onay
isteyin. Not: mevcut `SixLabors.ImageSharp` bağımlılığı MIT değil, Six Labors
Split License ile lisanslıdır — yeni bir bağımlılık önerirken benzer bir
lisans kontrolünü atlamayın (bkz. `THIRD_PARTY_NOTICES.md`).

## Sorular

Mimari/gereksinim geçmişi için önce `docs/DECISIONS.md` ve `docs/ROADMAP.md`'ye
bakın — çoğu "neden böyle yapıldı" sorusunun cevabı oradadır.
