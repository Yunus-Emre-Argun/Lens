# Lens

Bir fabrikanın daha önce ürettiği nevresim ürünlerini, yeni bir ürün görseli
verildiğinde geçmiş görseller arasından görsel benzerlik yoluyla bulmaya
yarayacak bir araç.

## Amaç

Kullanıcı bir ürün görseli verir. Sistem, geçmişte üretilmiş görseller arasından
aynı veya en benzer ürünleri (yaklaşık 5-10 aday) listeler. Nihai kararı kullanıcı
verir; sistem kesin eşleşme iddiasında bulunmaz.

## Mevcut Durum

Bu proje şu anda **planlama / mimari hazırlık** aşamasındadır.
Henüz uygulama kodu yazılmamıştır. Teknoloji seçimleri (model, depolama biçimi,
arayüz teknolojisi vb.) **kesinleşmemiştir**.

Detaylar için:

- Proje bağlamı: [`docs/PROJECT_CONTEXT.md`](docs/PROJECT_CONTEXT.md)
- Alınan / alınmamış kararlar: [`docs/DECISIONS.md`](docs/DECISIONS.md)
- Mimari toplantı hazırlığı: [`docs/ARCHITECTURE_PROPOSAL.md`](docs/ARCHITECTURE_PROPOSAL.md)
- Çalışma kuralları: [`CLAUDE.md`](CLAUDE.md)

## Kapsam Dışı (Şu Aşamada)

- Text search
- Login / kullanıcı yönetimi
- Kullanıcı geçmişi, raporlama
- Gerçek fabrika veritabanı entegrasyonu
