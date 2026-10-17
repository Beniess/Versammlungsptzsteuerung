# Freigabestand

Stand: 17.10.2026. **Keine uneingeschränkte Release-Freigabe für 1.0.0.7.** Automatisierte Tests und die Erzeugung des Windows-x64-Artefakts sind nachgewiesen. Die Gleichwertigkeit der finalen EXE mit allen manuellen Nachweisen für 1.0.0.3 ist nicht vollständig belegt. Darstellung bei 125/150/200 Prozent, Hochkontrast und reale Kamera bleiben offen.

## Geprüfter Stand und Versionszuordnung

- Release-Tag [v1.0.0.7](https://github.com/Beniess/Versammlungsptzsteuerung/tree/v1.0.0.7) und beide erfolgreichen Läufe gehören zu Commit `34611537be20ad5dfa3806cae7c9b2e15dfd3be9`.
- [Run 38567761767](https://github.com/Beniess/Versammlungsptzsteuerung/actions/runs/38567761767) ist der Tag-Build für **1.0.0.7**. Die heruntergeladene EXE hat 350.597.236 Bytes und SHA256 `FC58908ED339ADA57EB763BED60761F61E6D16520DB8559511B527E1C38CB0CE`. Version und Hash stimmen zwischen Publish-Protokoll, Build-Metadaten, Download-Prüfsummen und lokal gehashter Artefakt-EXE überein.
- [Run 38566982610](https://github.com/Beniess/Versammlungsptzsteuerung/actions/runs/38566982610) ist der Main-Build desselben Commits, aber mit Version **0.1.3.1**. Er ist ein zweiter erfolgreicher Quellstandstest, kein zweiter Build der Release-Version.
- Das [Prerelease 1.0.0.7](https://github.com/Beniess/Versammlungsptzsteuerung/releases/tag/v1.0.0.7) ist seit 14.10.2026 veröffentlicht. Sein EXE-Asset hat laut GitHub denselben SHA256. Ein veröffentlichtes Prerelease ist keine fachliche Vollfreigabe.
- Der zusätzliche Release-Eintrag [2.0.0.1 / V2 Beta](https://github.com/Beniess/Versammlungsptzsteuerung/releases/tag/2.0.0.1) vom 15.10. zeigt auf denselben Commit und hat keine Assets. Der Workflow reagiert auf `v*`-Tags. Daraus folgt kein zusätzlicher Build- oder Testnachweis für eine EXE mit Version 2.0.0.1.

## Änderungen gegenüber der dokumentierten Basis

Verglichen wurde der [importierte Quellstand e8346df](https://github.com/Beniess/Versammlungsptzsteuerung/tree/e8346df0f5738f01e2674a63284eac07693e3579), dessen Dokumentation den Kandidaten 1.0.0.3 beschreibt, mit dem [Release-Commit 3461153](https://github.com/Beniess/Versammlungsptzsteuerung/compare/e8346df0f5738f01e2674a63284eac07693e3579...34611537be20ad5dfa3806cae7c9b2e15dfd3be9). Ein eigener Git-Tag für 1.0.0.3 und die damalige EXE liegen im Repository nicht vor. Dieser Vergleich beweist Quellkontinuität seit dem Import, keine binäre Identität zum historischen Kandidaten.

Die Git-Blob-IDs aller Dateien in WinUI, AppCore und VISCA sowie deren drei Testprojekten, `scripts/` und `tests/` sind unverändert. Ebenso unverändert: `global.json`, App-Projekt und NuGet-Lockdatei. Es gibt in diesem Vergleich keine Änderung an Kamerabefehlen, Oberfläche, Fotoablauf, Persistenz oder Deployment-Implementierung.

Geändert wurden:

| Änderung | Prüfergebnis und Grenze |
| --- | --- |
| Windows-CI und Release-Erstellung | Das unkonfigurierte Desktop-Template wurde durch Offline-Tests, ausgewählte Windows-Adaptertests, Single-EXE-Publish, Prüfsummen und Release-Entwurf ersetzt. |
| Lizenzdateien und `.gitattributes` | 34 geänderte Dateien unter `LICENSES/` sind nach ausschließlicher CRLF→LF-Normalisierung inhaltsgleich. `LICENSES/** -text` und `tests/fixtures/** -text` erhalten Checkout-Bytes. Die 48 indizierten Originaldateien aus dem heruntergeladenen App-Quellarchiv stimmen mit ihren unveränderten Sollhashes überein. |
| IPC-Testhost | `-p:ThreadPoolMinThreads=8` gilt im Adapter-Testaufruf. Produktcode, IPC-Fristen und Assertions wurden nicht geändert; der Publish-Aufruf enthält diese Option nicht. |
| README und Build-Dokumentation | Veröffentlichung und Grenzen der CI beschrieben; keine zusätzlichen manuellen Testergebnisse. |

## Automatisiert nachgewiesen

Die sieben TRX-Dateien des Tag-Builds wurden aus dem Diagnoseartefakt gelesen und ihre Zähler einzeln geprüft:

| Suite | Ausgeführt / bestanden | Fehler / nicht ausgeführt |
| --- | --- | --- |
| VISCA/Legacy, .NET Framework 4.8.1 | 92 / 92 | 0 / 0 |
| VISCA/Transport/Protokoll, .NET 10 | 63 / 63 | 0 / 0 |
| AppCore Persistence | 31 / 31 | 0 / 0 |
| AppCore Controls | 47 / 47 | 0 / 0 |
| AppCore Presets | 45 / 45 | 0 / 0 |
| AppCore Deployment | 55 / 55 | 0 / 0 |
| **Offline gesamt** | **333 / 333** | **0 / 0** |
| Ausgewählte Windows-Adaptertests | **108 / 108** | **0 / 0** |

Der historische Stand nennt 314 Offline-Fälle, davon 26 Presets. Aktuell sind es 45 Presets, also 19 mehr. Diese höhere Zahl ist im aktuellen Lauf belegt; weil die Tests seit dem importierten Dokumentationsstand unverändert sind, lässt sich daraus keine neue Testcode-Änderung zwischen diesen beiden Git-Commits ableiten.

Die 108 Adaptertests decken die 13 im [Workflow](../.github/workflows/dotnet-desktop.yml) ausgewählten Kategorien ab, darunter Deployment/IPC, LayoutPolicy, Video-Adapter und LicenseCatalog. Sie binden Produktionsquellen und Lizenzressourcen in eine eigene Testassembly ein. Sie starten keine vollständige native WinUI-Bedienprüfung der finalen EXE. Ein LayoutPolicy-PASS belegt keine sichtbare DPI- oder Hochkontrastdarstellung.

Publish ist für Windows x64, self-contained und Single-File bestanden. Das Skript prüft eine einzige Ausgabedatei, File-/Produktversion 1.0.0.7, die finale Dateikopie und ein Inventar von 422 nativen VLC-Paketdateien. Dieses Inventar ist kein erneuter Laufzeitnachweis geladener Module oder der statischen CLR aus der finalen EXE. Alle vier Einträge der Artefakt-`SHA256SUMS.txt` wurden lokal gegen die heruntergeladenen Dateien geprüft. Der Release-Job hat diese vier Dateien ebenfalls erfolgreich geprüft und den Entwurf erstellt.

### Vorherige Fehler und Gegenläufe

| Lauf | Nachprüfbares Ergebnis |
| --- | --- |
| [38544539577](https://github.com/Beniess/Versammlungsptzsteuerung/actions/runs/38544539577) | Unkonfiguriertes Template: Release-Job scheitert beim Restore mit MSB1009 für `your-solution-name`; Debug-Job abgebrochen. Kein Nachweis einer Produktregression. |
| [38565290806](https://github.com/Beniess/Versammlungsptzsteuerung/actions/runs/38565290806) | 333 Offline-Tests bestanden; Adaptertests 105/108. Zwei IPC-Timeouts (`AbandonedPartialRequestDoesNotBlockTheNextReadyRequest`, `CandidateColdStartHasItsOwnBoundBeforeTheResponsiveShellDeadline`) und ungültiger Lizenzhash (`EmbeddedCatalogPreservesEveryIndexedOriginalAndItsHash`). Publish übersprungen. |
| 38566982610 und 38567761767 | Jeweils 333 Offline- und 108 Adaptertests bestanden. Alle drei zuvor fehlgeschlagenen Tests sind im Tag-TRX einzeln als bestanden enthalten. |

Die Gegenläufe stützen die Checkout- und Testhost-Korrektur. Zwei grüne Läufe mit angehobener Worker-Mindestzahl beweisen keine allgemeine IPC-Zeitstabilität der produktiven EXE auf schwachen oder ausgelasteten Geräten.

## Manuelle und native Nachweise: Geltungsbereich

| Bereich | Bereits dokumentiert für 1.0.0.3 | Aussage für 1.0.0.7 |
| --- | --- | --- |
| Sauberes Windows 11, normaler Erststart, Windows-Suche und Download-Löschung | Vier Gast-Szenarien, erwarteter Host-Hash, private WinUI-/VLC-Module, ohne separat installiertes .NET/VLC | Quellcode und Publish-Skript unverändert; kein neuer finaler EXE-Gastlauf nachgewiesen |
| Versionswechsel, Parallelstart, minimierte Aktivierung und Fehlerpfade | Sieben Pflichtabläufe über zwei Läufe, davon zwei gezielte Gegenfälle | Core- und Adaptertests grün; tatsächliches Upgrade 1.0.0.3→1.0.0.7 und native Aktivierung nicht neu belegt |
| Helles/dunkles Layout, Lizenzdialog 360 × 280 bei 96 DPI, Bild-ab/Escape | Historische Sichtprüfung dokumentiert | Unveränderte Views; aktuelle Katalog-/Hashprüfung bestanden, sichtbarer Dialog der neuen EXE nicht erneut geprüft |
| Native Steuerungs-/Stoppabläufe, Foto, Neustart, zehn Aufnahmezyklen, Loopback-Livebild, Bibliotheksaustausch | Historische Gast-/Loopback-Nachweise dokumentiert | Unveränderte Funktionsquellen; keine erneute native End-to-End-Prüfung der 1.0.0.7-EXE |
| Bibliotheks-Quellenbegleitsatz | 144 Quelldateien historisch über Hashes zugeordnet | Quellenarchiv, Manifeste und Validierungsdatei sind als Release-Assets vorhanden; der 755-MB-Begleitsatz und alle 144 Zuordnungen wurden in diesem Review nicht neu validiert |
| 125/150/200 Prozent und Hochkontrast | Ausdrücklich nicht bestanden/nicht nachgewiesen | **Weiter offen** |
| Reale Kamera, physische Bewegung und Bildsynchronität | Kein Test durchgeführt | **Weiter offen** |

Die alten Reviews sind eingecheckt, etwa [Clean Windows](../.omo/start-work/reviews/T9-clean-windows.md) und [letzte Oberflächenprüfung](../.omo/start-work/reviews/T10-final-surface.md). Die dort und unten referenzierten `.omo/evidence/`-JSONs und PNGs fehlen im Git-Baum; `.gitignore` schließt diesen Ordner aus. Die damaligen Beobachtungen werden als historische Dokumentation erhalten, hier aber weder als neu ausgeführt noch als unabhängig anhand der Originalbilder nachgeprüft ausgewiesen.

## Konkrete Regressionsrisiken und offene Abnahme

1. **Versions-/Paketwechsel:** Andere Version und anderer EXE-Hash können Installation, Versionsauswahl, Verknüpfung und Pipe-Aktivierung betreffen, obwohl ihre Quellen gleich sind. Den tatsächlich gehashten 1.0.0.7-Download auf sauberem Windows und im Upgrade von 1.0.0.3 prüfen; Daten, Suche nach Download-Löschung, Parallelstart und minimierte Aktivierung beobachten.
2. **IPC unter knappen Ressourcen:** Die zwei früheren Timeouts betreffen gemeinsame simulierte Client-/Serverprozesse. Der Testhost-Fix verändert deren Scheduling, nicht das Produkt. Normalen Erststart und Versionswechsel mit der Release-EXE unter begrenzten Ressourcen gezielt prüfen; keine allgemeine Produktkorrektur behaupten.
3. **Lizenzressourcen:** Byte-/Indexprüfung jetzt grün; der Adaptertest untersucht seine eigene Assembly. Den Lizenzdialog der finalen EXE einschließlich Mindestgröße, Scrollen und Escape sowie den passenden Quellenbegleitsatz versionsbezogen bestätigen.
4. **Darstellung:** 125, 150 und 200 Prozent jeweils sichtbar in hell/dunkel prüfen; Hochkontrast tatsächlich aktivieren und Bedienbarkeit/Lesbarkeit belegen. Frühere Grenzen des Gast-Beobachters sind kein PASS und kein nachgewiesener App-/OS-Fehler.
5. **Gerät und Video:** Reale Kamera mit korrektem VISCA-Port, RTSP-Pfad und Zugangsdaten prüfen. Physisches Home, Bewegungs-/Zoom-Stopp, Foto-/Positionszuordnung und Bildsynchronität beobachten. Loopback und Fakes ersetzen dies nicht.

Gerechtfertigt ist: **1.0.0.7 ist ein CI-geprüftes Windows-x64-Prerelease mit 333 bestandenen Offline- und 108 bestandenen Adaptertests sowie verifizierter Artefaktzuordnung.** Historische manuelle Befunde bleiben relevante Referenz für unveränderten Funktionscode. Nicht gerechtfertigt sind vollständige Gleichwertigkeit aller früheren Binärnachweise, bestandene höhere DPI/Hochkontrast, bestätigter realer Kamerabetrieb oder uneingeschränkte Release-Freigabe.

## Dauerhaft gesicherte aktuelle CI-Belege

Das originale [Diagnose-ZIP](evidence/1.0.0.7-ci-results.zip) des Tag-Builds ist mit SHA256 `3DCF159DF8ECB481FD96D8680326FA05FA7465A27628ACD7A5C3083A3F1ED529` im Repository gesichert. Es enthält sieben TRX-Dateien, Offline-Zusammenfassungen, Fixture-/Quellhashes, Publish-Zusammenfassung und natives Paketinventar. Der [Review-Auszug](evidence/1.0.0.7-review.json) hält Zähler, Artefakt-/Dateihashes, Quellvergleich und Prüfgrenzen fest.

GitHub-Artefakt `Windows-x64-1.0.0.7` (ID 11895524565) läuft am **13.11.2026, 13:01:48 UTC** ab; Diagnoseartefakt (ID 11895644185) am **28.10.2026, 13:01:54 UTC**. Das gesicherte ZIP erhält die aktuellen CI-Rohdaten über diese Frist hinaus. Es enthält keine neuen Desktopbilder oder Hardwaretests. Dieser Review führte keine Windows-/Kameratests aus.

## Historischer Prüfstand 1.0.0.3

Der folgende Stand vom 11.10.2026 bleibt als damalige Dokumentation erhalten. Seine auf 1.0.0.3 begrenzten Aussagen und lokal referenzierten Originalbelege sind von den oben neu geprüften CI-Ergebnissen zu unterscheiden.

Stand: 11.10.2026. **Keine uneingeschränkte Release-Freigabe.** Der geprüfte Kandidat hat Version 1.0.0.3. Darstellung bei 125/150/200 Prozent, Hochkontrast und Betrieb mit einer realen Kamera bleiben offen.

| Bereich | Stand |
| --- | --- |
| Historischer Import | Archivnachweis erhalten; getrennt von der neuen App dokumentiert |
| VISCA-Referenz und AppCore | Offline-All: 314/314 bestanden, kein Fehler und kein übersprungener Fall; einschließlich 55 Deployment-Fällen |
| Technischer Video-/Einzel-EXE-Nachweis | Livebild, PNG-Aufnahme und private WinUI-/VLC-Dateien nachgewiesen; statische CLR im geprüften Single-File-Host belegt |
| Sichtbare WinUI-Oberfläche | Helles und dunkles Layout bei 96 DPI geprüft. Lizenzdialog auf Kandidat 1.0.0.3 bei 360 × 280 Pixeln geprüft; Textbereich, Bild-ab und Escape bestätigt |
| DPI und Themen | 125/150/200 Prozent und Hochkontrast im begrenzten Gastlauf nicht nachgewiesen. Dies ist kein Nachweis einer allgemeinen OS-Unfähigkeit |
| Native Pointer- und Tastaturabläufe | Native Steuerungs-/Stoppabläufe im isolierten Gast bestanden; die vorhandenen Belege gelten für den unveränderten Steuerungscode |
| Foto und Vorschau | Fotos mit Neustart, zehn Aufnahmezyklen, Fehler-/Schließabläufe und kompatibler Bibliotheksaustausch bestanden |
| Normaler EXE-Erststart und Versionswechsel | Nach engem Pipe-Fix: 2/2 Gastfälle für A→B, Download-Löschung, Verknüpfungsstart, Parallelstart und minimierte Fensteraktivierung bestanden. Die fünf weiteren Pflichtfälle sind im vorigen Gastlauf bestanden |
| Sauberes Windows 11 | Kandidat 1.0.0.3 auf Windows 11 Build 26100 ohne separat installierte .NET-Runtime oder VLC gestartet; eigenes HWND, Host-Hash und private WinUI-/VLC-Dateien bestätigt |
| Windows-Suche und gestartete Version | Echte Windows-Suche startet die installierte Version 1.0.0.3. Erneuter Suchstart nach Download-Löschung mit unveränderten Daten bestanden |
| Lizenzquellen und Drittsoftware-Unterlagen | 26 Komponenten und 48 indizierte Originaltexte direkt aus der EXE geprüft; 144 vorhandene Quelldateien über Hashes zugeordnet. Keine pauschale Lizenzfestlegung für die eigene App |
| Reale Kamera | Kein Betrieb und keine physische Bewegungs-/Bildsynchronitätsprüfung durchgeführt |

Belege: [Offline-All 314/314](../.omo/evidence/winui3-modernisierung/T9/offline-current-20261010-222413-557/all-summary.json), [Gast-Gegenlauf 2/2](../.omo/evidence/winui3-modernisierung/T8/guest-release-20261010-224728-674/guest-output/Release/release-summary.json) und [gezielter Startpfad-Review](../.omo/evidence/winui3-modernisierung/T8/targeted-startup-review-20261011-122941-445.json). Die fünf weiteren Fälle prüfen Startfehler, Abbruch, gesperrte Verknüpfung mit Wiederherstellung, verweigertes Schließen und eine nicht erreichbare Kamera. Alle sieben Pflichtabläufe sind damit über die beiden Läufe belegt; ein neuer vollständiger Siebenerlauf wird nicht behauptet. Die eigenen Testordner und Prozesse wurden jeweils ohne Kill bereinigt.

Die [aktuelle Lizenzprüfung](../.omo/evidence/winui3-modernisierung/T10/guest-current-20261011-121206-601-EF648A14/LicenseEnvironment/summary.json) belegt den Dialog am Mindestmaß. Die [beobachteten OS-Elemente](../.omo/evidence/winui3-modernisierung/T10/guest-current-20261011-121206-601-EF648A14/LicenseEnvironment/os-boundary.json) belegen nur den begrenzten Prüfversuch für höhere Skalierung und Hochkontrast; diese Punkte sind nicht bestanden.

Der Kandidat 1.0.0.3 hat SHA256 `EF648A14732672BE2331F45AD24C0B9647CDB796DE48DDF84F0764DB4B9E3227`. Der [CleanWindows-/Suchlauf](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/clean-windows-results.json) besteht alle vier Szenarien. [Suchtreffer](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/search-after-download-removal.png) und [Livebild](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/clean-windows-preview.png) wurden im echten Gast aufgenommen. Der Gast meldete Exitcode 0. Der Host-Beobachter sah den Client zunächst noch beim Schließen; die [abschließende CIM-Prüfung](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/cleanup-final.json) bestätigt null Clients, ohne Kill.

Der [Quellvergleich](../.omo/evidence/winui3-modernisierung/T10/publish-candidate-20261011-132043-450/root-source-delta.json) zeigt gegenüber dem zuvor geprüften B-Stand nur Änderungen am Lizenzdialogkopf und am Austauschdokument. Die Video-, Steuerungs-, Deployment- und Core-Belege bleiben für diesen unveränderten Funktionscode gültig. Ein neuer vollständiger Deployment- oder Offline-Lauf wurde nicht ausgeführt.

Die aktuelle QA verwendet ausschließlich eigene Daten und lokale Testendpunkte. Ein erfolgreicher Loopback-Test bestätigt keine gerätespezifischen Eigenschaften einer realen Kamera. VISCA-Port, RTSP-Pfad, Zugangsdaten und physisches Home-Verhalten des eingesetzten Geräts bleiben vor dem echten Betrieb zu prüfen.

Offene Punkte werden erst nach einem ausgeführten Pflichtfall aktualisiert. Ein fehlender Desktop, eine gesperrte Eingabe oder ein ungültiges Bild ist kein PASS.
