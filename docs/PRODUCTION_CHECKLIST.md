# Production Rollout Checklist

Bu doküman, `docs/DECISIONS.md` "Not Yet Decided", `docs/ROADMAP.md`'nin
"bilerek ertelenen" maddeleri ve dağınık halde duran manuel-doğrulama
kalemlerini **tek bir aksiyon listesinde** toplar. FAZ 4G (production
rollout) öncesi bu listenin gözden geçirilmesi önerilir.

## Codex Bulguları — Açık/Bekleyen Riskler (Faz 1, 2026-09-03, henüz düzeltilmedi)

Codex code review'ında tespit edilen ama bu turda **bilinçli olarak
düzeltilmeyen** (yalnızca kayıt altına alınan) altı madde. Hiçbiri release'i
bloklayan bir "crash/veri kaybı" riski değildir, ama production onayından
önce gözden geçirilmelidir.

- [ ] **Strict threshold epsilon.** `SimilaritySearch.SearchWithThreshold`
  içindeki `ScoreEpsilon = 1e-4f` toleransı, float32 birikim hatasını
  telafi etmek için eklendi ama **product owner tarafından onaylanmış bir
  değer değil** — kullanıcı `%100` (kesin eşleşme) girdiğinde, aslında
  `%99.99` olan bir sonuç da "dahil" sayılabilir. "Strict" (sıfır tolerans)
  bir mod gerekip gerekmediği ve epsilon'un kesin değeri henüz onaylı
  değil. Kod: `src/Lens.Core/Search/SimilaritySearch.cs`.
- [ ] **Lock I/O hata ayrımı güvenilir değil.** `IndexLock.TryAcquire`,
  "kilit başka bir yazar tarafından tutuluyor" (`IOException`, sharing
  violation) ile "başka bir I/O sorunu" (`UnauthorizedAccessException`)
  arasında yalnızca **exception tipine bakarak** ayrım yapıyor. Gerçek bir
  UNC/SMB paylaşımında geçici ağ kesintisi, stale handle veya antivirüs
  kilidi gibi durumlar da `IOException` fırlatabilir ve yanlışlıkla
  "başka bir kullanıcı güncelliyor" olarak yorumlanabilir (kullanıcıya
  yanıltıcı mesaj). Gerçek UNC/SMB testi (bkz. "Manuel Doğrulama" bölümü)
  bu ayrımın pratikte ne kadar güvenilir olduğunu netleştirmeli. Kod:
  `src/Lens.Core/Indexing/IndexLock.cs`.
- [ ] **Legacy LocalAppData cache yan etkisi.** `AppPaths.CacheIndexFilePath`
  (eski, artık normal operasyonda kullanılmayan LocalAppData index yolu)
  hâlâ kodda duruyor ve **çağrıldığında side-effect'lidir**: içeride
  `EnsureCacheDirectoryFor` çalışır, bu da `%LocalAppData%\Lens\cache\<hash>\`
  klasörünü ve bir `meta.json` dosyasını **oluşturur** — dönen path artık
  hiçbir yerde okunup yazılmasa bile. Bu metodu (ör. teşhis/debug amaçlı)
  çağıran gelecekteki bir kod, farkında olmadan LocalAppData'da gereksiz
  klasör biriktirebilir. Düzeltme opsiyonları: metodu tamamen kaldırmak
  (yalnızca product owner onayıyla — mimari değişiklik) veya side-effect'i
  ayrı bir metoda taşımak. Kod: `src/Lens.Core/Config/AppPaths.cs`.
- [ ] **Panoramik/aşırı en-boy oranlı büyük görsel test edilmedi.**
  `ImagePreprocessor.LoadForPreprocessing`'in ekonomik decode yolu
  (`DecoderOptions.TargetSize = 448x448`) yalnızca kare/kareye-yakın büyük
  bir sentetik görselle (8000×7500) test edildi (bkz. AiProof
  `hardeningtest` Grup C). Çok geniş/dar en-boy oranlı (ör. 20000×1500
  panoramik) büyük bir görselde decoder-seviyesi downsampling'in davranışı
  ve sonraki shortest-edge/crop adımlarının bunu doğru ele alıp almadığı
  **doğrulanmadı**.
- [ ] **Türkçe locale'de "%100 tam eşleşme" vurgusu çalışmayabilir.**
  `MainWindow.SearchButton_Click` içinde `IsPerfectMatch`,
  `scoreText.EndsWith("100.0%", StringComparison.Ordinal)` ile hesaplanıyor
  — burada `scoreText`, `{r.Score:P1}` (kullanıcının/işletim sisteminin
  **o anki culture'ı** ile) formatlanıyor. Türkçe (`tr-TR`) culture'da
  yüzde biçimlendirmesi ondalık ayırıcı olarak virgül kullanır ve sembol
  yerleşimi farklı olabilir (ör. `"%100,0"`), bu yüzden İngilizce'ye özel
  sabit `"100.0%"` dizesiyle eşleşmeyebilir — sonuç olarak **Türkçe
  Windows'ta %100 eşleşmenin yeşil vurgusu hiç tetiklenmeyebilir**. Bu,
  Türkçe konuşan bir fabrika kullanıcı kitlesi için gerçek bir risktir.
  Düzeltme, ham `double`/`float` skor değerini (culture'dan bağımsız)
  epsilon ile `1.0`'a karşılaştırmak olurdu — bu turda yapılmadı. Kod:
  `src/Lens.Desktop/MainWindow.xaml.cs` (`SearchButton_Click`).
- [ ] **Publish çıktısındaki `.pdb` dosyaları yerel kullanıcı yolunu
  içeriyor.** `publish/Lens.Desktop-win-x64-manager-shared-index/` içindeki
  `Lens.Core.pdb`/`Lens.Desktop.pdb` debug sembol dosyaları, derleme
  makinesinin **mutlak yerel yolunu** (ör. geliştiricinin kullanıcı adını
  içeren `C:\Users\...\Lens\...`) gömülü olarak taşıyabilir. Kaynak kodu,
  gizli bilgi veya fabrika verisi İÇERMİYOR ama gereksiz bir bilgi sızıntısı
  riskidir. Düzeltme: release publish için `<DebugType>none</DebugType>`
  (veya `embedded`) ayarlamak ya da publish sonrası `.pdb` dosyalarını
  elle silmek — bu turda yapılmadı. **Not:** `publish/` klasörünün kendisi
  Git'e commit EDİLMEDİ (`.gitignore` ile hariç tutuluyor), bu yüzden bu
  risk yalnızca dağıtılan çıktıyı elle paylaşan biri için geçerlidir.

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
- [x] Aynı anda birden fazla Lens örneğine karşı dosya kilidi/mutex —
  **Faz 1'de implement edildi** (`Lens.Core.Indexing.IndexLock`, tek-yazarlı
  exclusive dosya kilidi, distributed lock/queue değil). Gerçek UNC share
  üzerinde manuel acceptance hâlâ gerekiyor (aşağıdaki "Manuel Doğrulama"
  bölümüne bakın).
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
- [ ] Sonuç kartı seçim rengi + query/sonuç karşılaştırma akışı (artık
  Top-10 sabit değil — eşik + en fazla 15, bkz. `docs/DECISIONS.md` #60).
- [ ] `publish/` klasörünü hedef makineye kopyalayıp çalıştırma,
  `appsettings.json`'a gerçek UNC path girme.
- [ ] **[Faz 1]** Gerçek UNC/SMB paylaşımı üzerinde: iki farklı istasyondan
  eşzamanlı "İndeksi Güncelle" (ikinci istasyon lock mesajını doğru
  görmeli, hiçbir overwrite başlatmamalı); atomic save'in (`File.Replace`
  → `Move(overwrite:true)` fallback) o spesifik dosya sunucusunda/SMB
  sürümünde beklenen şekilde çalıştığının doğrulanması; `.lens` klasörü
  için IT tarafından uygulanan yazma izinlerinin yeterliliği (bkz.
  `docs/DEPLOYMENT.md` §5b).

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
