# Immich Uploader

Windowsin ilmoitusalueella toimiva Immich-taustalataaja. Se tarkkailee valittuja kansioita ja lähettää tuetut kuvat ja videot Immichiin.

## Tue kehitystä

Sovellus on maksuton henkilökohtaiseen käyttöön. Jos siitä on sinulle iloa, voit halutessasi tukea kehitystä [Ko-fi-palvelussa](https://ko-fi.com/lafkato). Lahjoitus on aina vapaaehtoinen.

## Käyttö

1. Käynnistä `ImmichUploader.exe` ja avaa asetukset.
2. Anna Immich-palvelimen API-osoite, esimerkiksi `https://immich.example.com/api`, sekä API-avain.
3. Valitse vähintään yksi tarkkailtava kansio ja tallenna.
4. Ilmoitusalueen valikosta voi keskeyttää lataukset, käynnistää täysskannauksen tai avata lokin.

API-avain salataan Windows DPAPI:lla. Vain sama Windows-käyttäjä samalla koneella voi purkaa sen. Vanhasta selväkielisestä `config.json`-asetuksesta siirrytään salattuun muotoon seuraavan tallennuksen yhteydessä.

## Luotettavuus

- Latausjono on rajattu ja saman tiedoston päällekkäiset tapahtumat yhdistetään.
- Tiedoston koko ja muokkausaika tarkistetaan ennen latausta; keskeneräistä tiedostoa yritetään myöhemmin uudelleen.
- Tilapäiset verkko- ja palvelinvirheet yritetään uudelleen kasvavalla viiveellä, enintään kuusi kertaa.
- Jos albumiin lisäys epäonnistuu latauksen jälkeen, se säilyy korjausjonossa ja yritetään myöhemmissä skannauksissa.
- `FileSystemWatcher`-virhe käynnistää täysskannauksen. Valikossa on myös **Skannaa nyt**.

Tila tallennetaan käyttäjäprofiilin hakemistoon `.immich-uploader`: asetukset, loki ja `upload-history.json`. Historia sisältää hasheja ja tiedostofingerprinttejä, joiden avulla muuttumattomia tiedostoja ei hashata uudelleen jokaisella tarkistuskierroksella.

## Kehitys

```powershell
dotnet build ImmichUploaderApp.sln -c Release
dotnet run --project tests\ImmichUploaderApp.SmokeTests\ImmichUploaderApp.SmokeTests.csproj -c Release
```

Smoke-testit tarkistavat URL- ja MIME-käsittelyn sekä sen, ettei API-avain serialisoidu selväkielisenä.

## Asennuspaketti

Windows-asennuspaketti rakennetaan Inno Setup 6:lla. Julkaise sovellus ensin hakemistoon `publish\\release` ja käännä sen jälkeen [installer/ImmichUploader.iss](installer/ImmichUploader.iss). Asennuspaketti sisältää sovelluksen, työpöytäkuvakkeen valinnaisesti sekä poistotoiminnon.

## Lisenssi

Lähdekoodi on näkyvissä läpinäkyvyyttä ja oppimista varten, mutta projekti ei ole avoimen lähdekoodin ohjelmisto. Sovelluksen muuttamattomia binäärijulkaisuja saa käyttää ja jakaa henkilökohtaiseen, ei-kaupalliseen käyttöön. Muokkaus, johdannaisteokset ja kaupallinen käyttö edellyttävät tekijän kirjallista lupaa. Katso [LICENSE](LICENSE).
