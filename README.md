# Lens

Bir fabrikanın daha önce ürettiği nevresim ürünlerini, yeni bir ürün görseli
verildiğinde geçmiş görseller arasından görsel benzerlik yoluyla bulmaya
yarayacak bir araç.

## Amaç

Kullanıcı bir ürün görseli verir. Sistem, geçmişte üretilmiş görseller arasından
aynı veya en benzer ürünleri (yaklaşık 5-10 aday) listeler. Nihai kararı kullanıcı
verir; sistem kesin eşleşme iddiasında bulunmaz.

## Mevcut Durum

- **Faz 1-2 (tamamlandı):** Python ile CLIP vs SigLIP benchmark'ı çalıştırıldı
  (11 görsel + sentetik varyasyon, 55 sorgu). Sonuçlar: `benchmark/results/report.md`.
  Bu kod bir engineering/değerlendirme aracı olarak kalır, uygulamanın
  runtime'ı değildir.
- **Faz 3 (başlıyor):** Gerçek Lens uygulaması Windows masaüstü, **C#/.NET**
  ile geliştirilecek. Bugünkü MVP kapsamı yalnızca local klasörden görselle
  arama içerir (bkz. `docs/PROJECT_CONTEXT.md` Bölüm 2).
- Final AI model seçimi (CLIP/SigLIP) henüz **Confirmed değildir**
  (bkz. `docs/DECISIONS.md`).

Detaylar için:

- Proje bağlamı: [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md)
- Alınan / alınmamış kararlar: [`docs/DECISIONS.md`](docs/DECISIONS.md)
- Mimari toplantı hazırlığı: [`docs/ARCHITECTURE_PROPOSAL.md`](docs/ARCHITECTURE_PROPOSAL.md)
- Demo / deployment rehberi: [`docs/DEMO_DEPLOYMENT_GUIDE.md`](docs/DEMO_DEPLOYMENT_GUIDE.md)
- Çalışma kuralları: [`CLAUDE.md`](CLAUDE.md)

## Kapsam Dışı (Bugünkü MVP'de)

- Text search
- Login / kullanıcı yönetimi / yetkilendirme (production'da planlı, MVP'de yok)
- Ürün ekleme ekranı / CRUD / DB'ye yazma (production'da planlı, MVP'de yok)
- Fabrika veritabanı entegrasyonu (production'da local klasöre ek ikinci
  kaynak olarak planlı, MVP'de yok)
- Kullanıcı geçmişi, raporlama
