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

1. Varsa eski bir ClickOnce Lens kurulumunu kaldırın, ardından
   `publish\ClickOnce\setup.exe` ile kurulum yapın.
2. Lens'i **hem Başlat menüsünden hem masaüstündeki `Lens` kısayolundan**
   açın (§2b), modelin (CLIP ONNX) hatasız yüklendiğini ve sol alt
   köşedeki sürüm metninin (`07.09.2026 — v1.0`) doğru göründüğünü
   doğrulayın.
3. `FileVersion`'ı artırıp (§4) yeniden publish alıp aynı `PublishDir`'e
   yayımladıktan sonra (gerçek dağıtımda: gerçek adrese kopyaladıktan sonra),
   uygulamayı yeniden açıp güncellemenin sorulduğunu/uygulandığını ve daha
   önce seçtiğiniz tarama klasörü/eşik/tema gibi tercihlerin **korunduğunu**
   doğrulayın.

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
