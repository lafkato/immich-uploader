# v1.2.4

## Suomi

- Korjataan päivityksen lataus, kun vanha samanniminen EXE-tiedosto on toisen prosessin lukitsema. Jokainen lataus käyttää omaa tilapäiskansiota ja valmis tiedosto suljetaan ennen asennusta.
- Käytetään asennuspaketin lataukseen erillistä aikarajaa ja binääritiedoston pyyntöotsaketta.
- Tarkistetaan ladatun EXE:n koko ja GitHubin SHA-256-tarkistussumma; epäonnistuneet osittaiset lataukset poistetaan.
- Tallennetaan päivityksen latausvirheen tarkat tiedot lokiin.

**Vanhan version päivitys:** jos v1.2.2:n tai v1.2.3:n lataus jää tiedostolukon vuoksi jumiin, asenna tämä korjaus suoraan GitHubin ImmichUploaderSetup-1.2.4.exe-tiedostolla. Korjattu versio hakee tulevat päivitykset GitHubista erilliseen tilapäiskansioon.

## English

- Download each installer to a unique temporary directory so a locked installer from an earlier attempt cannot block the update.
- Separate installer transfer timeout from the short release-metadata timeout and request binary content.
- Verify installer size and GitHub SHA-256 digest, clean up incomplete downloads, and log download errors.

Validation: Release build, smoke tests, locked-file download regression, checksum and incomplete-file checks.

# v1.2.3

## Suomi

- Estetään puuttuvan USB-levyn, verkkokansion tai käyttöoikeusvirheen tulkitseminen kuvien poistamiseksi.
- Erotetaan lataushistoria ja synkronoinnin tila palvelimen ja API-avaimen mukaan; nykyisen tilin vanha historia siirtyy automaattisesti.
- Yritetään epäonnistunutta Immichin roskakoriin siirtoa uudelleen.
- Säilytetään paikalliset poistomerkinnät myös uudelleenkäynnistyksen yli, jotta poistetut kuvat ja albumikopiot eivät palaudu.
- Säilytetään avaamattomien alikansioiden poissulut asetuksia tallennettaessa.
- Odotetaan taustatehtävien päättymistä ennen palveluiden vaihtamista tai sulkemista.
- Yritetään albumin hakua uudelleen ennen latausta, jotta kuvat päätyvät valittuun albumiin.
- Skannaa nyt tarkistaa molemmat synkronointisuunnat.
- Tarkistetaan uudet julkaisut automaattisesti käynnistyessä ja kuuden tunnin välein; asennus käynnistetään asetuksista.

**Päivitys v1.2.2:sta:** vanha versio tarkistaa päivitykset vain manuaalisesti. Valitse asetuksista päivitystarkistus tai suorita ImmichUploaderSetup-1.2.3.exe. Version 1.2.3 asentamisen jälkeen tulevista julkaisuista ilmoitetaan automaattisesti.

## English

- Guard deletion sync against unavailable drives, network folders and access errors.
- Isolate upload history and sync state by server and API key, migrating the existing account's legacy state.
- Retain failed remote trash requests for retry and persist local deletion markers across restarts.
- Preserve deleted album copies and exclusions in unexpanded folder trees.
- Wait for background work before switching services or shutting down.
- Retry album resolution before upload and scan both sync directions from Scan now.
- Check for updates at startup and every six hours; install from Settings.

**Upgrading from v1.2.2:** check for updates manually in Settings or run ImmichUploaderSetup-1.2.3.exe. Automatic notifications are available after installing v1.2.3.

Validation: Release build without warnings and smoke/regression tests using isolated files and fake HTTP responses.
