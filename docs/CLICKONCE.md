# ClickOnce Kurulum ve Otomatik Güncelleme

Bu doküman, Lens için eklenen **ClickOnce** dağıtım yolunu anlatır:
`setup.exe` ile kullanıcı bazlı kurulum, Başlat menüsü kısayolu, masaüstü
kısayolu, çevrimdışı çalışma ve sonraki sürümlerde otomatik güncelleme
kontrolü.

**Bu doküman `docs/DEPLOYMENT.md`'nin (mevcut taşınabilir self-contained
publish akışı) yerine geçmez — onu tamamlar.** İki dağıtım yolu paralel var
olabilir; ClickOnce, taşınabilir publish'i **bozmaz veya değiştirmez**
(bkz. §"Taşınabilir Paket Yedek Seçenek Olarak Kalıyor").

## 0. Üretim Öncesi Eksik Bilgiler (özet)

ClickOnce paketi **yerel olarak hazır ve doğrulanmıştır**, ama aşağıdaki üç
bilgi netleşmeden **üretim dağıtımı tamamlanmış sayılmaz**:

| # | Eksik bilgi | Şu anki durum | Nerede doldurulur |
|---|---|---|---|
| 1 | Gerçek yayıncı/şirket adı | Açık placeholder: `[PLACEHOLDER] Yayıncı Adı Belirlenmedi` | `Properties/PublishProfiles/ClickOnce.pubxml` → `PublisherName` |
| 2 | Gerçek UNC/HTTPS güncelleme adresi | Boş (yerel/offline staging publish) | `Properties/PublishProfiles/ClickOnce.pubxml` → `InstallUrl` **ve** `UpdateUrl` |
| 3 | Şirket kod imzalama sertifikası | Yok — `SignManifests=false`, manifestler imzasız | `Properties/PublishProfiles/ClickOnce.pubxml` → `SignManifests`, `ManifestCertificateThumbprint`/`ManifestKeyFile` (bkz. §"İmzalama") |

Bu üç madde `docs/DECISIONS.md` "Not Yet Decided" bölümüne de eklenmiştir.

> **Not:** `feature/dinov2-base-pilot` dalında, bu profile DOKUNULMADAN,
> ayrı bir deneme paketi (`ClickOnceDinoV2.pubxml` → `publish/ClickOnce-dinov2-base/`)
> üretilmiştir — bkz. §12b. Yukarıdaki üç eksik bilgi o paket için de
> aynen geçerlidir.

## 1. Gerekli Araçlar (önemli — `dotnet publish` YETERSİZ)

`dotnet publish`/`dotnet msbuild` (.NET Core MSBuild) ClickOnce manifestlerini
üreten `UpdateManifest` görevini **desteklemez** — denendiğinde
`MSB4803: The task "UpdateManifest" is not supported on the .NET Core version
of MSBuild` hatası verir (bu turda yerinde doğrulandı). ClickOnce publish'i
**tam .NET Framework MSBuild** ile çalıştırılmalıdır:

- Bu makinede: Visual Studio Community kurulu
  (`C:\Program Files\Microsoft Visual Studio\18\Community`), onun
  `MSBuild\Current\Bin\MSBuild.exe`'si kullanıldı.
- Visual Studio yoksa: "Build Tools for Visual Studio" (ücretsiz, yalnızca
  MSBuild + gerekli iş yükleri) kurulabilir — ".NET desktop build tools" iş
  yükü ClickOnce publish görevlerini içerir.
- Klasik `mage.exe` de bu makinede mevcuttur
  (`C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8
  Tools\mage.exe`) — yalnızca manuel/ileri seviye manifest ihtiyacında.

## 2. İlk Sürümü Oluşturma (Publish)

```
"%ProgramFiles%\Microsoft Visual Studio\<sürüm>\<edition>\MSBuild\Current\Bin\MSBuild.exe" ^
  src\Lens.Desktop\Lens.Desktop.csproj /t:Restore,Publish ^
  /p:PublishProfile=ClickOnce /p:Configuration=Release /p:DebugType=none
```

(Bash/Git Bash'te `//t:`, `//p:` şeklinde çift eğik çizgi gerekir — MSYS yol
dönüştürmesini engellemek için.)

`/p:DebugType=none` **komut satırında ayrıca verilmelidir** — bkz.
§"Neden `/p:DebugType=none` Ayrıca Gerekli" aşağıda.

**⚠️ Artımlı (incremental) build tuzağı — yerinde tespit edildi:** Yalnızca
kaynak koda küçük bir değişiklik yapıp (ör. XAML metni) `bin`/`obj`'u
temizlemeden doğrudan yukarıdaki komutu tekrar çalıştırmak, MSBuild'in
`Lens.Core` için **eski, önbelleklenmiş bir `obj\Release\net8.0\` çıktısını**
yeniden kullanmasına ve `Lens.Core.dll` içine **yine** derleme makinesinin
yerel yolunu (`C:\Users\...\Lens.Core.pdb`) gömmesine yol açabiliyor —
`/p:DebugType=none` verilmiş olsa bile. Her ClickOnce publish öncesi
`Lens.Desktop`/`Lens.Core`'un `bin`/`obj` klasörlerini silip **temiz**
başlamak güvenlidir:

```
rm -rf src/Lens.Desktop/bin src/Lens.Desktop/obj src/Lens.Core/bin src/Lens.Core/obj
```

Çıktı, `Properties/PublishProfiles/ClickOnce.pubxml`'deki `PublishDir`'e göre
repo kökünde `publish\ClickOnce\` altına üretilir (bu klasör `.gitignore`
ile hariç tutulur, tıpkı mevcut `publish/` gibi):

```
publish\ClickOnce\
  setup.exe                          <- kullanıcıya dağıtılan kurulum başlatıcısı
  Lens.Desktop.application           <- deployment manifest
  Launcher.exe
  Application Files\Lens.Desktop_1_0_0_0\
    Lens.Desktop.exe.deploy, Lens.Core.dll.deploy, ...
    Lens.Desktop.dll.manifest        <- application manifest
    models\clip-vision-b16-openai.onnx.deploy
    appsettings.json.deploy
    onnxruntime.dll.deploy, onnxruntime_providers_shared.dll.deploy, ...
```

### Neden `/p:DebugType=none` Ayrıca Gerekli

`ClickOnce.pubxml` içindeki `<DebugType>none</DebugType>`, yalnızca
`Lens.Desktop` projesinin **kendi** derlemesine uygulanır. `Lens.Core` proje
referansı ayrı bir MSBuild çağrısıyla derlenir ve pubxml'deki bu değer ona
otomatik taşınmaz. Yerinde test edilerek doğrulandı: `/p:DebugType=none`
komut satırından **ayrıca** verilmezse, `Lens.Core.dll` içine — hiç `.pdb`
dosyası pakete dahil edilmese bile — derleme makinesinin **yerel kullanıcı
yolunu** (`C:\Users\<kullanıcı>\...\Lens.Core.pdb`) içeren gömülü bir PDB
referans dizesi kalıyordu. Komut satırında global property olarak
verildiğinde tüm proje grafiğine (Lens.Desktop + Lens.Core) uygulanır ve bu
sızıntı ortadan kalkar (`grep -rl "C:\\Users" publish\ClickOnce` boş döner).

### `None` Değil `Content`: Model ve `appsettings.json` Neden Pakete Girmiyordu

İlk denemede model ve `appsettings.json`, `CopyToOutputDirectory` ile
`bin\`/`app.publish\` çıktısına doğru kopyalanmasına rağmen ClickOnce
`Application Files` listesine **hiç girmiyordu**. Kök neden: Visual Studio'nun
klasik ClickOnce hedefleri (`Microsoft.Common.CurrentVersion.targets`,
`_ClickOnceContentItems`/`_ClickOnceNoneItems`), **`Content`** öğelerini
manifest'e koşulsuz dahil ederken, **`None`** öğelerini yalnızca dosya uzantısı
`.exe`/`.dll` ise veya özel bir `PublishFile` eşleşmesi varsa dahil ediyor.
Modelimiz ve `appsettings.json` `None` olarak tanımlıydı → dahil edilmiyordu.
Çözüm: `Lens.Desktop.csproj`'da bu iki öğe `None`'dan `Content`'e taşındı
(`appsettings.json` için önce `<None Remove="appsettings.json" />` ile SDK'nin
örtük glob'undan çıkarılıp sonra `Content Include` ile yeniden eklendi —
çakışma olmasın diye). Bu değişiklik normal `dotnet publish`/taşınabilir
publish davranışını **etkilemez** (None/Content, `CopyToOutputDirectory`
söz konusu olduğunda SDK publish'i için eştir) — yalnızca ClickOnce
uygunluğunu sağlar.

## 2b. Masaüstü Kısayolu

**[2026-09-08]** `ClickOnce.pubxml` içine `<CreateDesktopShortcut>true</CreateDesktopShortcut>`
eklendi (`Install`/`InstallFrom`/çevrimdışı kurulum ayarlarıyla aynı blokta).
Kurulum tamamlandığında:

- Başlat menüsü kaydı **korunur** (ClickOnce'ta zaten varsayılan davranış,
  bu ayardan bağımsız).
- Kullanıcının masaüstünde otomatik olarak **`Lens`** kısayolu oluşur.
- Görev çubuğuna otomatik sabitleme **yapılmaz** — ClickOnce'ta böyle bir
  ayar yok, istenmedi de.
- Kısayol, publish klasöründeki EXE'ye **doğrudan değil**, ClickOnce'ın
  yönettiği `Lens.Desktop.application` deployment manifestine işaret eder —
  bu yüzden sürüm güncellemelerinde (bkz. §4/§6) geçerliliğini korur, elle
  yeniden oluşturulması gerekmez.

Yerinde doğrulandı: publish sonrası üretilen `Lens.Desktop.application`
deployment manifestinde `co.v1:createDesktopShortcut="true"` özniteliği
mevcut (`<deployment install="true" mapFileExtensions="true"
co.v1:createDesktopShortcut="true">`). Gerçek kurulum penceresi açılıp
masaüstünde kısayolun fiilen oluştuğu bu turda **denenmedi** — bkz.
§12 "Kullanıcıdan Beklenen Canlı Test Adımları".

## 2c. Uygulama İkonu

**[2026-09-08]** Lens'in resmi uygulama ikonu eklendi — lime yeşili
yuvarlatılmış zemin üzerinde koyu lacivert büyüteç, kırmızı/mercan tonlarında
beş yapraklı çiçek ve sarı-altın merkez (yazı/harf yok). Bu tasarım,
kullanıcı tarafından onaylanmış, proje için özel üretilmiş özgün bir
tasarımdır (ilk sunulan geometrik/mavi-beyaz örgülü varyant yerine bu
çiçekli varyant final olarak seçildi — bkz. `docs/DECISIONS.md`).

**Dosyalar:**
- `src/Lens.Desktop/Assets/lens-app-icon-master.png` — temiz, gerçek RGBA
  alpha kanallı ana tasarım kaynağı (1254×1254). Kullanıcının onayladığı
  kaynak görsel, damalı sahte-şeffaflık içeriyordu (24bpp RGB, gerçek alpha
  yoktu) — bağlantılı-bileşen (connected component) flood-fill ile arka
  plan tespit edilip gerçek şeffaflığa çevrildi, kenar bandındaki gri
  karışımı "decontaminate" edilip (en yakın temiz ön-plan rengiyle
  değiştirilerek) hale/gri kenar oluşması önlendi. Bu dosya yalnızca
  **geliştirme zamanı kaynağıdır** — derleme/publish çıktısına dahil
  edilmez (`.csproj`'da herhangi bir Copy/Include öğesi yok).
- `src/Lens.Desktop/Assets/Lens.ico` — yukarıdaki ana PNG'den üretilen,
  **9 çözünürlüklü** (16, 20, 24, 32, 40, 48, 64, 128, 256 px) gerçek
  Windows ICO dosyası; her boyut ayrı, gerçekten var olan bir bitmap
  karesidir (yalnızca metadata etiketi değil — publish sonrası
  `Image.open(...).info['sizes']` ile teknik olarak doğrulandı). 48px ve
  altı boyutlarda hafif bir keskinleştirme (`UnsharpMask`) uygulanır —
  tasarımı değiştirmez, yalnızca küçük boyutlarda downsampling
  bulanıklığını kısmen telafi eder.

**Bağlantılar (tek fiziksel `Lens.ico` dosyasından, gereksiz kopya
oluşturulmadan):**
- `Lens.Desktop.csproj` → `<ApplicationIcon>Assets\Lens.ico</ApplicationIcon>`
  — derlenen `Lens.Desktop.exe`'nin Win32 kaynak ikonu (Gezgin/görev
  çubuğu/Alt-Tab), yerinde `System.Drawing.Icon.ExtractAssociatedIcon` ile
  doğrulandı.
- `Lens.Desktop.csproj` → `<Resource Include="Assets\Lens.ico" />` — WPF
  pack-URI (`/Assets/Lens.ico`) üzerinden pencere başlığı ikonu için.
- `MainWindow.xaml`, `AlertWindow.xaml`, `ImagePreviewWindow.xaml`,
  `ProblemFilesWindow.xaml`, `SettingsWindow.xaml` → hepsinde
  `Icon="/Assets/Lens.ico"` — **tüm** pencereler aynı ikonu gösterir,
  hiçbiri Windows'un varsayılan WPF simgesine dönmez.
- **ClickOnce masaüstü kısayolu (§2b) ve Başlat menüsü kaydı** aynı
  `Lens.Desktop.exe`'nin (dolayısıyla aynı `Lens.ico`'nun) simgesini
  kullanır — ayrı bir kısayol-ikonu ayarı YOKTUR, ikon EXE'den miras alınır.
  `setup.exe`/`Launcher.exe` için ayrı bir ikon **atanmadı** (görev
  talimatı gereği öncelik kurulan uygulama ve kısayollarıdır).

**⚠️ İkon her değiştiğinde ClickOnce paketi yeniden yayımlanmalıdır**
(bkz. §2 — temiz `bin`/`obj` + `/p:DebugType=none` ile republish) — aksi
halde `publish/ClickOnce/` içindeki eski paket önceki ikonu taşımaya devam
eder.

**Windows ikon önbelleği notu:** Zaten kurulu bir ClickOnce sürümü varsa,
Windows'un simge önbelleği yeni ikonu hemen göstermeyebilir — bkz. §12.

## 3. Paket İçeriği Doğrulaması (her yayından önce elle kontrol edin)

```
find "publish/ClickOnce/Application Files/Lens.Desktop_<versiyon>" -iname "*.onnx*" -o -iname "appsettings*"
grep -rl "C:\\Users" publish/ClickOnce            # bos donmeli
find publish/ClickOnce -iname "*.pdb*"            # bos donmeli
find publish/ClickOnce -iname "*.lens*" -o -iname "nevresim*" -o -iname "*.log"   # bos donmeli
```

Bu turda doğrulandı: model + `appsettings.json` + `setup.exe` + `.application`
+ `.manifest` mevcut; hiç `.pdb`, yerel kullanıcı yolu, ürün görseli, `.lens`
index/log dosyası **yok**.

## 4. Sürüm Artırma

**Tek kaynak:** `src/Lens.Desktop/Lens.Desktop.csproj` → `<FileVersion>`
(zaten `docs/DEPLOYMENT.md` §9'daki mevcut "Yeni sürüm çıkarırken izlenecek
adımlar" sürecinin bir parçası). `ClickOnce.pubxml`'deki
`<ApplicationVersion>$(FileVersion)</ApplicationVersion>` bu değeri otomatik
okur — **ayrı, elle tutulan bir ClickOnce sürüm sayısı yoktur.**

**Dikkat — iki farklı, birbirinden bağımsız "sürüm" kavramı vardır:**
`ApplicationVersion` (ClickOnce'ın güncelleme kontrolü için kullandığı,
4 parçalı sayısal `FileVersion` — ör. `1.0.0.0`) ile kullanıcıya sol alt
köşede/Hakkında ekranında gösterilen serbest metin `InformationalVersion`
(ör. `08.09.26 — v1.0`, bkz. `docs/DEPLOYMENT.md` §9) **birbirinden
bağımsızdır**. `InformationalVersion`'ı (görünen tarih/etiket) değiştirmek
ClickOnce'ın `ApplicationVersion`/güncelleme davranışını **etkilemez** —
ikisi ayrı MSBuild özellikleridir, birinin değişmesi diğerini tetiklemez.

Adımlar:

1. `docs/DEPLOYMENT.md` §9'daki adımlarla `AssemblyVersion`/`FileVersion`'ı
   artırın (ör. `1.0.0.0` → `1.0.0.1`).
2. `docs/DEPLOYMENT.md` §9 adım 3'teki gibi gerekirse `InformationalVersion`'ı
   (kullanıcıya gösterilen `gün.ay.yıl — vX.Y` metni) de güncelleyin — bu
   ClickOnce'tan bağımsızdır, birbirini etkilemez.
3. §2'deki publish komutunu tekrar çalıştırın.

**⚠️ Kritik — yerinde test edilerek doğrulandı:** Bu komut satırı MSBuild
akışı, **aynı veya daha düşük** bir `FileVersion` ile aynı `PublishDir`'e
tekrar publish yapıldığında **hata vermez, reddetmez** — sessizce üzerine
yazar (Visual Studio Publish sihirbazının IDE içi uyarısı burada yoktur).
Sürüm geriye düşmesine karşı **tek koruma, publish öncesi `FileVersion`'ın
mutlaka artırılmasıdır** — bu elle disiplin gerektirir, otomatik bir güvenlik
ağı yoktur. Zaten güncellenmiş istemciler (kurulu sürümü manifesttekinden
yüksekse) ClickOnce istemci tarafı kontrolü gereği yine de geriye doğru
"güncelleme" almaz — asıl risk, o an **yeni kurulum** yapan kullanıcıların
yanlışlıkla eski sürümü almasıdır. Yayından önce
`grep -o 'version="[^"]*"' publish/ClickOnce/Lens.Desktop.application` ile
beklenen sürümü teyit edin.

## 5. Gerçek Yayın/Güncelleme Adresini Tanımlama

`Properties/PublishProfiles/ClickOnce.pubxml` içinde iki alan **bilerek boş**
bırakıldı (gerçek adres bilinmediği için, uydurma/Google Drive bağlantısı
KULLANILMADI):

```xml
<InstallUrl></InstallUrl>
<UpdateUrl></UpdateUrl>
```

Gerçek adres netleştiğinde (şirket içi UNC klasör, ör. `\\sunucu\LensDeploy\`
**veya** şirketin HTTPS/intranet adresi), her ikisi de doldurulmalıdır:

```xml
<InstallUrl>\\sunucu\LensDeploy\</InstallUrl>
<UpdateUrl>\\sunucu\LensDeploy\</UpdateUrl>
```

Adres netleşene kadar: `setup.exe` yerel/offline kurulum için tamamen
çalışır durumdadır (kopyalanabilir/USB/paylaşılan klasör ile dağıtılabilir);
yalnızca **otomatik güncelleme kontrolü işlevsizdir** (adres olmadan
kontrol edecek bir yer yoktur).

## 6. Yeni Sürüm Yayımlama (özet iş akışı)

1. §4'teki gibi `FileVersion`'ı artırın.
2. §2'deki komutla yeniden publish alın — **aynı** `PublishDir`'e
   (`publish\ClickOnce\`) yayımlayın, silmeyin (ClickOnce, her sürümü ayrı
   `Application Files\Lens.Desktop_<versiyon>\` alt klasöründe tutar, eskisi
   otomatik silinmez — bu ClickOnce'ın kendi mekanizmasıdır).
3. §3'teki doğrulamayı tekrarlayın.
4. Gerçek dağıtım adresi tanımlıysa (§5), içeriği o adrese kopyalayın/
   senkronize edin (UNC klasör için basit dosya kopyalama; IIS/HTTPS için
   ilgili sunucu sürecine göre).
5. İstemciler, `UpdateMode=Foreground` (bu profildeki ayar) olduğu için
   uygulama açılışında güncelleme kontrolü yapar ve yeni sürüm varsa
   kullanıcıya sorar.

## 7. İmzalama

Şu an **gerçek şirket kod imzalama sertifikası yoktur**. Bu turda:

- Sahte/kişisel bir sertifika **üretilmedi**.
- Windows sertifika deposuna hiçbir şey **yüklenmedi**.
- `ClickOnce.pubxml` içinde `<SignManifests>false</SignManifests>` —
  deployment/application manifestleri **imzasız**.

**Sonuç:** Windows, kurulum sırasında "Bilinmeyen yayıncı" uyarısı
gösterebilir (Smart App Control/SmartScreen etkiyse daha da belirgin olabilir
— bkz. `docs/DEMO_DEPLOYMENT_GUIDE.md` §7, taşınabilir paket için benzer bir
gözlem zaten kayıtlı).

**Açık karar (bekliyor):** Üretim ClickOnce manifestleri, gerçek bir şirket
kod imzalama sertifikasıyla imzalanmalıdır (bkz. `docs/DECISIONS.md`
"Not Yet Decided"). Sertifika temin edildiğinde:

```xml
<SignManifests>true</SignManifests>
<ManifestCertificateThumbprint>...</ManifestCertificateThumbprint>
<!-- veya bir .pfx dosyasi: -->
<ManifestKeyFile>...\gercek-sertifika.pfx</ManifestKeyFile>
```

`.pfx`/sertifika parolası **asla** repoya commit edilmemelidir (bkz.
`CLAUDE.md` kural 7) — `.gitignore` ile hariç tutulan yerel bir dosya veya
kurumsal sertifika deposu kullanılmalıdır.

## 8. Model ve `appsettings.json` Doğrulaması (paket içinde)

- Model: `Application Files\Lens.Desktop_<versiyon>\models\clip-vision-b16-openai.onnx.deploy`
  — `MainWindow.ResolveModelPath()` (`src/Lens.Desktop/MainWindow.xaml.cs`)
  bunu `AppContext.BaseDirectory\models\...` altında arar; ClickOnce her
  sürümü kendi `Application Files\...\` klasörüne kurduğu ve
  `AppContext.BaseDirectory` o klasörü gösterdiği için yol çözümü **aynen
  çalışır** — yerinde publish testinde doğrulandı, ayrıca uygulamanın kurulu
  haliyle açılıp modelin yüklendiği canlı olarak da doğrulanmalıdır (bkz.
  §"Kullanıcıdan Beklenen Canlı Test Adımları").
- `appsettings.json`: `Application Files\Lens.Desktop_<versiyon>\appsettings.json.deploy`
  — repodaki hali zaten güvenli boş şablon (`{"AdminDefaultProductDirectory": ""}`),
  gerçek UNC yol **içermez**. IT, kurulumdan sonra gerekiyorsa **kurulu
  uygulamanın** `appsettings.json`'ını elle düzenleyebilir — ancak bkz. §9
  "Ayarlar ve Güncelleme Davranışı" için önemli bir uyarı.

## 9. Ayarlar ve Güncelleme Davranışı

Kod incelemesiyle doğrulandı (`Lens.Core.Config.AppPaths`):

| Veri | Konum | ClickOnce güncellemesinde korunur mu? |
|---|---|---|
| Kullanıcının seçtiği tarama klasörü (override) | `%LocalAppData%\Lens\config\user-settings.json` | **Evet** — kurulum/güncelleme klasörünün tamamen dışında |
| Minimum benzerlik / en fazla sonuç / otomatik indeksleme / tema tercihi | Aynı `user-settings.json` içinde | **Evet** |
| Log dosyaları | `%LocalAppData%\Lens\logs\` | **Evet** |
| Local cache (kullanılmıyor, bkz. `docs/DECISIONS.md` #61) | `%LocalAppData%\Lens\cache\` | Evet (etkisiz zaten) |
| Shared index | `<ProductDirectory>/.lens/index.json` (kullanıcı verisiyle aynı paylaşılan klasörde) | **Evet** — publish/kurulum klasöründen tamamen bağımsız |

Bunların hepsi `%LocalAppData%` veya ürün dizini altında, ClickOnce'ın her
sürümü ayrı bir `Application Files\Lens.Desktop_<versiyon>\` klasörüne
kurduğu mekanizmanın tamamen dışındadır — bir ClickOnce güncellemesi bu
verileri **silmez veya değiştirmez** (kod incelemesiyle doğrulandı, ayrıca
canlı ikinci-sürüm testinde teyit edilmelidir).

**⚠️ Önemli uyarı (mevcut riskin ClickOnce'a yansıması):**
`docs/DEPLOYMENT.md` §4'te zaten belirtildiği gibi, `appsettings.json`
(`AdminDefaultProductDirectory`) publish paketinin **içindedir**, kullanıcıya
özel kalıcı bir yer değildir. Bir yöneticinin **kurulu ClickOnce
uygulamasının** `Application Files\Lens.Desktop_<versiyon>\appsettings.json`
dosyasını elle düzenlemesi **güvenilir bir yöntem değildir** — ClickOnce her
güncellemede **yeni bir sürüm klasörü** oluşturur (`Lens.Desktop_<yeni
versiyon>\`), önceki sürümdeki elle yapılan `appsettings.json` düzenlemesi
yeni sürüme **otomatik taşınmaz**. Bu, mevcut taşınabilir publish'teki aynı
riskin (bkz. `docs/DEPLOYMENT.md` §4) ClickOnce'a özgü bir uzantısıdır —
**bu turda yalnızca raporlanmaktadır, kapsamı büyüten bir mimari çözüm
(örn. `appsettings.json`'ı da `%LocalAppData%`'ya taşımak) bu görevde
YAPILMAMIŞTIR** (bkz. `CLAUDE.md` kural 2-3). Kalıcı bir admin-default
UNC yolu gerekiyorsa, `docs/DECISIONS.md` #41-42'deki tasarımın (admin
default / kullanıcı override ayrımı) ClickOnce ile nasıl bir arada
çalışacağı ayrı bir karar konusu olarak ele alınmalıdır.

## 10. Taşınabilir Paket Yedek Dağıtım Seçeneği Olarak Kalıyor

`docs/DEPLOYMENT.md` §1'deki `dotnet publish -r win-x64 (self contained
true)` komutu **değişmeden** çalışmaya devam eder — bu turda yerinde
doğrulandı (`publish/verify-portable` çıktısı, model + `appsettings.json`
dahil, sorunsuz üretildi). ClickOnce kurulum/güncelleme gerektirmeyen
senaryolarda (ör. tek seferlik elle kopyalama, USB ile taşıma) taşınabilir
paket kullanılmaya devam edebilir.

## 11. Kaldırma ve Önceki Sürüme Dönme

- **Kaldırma:** ClickOnce kurulan uygulamalar Windows "Uygulamalar ve
  Özellikler" (Programs and Features) listesinde görünür — standart Windows
  kaldırma akışıyla kaldırılabilir. Kullanıcı bazlı kurulum olduğundan
  (`InstallFrom=Disk`, `Install=true`, admin hakkı gerektirmez) her kullanıcı
  kendi kurulumunu kaldırabilir.
- **Önceki sürüme dönme:** ClickOnce'ın yerleşik bir "rollback" arayüzü
  yoktur (kullanıcı Programs and Features'tan yalnızca mevcut sürümü
  kaldırabilir/yeniden kurabilir). Önceki sürüme dönmek için: (a) önceki
  sürümün `setup.exe`'sini saklayın (§6'daki `Application Files\
  Lens.Desktop_<eski-versiyon>\` klasörü kendi başına yeniden kurulum için
  yeterli değildir — publish anındaki tam `publish\ClickOnce\` çıktısının o
  sürüme ait haliyle saklanması gerekir, tıpkı `docs/DEPLOYMENT.md` §7'deki
  taşınabilir paket rollback önerisiyle aynı mantıkla sürüm/tarih etiketli
  ayrı bir klasörde), (b) kullanıcı mevcut kurulumu kaldırıp saklanan eski
  `setup.exe`'yi çalıştırır. Kullanıcı verileri/ayarları (§9) bu süreçte
  etkilenmez.

## 12. Kullanıcıdan Beklenen Canlı Test Adımları (en fazla 3)

Bu tur yalnızca komut satırından doğrulama yaptı, gerçek kurulum/güncelleme
penceresi **açılmadı**. Aşağıdaki adımlar kullanıcı tarafından elle
doğrulanmalıdır:

1. Aynı ClickOnce sürümü daha önce kurulduysa **önce eski Lens kurulumunu
   kaldırın** (Windows ikon önbelleği nedeniyle üzerine kurulum yeni ikonu
   hemen göstermeyebilir — bkz. §2c), ardından yenilenen `publish\ClickOnce\`
   klasöründeki `setup.exe` ile temiz kurulum yapın.
2. Lens'i **hem Başlat menüsünden hem masaüstündeki `Lens` kısayolundan**
   açın (§2b); masaüstü, Başlat menüsü, uygulama penceresi başlığı ve görev
   çubuğundaki simgelerin yeni ikonu (lime zemin + çiçekli büyüteç)
   gösterdiğini, modelin (CLIP ONNX) hatasız yüklendiğini ve sol alt
   köşedeki sürüm metninin (`08.09.26 — v1.0`) doğru göründüğünü
   doğrulayın.
3. `FileVersion`'ı artırıp (§4) yeniden publish alıp aynı `PublishDir`'e
   yayımladıktan sonra (gerçek dağıtımda: gerçek adrese kopyaladıktan sonra),
   uygulamayı yeniden açıp güncellemenin sorulduğunu/uygulandığını ve daha
   önce seçtiğiniz tarama klasörü/eşik/tema gibi tercihlerin **korunduğunu**
   doğrulayın.

## 12b. [PİLOT] DINOv2 deneme paketi — `feature/dinov2-base-pilot`

> Bu bölüm yalnızca `feature/dinov2-base-pilot` dalını anlatır. `main`
> dalındaki CLIP tabanlı ClickOnce profili ve paketi **değiştirilmemiştir**.

Kullanıcının DINOv2-Base sürümünü **kurup deneyebilmesi** için, mevcut CLIP
paketine hiç dokunmadan ayrı bir ClickOnce paketi üretilir.

| | CLIP (mevcut) | DINOv2 pilotu (yeni) |
|---|---|---|
| Profil | `Properties/PublishProfiles/ClickOnce.pubxml` | `Properties/PublishProfiles/ClickOnceDinoV2.pubxml` |
| Çıktı klasörü | `publish/ClickOnce/` | `publish/ClickOnce-dinov2-base/` |
| Görünen ad | `Lens` | `Lens (DINOv2 Pilot)` |
| ClickOnce sürümü | `1.0.0.0` (`$(FileVersion)`'dan türer) | `1.1.0.0` (bilerek sabit — aşağıya bkz.) |
| Paketlenen model | `clip-vision-b16-openai.onnx` | `dinov2-base.onnx` (~330 MB) |

Diğer **tüm** ayarlar bilerek aynıdır: kullanıcı bazlı kurulum,
self-contained (hedefte .NET gerekmez), çevrimdışı çalışma, masaüstü +
Başlat menüsü kısayolu, imzasız manifestler, boş `InstallUrl`/`UpdateUrl`,
placeholder yayıncı adı.

### Üretme komutu

```
"%ProgramFiles%\Microsoft Visual Studio\<sürüm>\<edition>\MSBuild\Current\Bin\MSBuild.exe" ^
  src\Lens.Desktop\Lens.Desktop.csproj /t:Restore,Publish ^
  /p:PublishProfile=ClickOnceDinoV2 /p:Configuration=Release /p:DebugType=none
```

`/p:DebugType=none`'ın komut satırında **ayrıca** verilmesi gerekçesi §2
"Neden `/p:DebugType=none` Ayrıca Gerekli" ile aynıdır ve bu profilde de
geçerlidir.

### ⚠️ Yan yana kurulum YOKTUR — pilot, kurulu Lens'in üzerine kurulur

ClickOnce'ın kurulum **kimliği** görünen addan (`ProductName`) değil,
deployment manifestindeki `assemblyIdentity` adından türer; o da
`AssemblyName`'e bağlıdır. Her iki paket de aynı kimliği taşır:

```
<assemblyIdentity name="Lens.Desktop.application" version="..." />
```

Sonuç: pilot paketi kurulduğunda **mevcut kurulu Lens'in üzerine kurulur**,
iki uygulama yan yana durmaz. Yan yana kurulum için `AssemblyName`'in
değiştirilmesi gerekirdi — bu, pilot kapsamı dışında bilerek bırakıldı
(uygulama adını/çıktı dosya adını değiştirmek daha geniş bir değişikliktir).

Masaüstü/Başlat menüsü kısayolu **`Lens (DINOv2 Pilot)`** adıyla oluşur, bu
sayede hangi sürümün kurulu olduğu kısayol adından anlaşılır.

**Neden sürüm `1.1.0.0`:** `FileVersion` hâlâ `1.0.0.0` ve mevcut CLIP
paketi de `1.0.0.0` ile üretilmişti. Aynı kimlik + aynı sürümle kurulum
denendiğinde ClickOnce "zaten kurulu" deyip hiçbir şey yapmayabilir, yani
pilot **denenemez**. Bu yüzden pilot profilinde `ApplicationVersion` bilerek
sabit `1.1.0.0`'dır — bu bir ürün sürüm artışı **değildir** ve §4'teki
"tek sürüm kaynağı `FileVersion`" kuralını değiştirmez.
`Lens.Desktop.csproj`'daki `AssemblyVersion`/`FileVersion`/
`InformationalVersion` **değiştirilmemiştir**; uygulama içinde görünen sürüm
metni hâlâ `08.09.26 — v1.0`'dır (iki farklı sürüm kavramı için bkz. §4).

**Pilot kabul edilirse** bu override kaldırılmalı ve normal `$(FileVersion)`
akışına dönülmelidir.

### CLIP sürümüne geri dönme

```
publish\ClickOnce\setup.exe
```

Eski paket yerinde durmaktadır. Alternatif olarak Windows
**Ayarlar → Uygulamalar** üzerinden `Lens (DINOv2 Pilot)` kaldırılıp eski
kurulum tekrar yapılabilir (bkz. §11).

**İndeks açısından risk yoktur:** DINOv2 sürümü kendi indeksini
`<ÜrünDizini>\.lens\indexes\dinov2-base-v1\` altında tutar; CLIP'in
`<ÜrünDizini>\.lens\index.json` dosyasına **dokunmaz**. CLIP sürümüne
dönüldüğünde yeniden indeksleme gerekmez (bkz. `docs/MODEL_CARD.md`
"PİLOT ENTEGRASYON", `docs/DECISIONS.md` #96).

### Bu paketin doğrulanmış içeriği (2026-09-10)

| Kontrol | Sonuç |
|---|---|
| Toplam boyut / dosya | 506 MB / 478 dosya |
| `setup.exe` + `Lens.Desktop.application` + `Application Files\Lens.Desktop_1_1_0_0` | ✅ mevcut |
| `models\dinov2-base.onnx.deploy` | ✅ mevcut |
| `appsettings.json.deploy` | ✅ mevcut, **boş şablon** (`AdminDefaultProductDirectory: ""`) |
| `co.v1:createDesktopShortcut="true"` | ✅ manifestte mevcut |
| `asmv2:product="Lens (DINOv2 Pilot)"` | ✅ manifestte mevcut |
| CLIP modeli | ✅ **yok** |
| `.pdb` dosyası | ✅ **yok** |
| `Lens.Core.dll` / `Lens.Desktop.dll` içinde gömülü PDB yolu | ✅ **yok** |
| Yerel geliştirici yolu (`...\Users\<kullanıcı>\...`) | ✅ **yok** |
| Gerçek kullanıcı ayarı / index / log / ürün görseli | ✅ **yok** |
| Eski `publish\ClickOnce\` klasörü | ✅ **değişmedi** (dosya tarihleri korundu) |

**Yapılmayan:** paket kurulmadı ve çalıştırılmadı — kurulum penceresi ve
canlı arayüz kullanıcının izni olmadan açılmadı. Kurulum davranışının
(özellikle "üzerine kurulma" ve kısayol adı) gerçek doğrulaması
kullanıcıyı beklemektedir.

## 12c. Çok modelli + desen kodu paketi — `feature/desen-code-integration`

> Bu bölüm yalnızca `feature/desen-code-integration` dalını anlatır. `main`
> dalındaki CLIP ClickOnce profili/paketi, DINOv2 pilot paketi ve çok modelli
> pilot paketi **değiştirilmemiştir**.

| | Çok modelli pilot (mevcut) | Çok modelli + desen kodu (yeni) |
|---|---|---|
| Profil | `ClickOnceMultiModel.pubxml` | `ClickOnceMultiModelDesenCode.pubxml` |
| Çıktı klasörü | `publish/ClickOnce-multi-model/` | `publish/ClickOnce-multi-model-desen-code/` |
| `AssemblyName` | `Lens.Desktop.MultiModel` | `Lens.Desktop.MultiModel` (**aynı — bilerek**) |
| Manifest kimliği | `Lens.Desktop.MultiModel.application` | `Lens.Desktop.MultiModel.application` (**aynı**) |
| Görünen ad | `Lens (Çok Modelli Pilot)` | `Lens (Çok Modelli Pilot)` (**aynı**) |
| ClickOnce sürümü | `1.0.0.0` | **`1.0.0.1`** (`ApplicationRevision` 0 → 1) |
| Paketlenen model | DINOv2 + CLIP | DINOv2 + CLIP (değişmedi) |
| Desen kodu servisi | yok | var |

### ⚠ Bu paket hangi kurulumun güncellemesidir?

Kurulu **"Lens (Çok Modelli Pilot)"** kurulumunun güncellemesidir ve onun
**üzerine** kurulur. ClickOnce'ın kurulum kimliği görünen addan değil
deployment manifestindeki `assemblyIdentity name` değerinden türer; bu da
`AssemblyName`'e bağlıdır. `AssemblyName` **bilerek değiştirilmedi**, çünkü:

- Aynı özellik setinin ikinci bir kopyası, ikinci bir ayar/log klasörü ve
  kullanıcıda "hangisi güncel?" belirsizliği yaratırdı.
- Desen kodu entegrasyonu çok modelli sürümün **devamıdır**, alternatifi
  değil.

Diğer paketler (`Lens`, `Lens (DINOv2 Pilot)`) **etkilenmez** — onların
kimlikleri farklıdır.

### ⚠ Neden `ApplicationRevision` artırıldı?

Mevcut `publish/ClickOnce-multi-model/` paketi `1.0.0.0` ile üretildi. **Aynı
kimlik + aynı sürüm** ile ikinci bir paket ClickOnce tarafından "zaten kurulu"
sayılır ve kurulum/güncelleme **çalışmaz** — DINOv2 pilotunda yaşanan durumun
aynısı (§12b, karar #97). Bu yüzden yalnızca ClickOnce'a özel
`ApplicationRevision` `0` → `1` yapıldı; paket sürümü `1.0.0.1` olur.

Bu bir **ürün sürüm artışı değildir**: `Lens.Desktop.csproj`'daki
`AssemblyVersion` / `FileVersion` / `InformationalVersion` **değiştirilmedi**
(uygulamada görünen sürüm hâlâ `08.09.26 — v1.0`) ve karar #80'in "tek sürüm
kaynağı" kuralı korundu — `ApplicationVersion` hâlâ `$(FileVersion)`'dan
türer, yalnızca revizyon numarası artar.

Sonraki paketlerde revizyon **artırılmaya devam etmelidir** (1 → 2 → …),
aksi halde aynı sorun tekrarlar.

> ⚠ **Tuzak (üretim sırasında yaşandı ve doğrulandı):**
> `ApplicationRevision` **yalnızca** `ApplicationVersion` `.*` ile bittiğinde
> uygulanır. Düz `$(FileVersion)` (= `1.0.0.0`) yazıldığında revizyon
> **sessizce yok sayılır** ve paket yine `1.0.0.0` olarak üretilir — hata
> vermez, manifest kontrol edilmezse fark edilmez. İlk üretimde tam olarak bu
> oldu; profil `$(FileVersion)`'ın ilk üç parçasını alıp sonuna `.*`
> ekleyecek şekilde düzeltildi:
>
> ```xml
> <ApplicationVersion>$(FileVersion.Substring(0, $(FileVersion.LastIndexOf(".")))).*</ApplicationVersion>
> <ApplicationRevision>1</ApplicationRevision>
> ```
>
> Sürüm numarası hâlâ tek kaynaktan (`FileVersion`) türer, elle yazılmaz.
> **Her publish'ten sonra `*.application` dosyasındaki `assemblyIdentity`
> `version` değeri elle kontrol edilmelidir.**

### ⚠ Servis adresi ve manifest bütünlüğü

ClickOnce, dağıtılan **her dosyanın** hash'ini manifeste yazar. Paket
üretildikten **sonra** `appsettings.json`'ı (veya başka bir dosyayı) elle
düzenlemek, kurulumda **doğrulama hatasına** yol açar.

Bu yüzden gerçek uç adresi **publish anında** `appsettings.json` içinde
olmalıdır. Repo'daki örnek dosyada `Endpoint` **boştur ve boş kalır**; adres
kaynak koda gömülmez. Adres değişirse **yeni bir publish üretilir**.

Kimlik bilgisi (VPN kullanıcı adı/şifre) hiçbir dosyaya yazılmaz.

### Üretme komutu

```
"%ProgramFiles%\Microsoft Visual Studio\<sürüm>\<edition>\MSBuild\Current\Bin\MSBuild.exe" ^
  src\Lens.Desktop\Lens.Desktop.csproj /t:Restore,Publish ^
  /p:PublishProfile=ClickOnceMultiModelDesenCode /p:Configuration=Release /p:DebugType=none
```

`dotnet publish` ClickOnce manifestini **desteklemez** (MSB4803) — tam .NET
Framework MSBuild gerekir.

### Bu paketin doğrulanmış içeriği (2026-09-11)

| | Değer |
|---|---|
| Manifest kimliği | `Lens.Desktop.MultiModel.application` |
| Manifest sürümü | **`1.0.0.1`** (elle kontrol edildi) |
| Görünen ad | `Lens (Çok Modelli Pilot)` |
| `Application Files` klasörü | `Lens.Desktop.MultiModel_1_0_0_1` |
| Dosya sayısı / boyut | 479 dosya / ~834 MB |
| Masaüstü kısayolu | `createDesktopShortcut="true"` ✅ |
| Modeller | `dinov2-base.onnx.deploy`, `clip-vision-b16-openai.onnx.deploy` ✅ |
| `appsettings.json.deploy` | var, `Endpoint` **dolu** (gerçek uç) ✅ |
| `.pdb` | **yok** ✅ |
| Kullanıcı ayarı / log / index / ürün görseli | **yok** ✅ |
| Yerel geliştirici yolu (`Lens.Core.dll`, `Lens.Desktop.dll`) | **yok** ✅ |

**Paket kurulmadı ve çalıştırılmadı** — kurulum penceresi ve canlı arayüz
kullanıcının izni olmadan açılmadı. "Üzerine kurulma" davranışının gerçek
doğrulaması kullanıcıyı beklemektedir.

### Geri dönme

Çok modelli sürümün desen kodsuz hâline dönmek için
`publish\ClickOnce-multi-model\setup.exe` yeniden çalıştırılır. **İndeks
açısından risk yoktur** — desen kodu metadata'sı
(`.lens\metadata\desen-codes-v1.json`) embedding indekslerinden ayrıdır,
yeniden indeksleme gerekmez.

## 12d. Kompakt arama yerleşimi paketi — `feature/compact-search-layout`

> Yalnızca `feature/compact-search-layout` dalını anlatır. Önceki ClickOnce
> profilleri ve çıktıları **değiştirilmemiştir**.

| | Çok modelli + desen kodu | Kompakt yerleşim (yeni) |
|---|---|---|
| Profil | `ClickOnceMultiModelDesenCode.pubxml` | `ClickOnceCompactLayout.pubxml` |
| Çıktı klasörü | `publish/ClickOnce-multi-model-desen-code/` | `publish/ClickOnce-compact-search-layout/` |
| `AssemblyName` | `Lens.Desktop.MultiModel` | `Lens.Desktop.MultiModel` (**aynı**) |
| Manifest kimliği | `Lens.Desktop.MultiModel.application` | `Lens.Desktop.MultiModel.application` (**aynı**) |
| Görünen ad | `Lens (Çok Modelli Pilot)` | `Lens (Çok Modelli Pilot)` (**aynı**) |
| ClickOnce sürümü | `1.0.0.1` | **`1.0.0.2`** (`ApplicationRevision` 2) |
| İçerik | DINOv2 + CLIP + desen kodu | aynı + kompakt arama yerleşimi |

**Hangi kurulumun güncellemesi?** Kurulu **"Lens (Çok Modelli Pilot)"**
kurulumunun — onun üzerine kurulur. Yan yana kurulum yoktur; bu bir yerleşim
düzenlemesidir, ayrı bir ürün değil. `Lens` ve `Lens (DINOv2 Pilot)`
kurulumları **etkilenmez**.

**Sürüm zinciri:** `1.0.0.0` (çok modelli) → `1.0.0.1` (desen kodu) →
`1.0.0.2` (kompakt yerleşim). Sonraki pakette revizyon **artırılmaya devam
etmelidir**; aynı kimlik + aynı sürüm ClickOnce tarafından "zaten kurulu"
sayılır ve kurulum çalışmaz.

> ⚠ `ApplicationRevision` **yalnızca** `ApplicationVersion` `.*` ile bittiğinde
> uygulanır (bkz. §12c'deki tuzak). Profil bu yüzden
> `$(FileVersion)`'ın ilk üç parçasını alıp sonuna `.*` ekler. **Her
> publish'ten sonra `*.application` içindeki `assemblyIdentity` `version`
> değeri elle kontrol edilmelidir.**

### Üretme komutu

```
"%ProgramFiles%\Microsoft Visual Studio\<sürüm>\<edition>\MSBuild\Current\Bin\MSBuild.exe" ^
  src\Lens.Desktop\Lens.Desktop.csproj /t:Restore,Publish ^
  /p:PublishProfile=ClickOnceCompactLayout /p:Configuration=Release /p:DebugType=none
```

### Bu paketin doğrulanmış içeriği (2026-09-11)

| | Değer |
|---|---|
| Manifest kimliği | `Lens.Desktop.MultiModel.application` |
| Manifest sürümü | **`1.0.0.2`** (manifestten okundu) |
| Görünen ad | `Lens (Çok Modelli Pilot)` |
| `Application Files` klasörü | `Lens.Desktop.MultiModel_1_0_0_2` |
| Dosya sayısı / boyut | 479 dosya / ~834 MB |
| Masaüstü kısayolu | `createDesktopShortcut="true"` ✅ |
| Modeller | `dinov2-base.onnx.deploy`, `clip-vision-b16-openai.onnx.deploy` ✅ |
| Desen kodu ayarları | `appsettings.json.deploy` içinde `Endpoint`/`MethodName`/`ParameterName` **dolu** ✅ |
| `.pdb` | **yok** ✅ |

**Paket kurulmadı ve çalıştırılmadı** — kurulum penceresi ve canlı arayüz
kullanıcının izni olmadan açılmadı.

### Geri dönme

Kompakt yerleşim öncesine dönmek için
`publish\ClickOnce-multi-model-desen-code\setup.exe` yeniden çalıştırılır.
**Not:** ClickOnce daha düşük sürüme (`1.0.0.1`) kendiliğinden dönmez; önce
Denetim Masası'ndan kaldırmak gerekebilir. İndeks açısından risk yoktur —
yerleşim değişikliği indeksleri ve desen kodu metadata'sını etkilemez.

## 13. Açık Kararlar (özet)

Bkz. `docs/DECISIONS.md` "Not Yet Decided": yayıncı/şirket adı, gerçek
UNC/HTTPS güncelleme adresi, şirket kod imzalama sertifikası. Bu üçü
netleşmeden **üretim dağıtımı tamamlanmış sayılmaz** (yerel ClickOnce
hazırlığı tamamdır, üretim değildir).

## İlgili Dokümanlar

- Taşınabilir publish akışı (değişmedi): `docs/DEPLOYMENT.md`
- Sürüm alanlarının genel yönetimi: `docs/DEPLOYMENT.md` §9
- Rollout öncesi açık maddeler: `docs/PRODUCTION_CHECKLIST.md`
- Kararlar: `docs/DECISIONS.md`
