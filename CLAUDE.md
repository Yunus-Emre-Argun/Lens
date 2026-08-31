# Çalışma Kuralları — Lens Projesi

Bu dosya, bu proje üzerinde çalışırken Claude Code'un uyması gereken kuralları tanımlar.
Kapsam: sadece bu klasör (`Lens`). Bu klasörün dışına müdahale edilmez.

## Temel İlkeler

1. **Önce plan, sonra kod.**
   Herhangi bir uygulama kodu, script veya PoC yazmadan önce yaklaşım kısaca özetlenir
   ve gerekirse kullanıcıdan/Tech Lead'den onay istenir. Plan onaylanmadan implementasyona
   geçilmez.

2. **Kapsamı kendiliğinden genişletme.**
   Sadece istenen iş yapılır. "Madem buradayım, şunu da ekleyeyim" yaklaşımı yok.
   Login, raporlama, text search, çoklu kullanıcı gibi ileri faz özellikleri
   açıkça istenmeden eklenmez.

3. **Mimari değişiklikleri onaysız yapma.**
   Model seçimi (CLIP/SigLIP/başka), veritabanı/vector store seçimi, depolama biçimi,
   servis mimarisi gibi kararlar kullanıcı veya Tech Lead/CTO onayı olmadan
   uygulamaya alınmaz. Bu tür kararlar `docs/DECISIONS.md` içinde "Confirmed"
   olarak işaretlenmeden nihai kabul edilmez.

4. **Bilinmeyen gereksinimleri tahmin etme.**
   Belirsiz veya verilmemiş bir gereksinim varsa, varsayım yapılmaz; açıkça
   "unknown / to be clarified" olarak işaretlenir ve kullanıcıya sorulur.

5. **Küçük ve geri alınabilir değişiklikler yap.**
   Değişiklikler mümkün olduğunca küçük, incelenebilir ve gerektiğinde geri
   alınabilir adımlar halinde yapılır. Büyük, tek seferlik toplu değişikliklerden
   kaçınılır.

6. **Dependency eklemeden önce gerekçelendir.**
   Yeni bir paket/kütüphane/model eklenmeden önce neden gerekli olduğu, alternatifleri
   ve maliyeti (kurulum boyutu, lisans, bakım yükü) kısaca açıklanır ve onay istenir.

7. **Secrets/credentials commit etme.**
   API anahtarları, veritabanı bağlantı bilgileri, şifreler vb. hiçbir şekilde
   repoya commit edilmez. Böyle bir bilgiye ihtiyaç olursa `.env` benzeri,
   `.gitignore` ile hariç tutulan bir dosya kullanılması önerilir.

8. **Her faz sonunda raporla.**
   Bir çalışma turu/faz tamamlandığında: ne yapıldığı, hangi dosyaların
   değiştiği/oluşturulduğu, hangi testlerin (varsa) çalıştırıldığı ve sonraki
   adım için nelerin onay beklediği kısaca raporlanır.

## Bu Proje Özelinde

- Bu bir PoC/MVP hazırlığıdır; production mimarisi ile karıştırılmaz.
- Mikroservis, Docker/Kubernetes, cloud altyapı, vector database gibi ağır
  çözümler gerçek bir teknik gerekçe kanıtlanmadan önerilmez/kurulmaz.
- Teknoloji seçimleri (CLIP vs SigLIP, storage biçimi vb.) moda veya
  "daha yeni" olmasına göre değil, projenin gerçek ihtiyacına ve ölçülmüş
  sonuçlara göre yapılır.
- Detaylı proje bağlamı için: `docs/PROJECT_CONTEXT.md`
- Alınan/alınmamış kararlar için: `docs/DECISIONS.md`
- Mimari toplantı hazırlığı için: `docs/ARCHITECTURE_PROPOSAL.md`
