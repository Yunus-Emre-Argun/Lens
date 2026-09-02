# Güvenlik Politikası

## Desteklenen Sürümler

Bu proje henüz production onayı almamış bir MVP/PoC aşamasındadır (bkz.
`README.md`, `docs/ROADMAP.md`). Resmi bir sürüm/destek matrisi yoktur —
`main` branch'in en güncel hali esas alınır.

## Güvenlik Açığı Bildirimi

Bir güvenlik açığı tespit ederseniz, lütfen **genel bir GitHub issue
açmayın** (açık bir açığın herkese görünür olması istenmez). Bunun yerine
GitHub'ın private bildirim mekanizmasını kullanın:

1. Repo sayfasında **Security → Report a vulnerability** (Security Advisory)
   bölümünden özel bir rapor açın.
2. Raporunuzda: etkilenen dosya/bileşen, tekrar üretme adımları, ve varsa
   önerilen çözüm yönünü belirtin.

## Bildirirken Dikkat Edilmesi Gerekenler

Bu uygulama, yerel/UNC dosya sistemi üzerinden gerçek fabrika ürün görselleri
ile çalışır. Bir güvenlik raporu hazırlarken:

- **Gerçek ürün görselleri veya gerçek UNC yolları paylaşmayın** — hem bir
  gizlilik hem de bu deponun genel görünürlüğü açısından risk taşır (bkz.
  `docs/DATA_PRIVACY.md`).
  raporunuzu, gerçek veriler yerine anonimleştirilmiş/sentetik örneklerle
  destekleyin.
- Kimlik bilgisi, API anahtarı veya benzeri bir sızıntı bulduysanız, bunun
  **değerini** paylaşmayın — yalnızca nerede bulunduğunu bildirin.

## Bilinen Kapsam Dışı Alanlar

- Bu bir tek-kullanıcı, dahili masaüstü aracıdır; login/yetkilendirme yoktur
  (bilinçli MVP kararı, bkz. `docs/DECISIONS.md`). Çoklu kullanıcı/ağ
  güvenliği tehdit modeli kapsamının parçası değildir.
- Üçüncü taraf bağımlılıkların (ONNX Runtime, ImageSharp) kendi güvenlik
  duyuruları için bkz. `THIRD_PARTY_NOTICES.md` ve `docs/RELEASE_PROCESS.md`
  (dependency vulnerability taraması önerisi).
