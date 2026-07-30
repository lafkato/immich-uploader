<p align="center">
  <img src="assets/immich-uploader-banner.svg" alt="Immich Uploader" width="100%">
</p>

<p align="center">
  <a href="https://github.com/lafkato/immich-uploader/releases/latest"><img src="https://img.shields.io/github/v/release/lafkato/immich-uploader?label=Download&color=20B486" alt="Latest release"></a>
  <a href="https://ko-fi.com/lafkato"><img src="https://img.shields.io/badge/Ko--fi-Support%20development-FF5E5B?logo=ko-fi&amp;logoColor=white" alt="Support development on Ko-fi"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-personal%20non--commercial-4D7198" alt="Personal non-commercial license"></a>
</p>

<p align="center"><strong>🇫🇮 <a href="#suomi">Suomi</a> · 🇬🇧 <a href="#english">English</a></strong></p>

---

## Suomi

**Immich Uploader** on kevyt Windowsin ilmoitusalueella toimiva taustalataaja. Se tarkkailee valitsemiasi kansioita ja varmuuskopioi kuvat ja videot automaattisesti omaan Immich-palvelimeesi.

### Lataa ja aloita

1. [Lataa uusin julkaisu](https://github.com/lafkato/immich-uploader/releases/latest) ja käynnistä `ImmichUploader.exe`.
2. Anna asetuksissa Immich-palvelimen API-osoite ja API-avain.
3. Valitse tarkkailtavat kansiot. Sovellus jatkaa toimintaansa huomaamattomasti ilmoitusalueella.

> [!TIP]
> Sovellus on self-contained: erillistä .NET-asennusta ei tarvita.

> [!NOTE]
> Julkaisu rakennetaan self-contained-kansioksi pakatun yhden `.exe`-tiedoston sijaan. Tämä pitää runtime-tiedostot läpinäkyvinä ja vähentää virustorjunnan heuristiikkaosumia.

### Ominaisuudet

- Automaattinen kuvien ja videoiden lataus valituista kansioista
- Kaksisuuntainen synkronointi: peilaa koko Immich-kirjastosi (tai osan siitä) paikalliseen kansioon kevyenä esikatseluna tai täysikokoisina tiedostoina - katso tarkemmin alta
- Albumin valinta, kansioiden poissulku sekä Windowsin käynnistyksen yhteydessä avautuminen
- Päällekkäisten latausten esto, tiedoston valmiustarkistus ja automaattiset uudelleenyritykset
- Suomen, englannin, ruotsin ja saksan kielet sekä vaalea/tumma ulkoasu
- Manuaalinen päivitystarkastus ja -asennus suoraan asetuksista
- API-avain suojataan Windowsin DPAPI-salauksella

### Kuvien ja videoiden lataus Immichistä

Lataussuunta toimii myös toisin päin: Immich Uploader voi peilata koko Immich-kirjastosi paikalliseen kansioon, samaan tapaan kuin Google Drive, OneDrive tai iCloud synkronoivat pilvitiedostoja koneellesi. Ota käyttöön asetusten Lataukset-välilehdeltä:

- Erilliset kohdekansiot kuville ja videoille
- Kevyt tila lataa pienet esikatselukuvat; täysi koko lataa alkuperäiset tiedostot. Videot ladataan aina alkuperäisenä, koska Immichillä ei ole kevyttä videoesikatselua
- Kansiot voi järjestää joko kuukauden tai Immich-albumin mukaan
- Valinnainen kaksisuuntainen poistosynkronointi: paikallisesti poistetun tiedoston voi siirtää roskakoriin myös Immichissä, ja Immichistä poistetut kuvat katoavat automaattisesti paikallisesti
- Sama kansio voi turvallisesti olla sekä tarkkailtava (lataus Immichiin) että ladattava (lataus Immichistä) kansio - sovellus ei koskaan lataa itse lataamaansa tiedostoa takaisin Immichiin

### Julkaisun rakentaminen

```powershell
dotnet publish src\ImmichUploaderApp\ImmichUploaderApp.csproj -c Release -o publish\release
```

Asennuspaketti tehdään Inno Setup 6:lla tiedostosta `installer\ImmichUploader.iss`. Asennuspaketti ottaa mukaan koko `publish\release`-kansion.

### Tue kehitystä ☕

Sovellus on maksuton henkilökohtaiseen käyttöön. Jos siitä on sinulle iloa, voit halutessasi auttaa uusia päivityksiä ja ylläpitoa pienellä kahvirahalla. Lahjoitus on aina täysin vapaaehtoinen.

<p align="center">
  <a href="https://ko-fi.com/lafkato"><img src="https://img.shields.io/badge/Tue%20kehityst%C3%A4-Ko--fi-FF5E5B?style=for-the-badge&amp;logo=ko-fi&amp;logoColor=white" alt="Tue kehitystä Ko-fi:ssa"></a>
</p>

### Yksityisyys ja lisenssi

Sovellus tallentaa asetukset ja lataushistorian Windows-käyttäjäprofiiliisi. API-avain salataan käyttäjäkohtaisesti. Lähdekoodi on näkyvissä läpinäkyvyyttä varten, mutta projekti ei ole avoimen lähdekoodin ohjelmisto: käyttö on sallittu henkilökohtaiseen, ei-kaupalliseen tarkoitukseen. Katso tarkat ehdot [lisenssistä](LICENSE).

---

## English

**Immich Uploader** is a lightweight Windows tray app that watches the folders you choose and automatically backs up photos and videos to your own Immich server.

### Download and get started

1. [Download the latest release](https://github.com/lafkato/immich-uploader/releases/latest) and run `ImmichUploader.exe`.
2. Enter your Immich API URL and API key in Settings.
3. Choose the folders to watch. The app then runs quietly in the system tray.

> [!TIP]
> The app is self-contained — no separate .NET installation is required.

> [!NOTE]
> Releases are built as a self-contained folder instead of a packed single `.exe`. This keeps runtime files transparent and reduces false-positive antivirus heuristics.

### Highlights

- Automatic photo and video uploads from selected folders
- Two-way sync: mirror your whole Immich library (or part of it) to a local folder, as lightweight previews or full-size originals - see below
- Album selection, folder exclusions, and optional start with Windows
- Duplicate prevention, file-stability checks, and automatic retries
- Finnish, English, Swedish, and German interfaces with light and dark themes
- Manual update check and one-click install from Settings
- Your API key is protected with Windows DPAPI encryption

### Downloading photos and videos from Immich

The upload direction also works in reverse: Immich Uploader can mirror your whole Immich library to a local folder, the same way Google Drive, OneDrive, or iCloud sync cloud files to your computer. Enable it from the Downloads tab in Settings:

- Separate destination folders for photos and videos
- Lightweight mode downloads small preview images; full size downloads the original files. Videos always download at full size, since Immich has no lightweight video preview
- Folders can be organized by month or by Immich album
- Optional two-way delete sync: a locally deleted file can be trashed on the Immich side too, and photos deleted from Immich disappear locally on the next scan
- The same folder can safely be both watched (uploaded to Immich) and downloaded to (from Immich) - the app never re-uploads a file it just downloaded itself

### Release build

```powershell
dotnet publish src\ImmichUploaderApp\ImmichUploaderApp.csproj -c Release -o publish\release
```

Build the installer with Inno Setup 6 from `installer\ImmichUploader.iss`. The installer includes the whole `publish\release` folder.

### Support development ☕

Immich Uploader is free for personal use. If it makes your photo backup easier, you can optionally support future updates and maintenance with a small coffee. Donations are always voluntary.

<p align="center">
  <a href="https://ko-fi.com/lafkato"><img src="https://img.shields.io/badge/Support%20on-Ko--fi-FF5E5B?style=for-the-badge&amp;logo=ko-fi&amp;logoColor=white" alt="Support on Ko-fi"></a>
</p>

### Privacy and license

The app stores its settings and upload history in your Windows user profile. API keys are encrypted for that Windows user. The source is available for transparency, but this is not open-source software: use is permitted for personal, non-commercial purposes. Read the full [license](LICENSE).
