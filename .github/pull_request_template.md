## Ne Değişti

## Neden

## Etkilenen Dosyalar

## Test

- [ ] `dotnet build Lens.sln` → 0 warning / 0 error
- [ ] İndeksleme/cache/embedding mantığı etkilendiyse:
      `dotnet run --project src/Lens.AiProof -- hardeningtest` çalıştırıldı ve sonucu eklendi
- [ ] UI değişikliği varsa manuel olarak denendi

## Kontrol Listesi

- [ ] Mimari/dependency/model kararı içeriyorsa `docs/DECISIONS.md`'ye eklendi
- [ ] Gerçek ürün görseli, gerçek UNC yolu, secret/credential veya model
      dosyası bu PR'da commit edilmedi (bkz. `SECURITY.md`, `docs/DATA_PRIVACY.md`)
- [ ] Kapsamı genişletecek ek değişiklik yapılmadı (bkz. `CLAUDE.md` kural 2)
