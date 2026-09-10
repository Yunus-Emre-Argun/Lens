# Deployment / Release Runbook

Bu doküman, Lens'in **tekrarlanabilir** genel dağıtım/güncelleme sürecini
anlatır. İlk pilot/demo dağıtımına özel adımlar (yönetici bilgisayarına ilk
kurulum, Smart App Control gözlemi) için `docs/DEMO_DEPLOYMENT_GUIDE.md`'ye
bakın — iki doküman birbirini tamamlar, bu doküman onu geçersiz kılmaz.

## 1. Publish Üretimi

```
dotnet publish src/Lens.Desktop/Lens.Desktop.csproj -c Release -r win-x64 --self-contained true -o publish/<hedef-klasör>
```

> **[PİLOT dalı — `feature/dinov2-base-pilot`]** Bu dalda paketlenen model
> CLIP değil **DINOv2-Base**'dir (`models/dinov2-base.onnx`, ~330 MB); CLIP
> modeli pakete **dahil edilmez**. Kullanıcının görsel kabul testi için
> üretilen paket:
>
> ```
> dotnet publish src/Lens.Desktop/Lens.Desktop.csproj -c Release -r win-x64 \
>   --self-contained true -p:DebugType=none -p:DebugSymbols=false \
>   -o publish/Lens.Desktop-win-x64-dinov2-base
> ```
>
> Paket ~506 MB'dır.
>
> Kullanıcının kurarak deneyebilmesi için **ayrı** bir ClickOnce paketi de
> üretilmiştir: `publish/ClickOnce-dinov2-base/` (profil:
> `ClickOnceDinoV2.pubxml`). **Mevcut CLIP ClickOnce profili ve
> `publish/ClickOnce/` klasörü DEĞİŞTİRİLMEMİŞTİR.** Yan yana kurulum yoktur —
> pilot, kurulu Lens'in üzerine kurulur; geri dönüş `publish\ClickOnce\setup.exe`.
> Ayrıntı ve doğrulama listesi: `docs/CLICKONCE.md` §12b.
>
> DINOv2 sürümü kullanıcı tarafından kabul edilmeden gerçek dağıtım
> adresine/sistemine taşınmamalıdır.

> **[Çok modelli pilot — `feature/multi-model-search`]** Bu dal HER İKİ modeli
> de paketler (kullanıcı arayüzden seçtiği için):
>
> ```
> dotnet publish src/Lens.Desktop/Lens.Desktop.csproj -c Release -r win-x64 \
>   --self-contained true -p:DebugType=none -p:DebugSymbols=false \
>   -o publish/Lens.Desktop-win-x64-multi-model
> ```
>
> ClickOnce için `ClickOnceMultiModel.pubxml` → `publish/ClickOnce-multi-model/`;
> `AssemblyName` = `Lens.Desktop.MultiModel` ile **bağımsız kurulum kimliği**
> üretir, mevcut pilotların üzerine kurulmaz. Veri klasörü
> `%LocalAppData%\Lens.MultiModel\`. Mevcut ClickOnce paketleri
> DEĞİŞTİRİLMEMİŞTİR (478'er dosya bayt bayt aynı). Ayrıntı:
> `docs/MULTI_MODEL_SEARCH.md`.

- **Hedef mimari: x64.** `win-x86`/`win-arm64` için ayrıca test edilmemiştir.
- Self-contained olduğu için hedef makinede .NET runtime kurulu olması
  gerekmez; WPF runtime ve ONNX Runtime native binary'leri publish çıktısına
  dahildir.
- Model dosyası (`models/clip-vision-b16-openai.onnx`) yerel makinenizde
  mevcutsa (bkz. `docs/DEVELOPMENT_SETUP.md`), publish sırasında otomatik
  olarak çıktıya kopyalanır (`CopyToOutputDirectory`, `Lens.Desktop.csproj`).

## 2. Ön-Kontrol Listesi (publish sonrası, dağıtımdan önce)

Publish klasöründe şunların bulunduğunu doğrulayın:

- [ ] `Lens.Desktop.exe`
- [ ] `Lens.Core.dll`, `Lens.Desktop.dll`
- [ ] `Microsoft.ML.OnnxRuntime.dll` + native `onnxruntime.dll` / `onnxruntime_providers_shared.dll`
- [ ] `models\clip-vision-b16-openai.onnx`
- [ ] `appsettings.json` (gerçek fabrika UNC yolu **içermediğini** doğrulayın —
  `AdminDefaultProductDirectory` boş şablon veya bilinçli olarak doldurulmuş
  onaylı bir yol olmalı)
- [ ] Kültür/lokalizasyon klasörleri (`cs\`, `de\`, `tr\`, vb. — .NET runtime'ın
  parçası, normaldir)

Eksik bir dosya varsa uygulama açılmayabilir veya model yüklenemez hatası
verir — publish klasörünü **kısmi kopyalamayın**, tamamını taşıyın.

## 3. Hedef Makineye Taşıma

**Yalnızca `.exe` dosyasını kopyalamak yeterli değildir.** Publish klasörünün
**tamamı** hedef Windows makinesine kopyalanmalıdır (bkz. `docs/DEMO_DEPLOYMENT_GUIDE.md` §1).

## 4. `appsettings.json` — Güncelleme Sırasında Korunma

Bir güncelleme dağıtırken (yeni publish, mevcut kurulumun üzerine):

1. Hedef makinedeki **mevcut** `appsettings.json`'ı (varsa özelleştirilmiş
   `AdminDefaultProductDirectory` değeriyle) bir kenara alın.
2. Yeni publish klasörünü kopyalayın.
3. Kenara aldığınız `appsettings.json`'ı yeni klasördeki şablonun üzerine
   **geri yazın** (elle birleştirme — otomatik bir migration mekanizması
   yoktur).

Kullanıcının kendi seçtiği klasör override'ı `%LocalAppData%\Lens\config\`
içindedir, publish klasörünün dışındadır — güncelleme bunu etkilemez (bkz.
`docs/ARCHITECTURE.md`).

## 5. Shared Index ve Arama Sonucu Davranışı (güncelleme sonrası ne beklenir)

**[Faz 1, 2026-09-03] Index artık `%LocalAppData%` DEĞİL, paylaşılan ürün
dizininin kendi içinde** (`<ProductDirectory>/.lens/index.json`) — bkz.
`docs/ARCHITECTURE.md`, `docs/DECISIONS.md` #61.

- Kullanıcının mevcut shared index'i publish klasörünün dışındadır (ürün
  dizininde) — bir uygulama güncellemesi bunu **silmez**. Model veya
  preprocessing değişmediyse index aynen kullanılmaya devam eder.
- Model dosyası değiştiyse (farklı `.onnx`), `docs/MODEL_CARD.md`'deki
  "Model/Preprocessing Değiştiğinde Cache" talimatına göre `.lens/index.json`
  dosyasının elle silinmesi gerekir — aksi halde eski embedding'ler yeni
  modelle karışabilir (otomatik versiyon kontrolü yoktur). **Dikkat:** bu
  dosya artık paylaşılan bir klasördedir — silme işlemi TÜM kullanıcıları
  etkiler, tek bir istasyonun local cache'i değildir.
- UI artık sabit **Top-10 değil**; kullanıcının girdiği minimum benzerlik
  eşiğini geçen sonuçlardan **en fazla 15**'i gösterir (Faz 1'den beri; eski
  "Top-5"/"Top-10" referansları yalnızca tarihsel benchmark ölçümlerinde
  kalmıştır). Bkz. `docs/DECISIONS.md` #60.
- `.lens/index.json` yanında artık `.lens/index.lock` (tek-yazarlı exclusive
  kilit dosyası) de bulunur — bu dosyanın **varlığı** aktif bir kilit
  anlamına gelmez, yalnızca process çalışırken tuttuğu OS handle önemlidir;
  silinmesi/elle müdahale gerekmez.
- Eski `%LocalAppData%\Lens\cache\` altındaki local index dosyaları normal
  operasyonda artık kullanılmıyor (otomatik silinmiyor da) — güncelleme bu
  eski dosyalardan etkilenmez.

## 5b. `.lens` Klasörü Yazma İzinleri (Faz 1)

Shared index yazan her kullanıcı, paylaşılan ürün dizininde **`.lens` alt
klasörü için** en az şu haklara ihtiyaç duyar:

- Klasör/dosya oluşturma (ilk kullanımda `.lens` henüz yoksa)
- Yazma
- Değiştirme/yeniden adlandırma (atomic save: temp dosya → replace/move)
- Temp dosya temizleme/silme

Önerilen IT düzeni: ürün görselleri genel **read**, `.lens` teknik klasörü
**kontrollü write** (yalnızca Lens kullanıcıları/servis hesabı). İzin
yetersizse index güncellemesi başarısız olur ama uygulama çökmez — kullanıcı
dostu bir hata gösterilir ve önceki (varsa) sağlam index korunur; gerçek bir
paylaşılan (SMB) klasör üzerinde bu senaryonun **manuel kabul testi**
gerekir (bkz. final rapor / `docs/PRODUCTION_CHECKLIST.md`).

## 6. Code Signing / Application Control

Şu an publish çıktısı **imzalanmamıştır** (Authenticode code signing yok).
Geliştirme sırasında Windows Smart App Control'un imzasız binary'leri
engellediği gözlemlenmiştir (bkz. `docs/DEMO_DEPLOYMENT_GUIDE.md` §7). Fabrika
workstation'larında bu politika aktifse:

- Engel **tespit edilip raporlanmalı**, bypass edilmemelidir (bkz. `CLAUDE.md`).
- Kalıcı çözüm için code signing sertifikası edinme ihtiyacı
  `docs/RELEASE_PROCESS.md`'de bir öneri olarak not edilmiştir — henüz
  uygulanmamıştır.

## 7. Rollback

Otomatik bir rollback mekanizması yoktur. Önerilen yaklaşım:

1. Her release için publish klasörünü **sürüm numarası/tarih etiketiyle**
   ayrı bir klasörde saklayın (örn. `publish/Lens.Desktop-win-x64-2026-09-02/`).
2. Bir sorun çıkarsa, hedef makinedeki klasörü önceki sürümün publish
   klasörüyle **değiştirin** — `appsettings.json`'ı (madde 4'teki gibi) koruyun.
3. Shared index (`<ProductDirectory>/.lens/index.json`) ve
   `%LocalAppData%\Lens\config\` klasörü publish'ten bağımsız olduğu için
   rollback sırasında etkilenmez; ancak yeni sürümde model/preprocessing
   değişikliği yapıldıysa ve index o sürümle güncellendiyse, eski sürüme
   dönüldüğünde tutarlılık garanti değildir — şüpheli durumda
   `.lens/index.json`'ı silin (paylaşılan dosya olduğu için bu TÜM
   kullanıcıları etkiler, tek istasyonu değil).

## 8. Kaynak Commit ile Dağıtılan Exe'nin Eşleştirilmesi

Şu an publish çıktısında hangi git commit'inden üretildiğini gösteren
otomatik bir versiyon/commit-hash dosyası **yoktur**. Önerilen (henüz
uygulanmamış) süreç — detay ve gerekçe `docs/RELEASE_PROCESS.md`'de:

- Her publish öncesi `git rev-parse HEAD` çıktısını not edin.
- Publish klasörüne elle bir `VERSION.txt` (commit hash + tarih + model
  SHA-256) eklemeyi değerlendirin — bu bir süreç önerisidir, bu dokümanla
  birlikte otomatik olarak eklenmemiştir.

## 9. Sürüm Güncelleme (Yönetici Süreci)

**[2026-09-07, biçim 2026-09-08'de iki haneli yıla geçirildi]** Ana
pencerenin sol alt köşesinde (`Sürüm: 08.09.26 — v1.0` biçiminde) ve
Hakkında ekranında gösterilen sürüm metninin **tek kaynağı**
derlenen `Lens.Desktop.exe` içindeki Assembly metadata'sıdır — XAML/C# içinde
sabit bir metin olarak YAZILMAZ (bkz. `Lens.Desktop.MainWindow.xaml`
`FooterVersionText`, `MainWindow.AboutMenuItem_Click`, ortak okuyucu:
`Lens.Desktop.AppVersionInfo.GetDisplayVersion()`). Değer, `src/Lens.Desktop/
Lens.Desktop.csproj` içindeki üç MSBuild özelliğinden derleme sırasında SDK
tarafından otomatik üretilir:

| MSBuild özelliği | Biçim | Örnek | Kullanım yeri |
|---|---|---|---|
| `AssemblyVersion` | yalnızca `major.minor.build.revision` (sayısal) | `1.0.0.0` | .NET assembly binding/CLR metadata |
| `FileVersion` | yalnızca `major.minor.build.revision` (sayısal) | `1.0.0.0` | Windows Gezgini "Dosya sürümü" |
| `InformationalVersion` | serbest metin | `08.09.26 — v1.0` | Alt bilgi satırı + Hakkında ekranı (**kullanıcının gördüğü değer budur**) |

`IncludeSourceRevisionInInformationalVersion` bilinçli olarak `false` bırakıldı
— aksi halde (bu proje ileride bir SourceLink paketi kullanmaya başlarsa)
`InformationalVersion`'ın sonuna otomatik olarak `+<git-commit-hash>` eklenip
gösterilen metni değiştirebilirdi.

### Yeni sürüm çıkarırken izlenecek adımlar

1. Visual Studio'da `Lens.Desktop` projesine (Solution Explorer) sağ tıklayıp
   **Properties**'i açın.
2. **Sayısal alanlar (`AssemblyVersion`/`FileVersion`):** **Application**
   sekmesindeki **"Assembly Information..."** düğmesiyle açılan diyalogda
   **"Assembly version"** / **"File version"** kutularını `1.0.0.0` benzeri
   geçerli bir sayısal değere güncelleyin. Bu diyalog/alan adları uzun
   süredir kararlı bir konumdur, ancak **VS sürümüne göre "Assembly
   Information..." düğmesinin tam yeri (Application sekmesi vs. yeni
   birleşik Properties sayfası) değişebilir** — bulamazsanız adım 3'teki
   yöntemle doğrudan `.csproj`'u düzenlemek her VS sürümünde garanti çalışır.
3. **Kullanıcıya gösterilen serbest metin (`InformationalVersion`):** Yeni
   SDK-style proje Properties arayüzlerinde bu alan için **her VS sürümünde
   garanti bir metin kutusu bulunmayabilir** (bu, `Assembly Version`/`File
   Version`'ın aksine, VS'nin klasik "Assembly Information" diyaloğunda
   HİÇBİR ZAMAN ayrı bir alan olarak yer almamıştır). En güvenilir ve VS
   sürümünden bağımsız yol: Solution Explorer'da `Lens.Desktop` projesine
   sağ tıklayıp **"Edit Project File"** (veya Properties sayfasının kendi
   XML/gelişmiş düzenleme seçeneği) ile `Lens.Desktop.csproj`'u açıp
   `<InformationalVersion>08.09.26 — v1.0</InformationalVersion>` satırını
   doğrudan değiştirmektir (bkz. dosyadaki ilgili `PropertyGroup` yorumu).
   **Zorunlu biçim (2026-09-08'den itibaren): `GG.AA.YY — vX.Y`** (Türkçe
   tarih, **yıl İKİ haneli**, kısa sürüm etiketi) — ör. `08.09.26 — v1.0`.
   Yıl dört haneli (`GG.AA.YYYY`) yazılmamalıdır; tarih derleme sırasında
   otomatik güncellenmez, bu alanı her sürümde **elle** bugünün tarihine
   güncellemek yöneticinin sorumluluğundadır.
4. Gerekirse (büyük bir sürüm atlaması, dağıtım takibi için) adım 2'deki
   sayısal `AssemblyVersion`/`FileVersion` değerlerini de artırın — bu iki
   alanın kullanıcıya gösterilen metinle (`InformationalVersion`) BİREBİR
   aynı olması ZORUNLU DEĞİLDİR, bağımsız izlenebilirler.
5. Projeyi yeniden derleyin (`dotnet build Lens.sln -c Release`, 0 warning/0
   error beklenir) ve gerekiyorsa yeniden publish alın (bkz. §1).
6. Doğrulayın: ana pencerenin sol alt köşesi, Hakkında ekranı (⋮ → Hakkında)
   ve EXE'nin Windows Gezgini → Özellikler → Detaylar sekmesindeki "Dosya
   sürümü"/"Ürün sürümü" alanları güncel/tutarlı değerleri gösteriyor mu.

## İlgili Dokümanlar

- İlk pilot/demo'ya özel adımlar: `docs/DEMO_DEPLOYMENT_GUIDE.md`
- Model dosyası ve hash doğrulama: `docs/MODEL_CARD.md`
- GitHub/release süreci önerisi (CI, tag, signing): `docs/RELEASE_PROCESS.md`
- Rollout öncesi açık maddeler: `docs/PRODUCTION_CHECKLIST.md`
- **[2026-09-08] ClickOnce kurulum/otomatik güncelleme (yeni, ek dağıtım
  yolu — bu dokümandaki taşınabilir publish akışının yerine geçmez):**
  `docs/CLICKONCE.md`
