# winui3-modernisierung - Work Plan

## TL;DR (For humans)
Geplant ist eine kleine Kamerasteuerung für Windows 11 mit vier Bildtasten, manueller Steuerung, Einstellungen und einem getrennten Livebildfenster. Fotos werden aus diesem Livebild aufgenommen und dauerhaft einer Taste zugeordnet.

Du startest eine neue heruntergeladene EXE. Die App richtet die Programmdateien im Benutzerkonto ein und aktualisiert den Eintrag in der Windows-Suche. Nach dem Start einer neueren EXE verwendet dieser Eintrag die neue Version. Ein separater Installationsassistent ist nicht vorgesehen.

Einstellungen und Fotos liegen in einem festen Windows-Benutzerordner. Es gibt keine Sicherungsfunktion und keine Übernahme alter Einstellungen. Die vorhandene Kamerabibliothek bleibt die technische Grundlage.

Vor der Umstellung sichern Tests die bisherigen Kamerabefehle und die Bedienabbildung. Danach muss ein früher technischer Nachweis zeigen, dass Livebild, Fotoaufnahme und die ausgelieferte EXE zusammen funktionieren. Erst dann folgt die vollständige Oberfläche.

Der Aufwand ist groß, weil zusätzlich zur Oberfläche auch Video, Bereitstellung und eine bisher fehlende Testgrundlage entstehen. Das Hauptrisiko ist die Ausführung der nativen Videobibliotheken aus dem EXE-Paket. Eine echte Kamera ist nicht verfügbar. Die Software kann offline geprüft werden; ihr Verhalten am unbekannten Gerät bleibt unbestätigt.

Festgelegte Annahmen: Windows x64; drei normale Positionen mit editierbaren Namen plus Home. Home behält den bestehenden Kamerabefehl. Die Umsetzung ist gestartet. Referenztests, Datenmodell und Videonachweis sind abgenommen. Die integrierte Oberfläche und der lokale Versionswechsel werden jetzt mit echten Windows-Prozessen geprüft.

---

> Status: Umsetzung beauftragt und gestartet. Stand: 10.10.2026. Risiko: Video und lokale Bereitstellung müssen mit dem tatsächlichen Release-Artefakt geprüft werden. Reale Kamera nicht verfügbar.

## Scope
### Must have
- Windows 11, zunächst x64. C#, .NET 10 LTS, WinUI 3 und eine unterstützte Stable-Version des Windows App SDK. Keine Preview-Abhängigkeiten.
- Eine heruntergeladene EXE starten. Die App richtet sich ohne Installationsassistent im Benutzerkonto ein. Eine neuere EXE aktualisiert diese lokale Einrichtung. Der bestehende Startmenüeintrag startet danach die aktivierte neue Version.
- Das ist technisch eine lokale Einrichtung der Programmdateien. Sie benötigt weder MSIX-Paketidentität noch einen Store-Eintrag. Normale Windows-Prüfungen für heruntergeladene Programme bleiben aktiv.
- Hauptfenster frei verschiebbar und skalierbar; Ziel etwa 600 x 430 physische Pixel einschließlich Fensterrahmen auf 1920 x 1080. Vier Bildtasten im 2x2-Raster. Startseite immer Positionen.
- Weitere Seiten für manuelle Steuerung und Einstellungen. Eine globale Vorschau-Aktion öffnet genau ein zusätzliches Fenster mit Livebild.
- Vorschau und Aufnahme von Positionsbildern sind Teil derselben Lieferung. Betrieb ohne separat installiertes VLC, FFmpeg oder Entwicklerwerkzeuge.
- Kamerasteuerung bleibt VISCA über den bestehenden TCP-Client. Bestehende Befehlsbytes, Richtungen und Geschwindigkeiten werden zuerst durch Referenztests festgeschrieben.
- Kamera nicht verfügbar: verbindliche Tests mit festen Referenzdaten, kontrollierten Antworten und lokalem TCP-/RTSP-Server. Diese Tests entscheiden über die Softwarefreigabe.
- Neue Einstellungen und Fotos werden benutzerbezogen in einem stabilen Windows-Datenordner gespeichert. Programmwechsel verändert diesen Ort nicht.
- Einrichtung beginnt ohne Kameraadresse und ohne gespeicherte Positionen. Keine automatische Verbindung zu Adressen aus dem Repository. Nach vollständiger Konfiguration ist eine automatische VISCA-Verbindung beim nächsten Start zulässig; Video startet erst bei Bedarf.

### Festgelegtes Verhalten

| Bereich | Entscheidung |
| --- | --- |
| Vier Tasten | Feste IDs `leser`, `tisch`, `redner`, `home`. Anfangsnamen Leser, Tisch, Redner, Home. Die drei normalen Namen sind editierbar. Reihenfolge bleibt fest. Es gibt keine frei erweiterbare Presetverwaltung. |
| Normale Position | Lokal gespeicherte Pan-, Tilt- und Zoomwerte. Aufruf sendet wie bisher absolute Pan/Tilt-Werte mit Geschwindigkeit 12/12 und den Zoomwert. Keine Umstellung auf Speicherplätze der Kamera. |
| Home | Ruft ausschließlich den bisherigen nativen `GoHome`-Befehl auf. Foto ist zuordenbar. Es wird kein Zoomwert ergänzt und kein Nullpunkt erfunden. Ob die reale Kamera damit den ganzen Raum zeigt, bleibt unbestätigt. |
| Bedienung | Gesamte Bildtaste anklickbar; immer lesbarer Text. Fehlendes Foto zeigt einen Platzhalter. Nicht eingerichtete normale Position ist deaktiviert und als nicht eingerichtet bezeichnet. Home benötigt nur eine Kameraverbindung. |
| Auftragsstatus | Eine Positionsfahrt gleichzeitig. Währenddessen weitere Positionsaufrufe sperren; keine unbeschränkte Befehlswarteschlange. Auswahl zeigt höchstens den letzten Auftrag, niemals ohne Nachweis die aktuelle Kameraposition. |
| Steuerung | Richtung und Geschwindigkeitsabbildung aus dem bisherigen Steuerfeld übernehmen. Loslassen sendet Stopp. Zusätzlich Stopp bei Verlust der Zeigererfassung, Seitenwechsel, Deaktivierung und geordnetem Schließen. Bei getrennter Verbindung keine erfolgreiche Bewegung oder erfolgreichen Stopp anzeigen. |
| Einstellungen | Host/IP und VISCA-Port unabhängig von einer vollständigen RTSP-URL. Verbindung prüfen, Speichern, Neu verbinden. Positionseditor mit lokalem Entwurf; Speichern und Abbrechen. Seitenwechsel zur Steuerung erhält den Entwurf. |
| Fotoablauf | Normale Position wählen, Kamera ausrichten, `Foto aufnehmen`. Diese Aktion übernimmt Bild und aktuelle Pan/Tilt/Zoom-Werte in den Entwurf; Hilfetext nennt beide Wirkungen. Erst Speichern ändert die aktive Taste. Bei Home übernimmt die Aktion nur das Bild. |
| Aufnahmeprüfung | Vorschau sichtbar öffnen/aktivieren; auf ein decodiertes Bild warten; während einer manuellen Bewegung keine Aufnahme. Position vor und nach der Aufnahme lesen. Bei Änderung den Entwurf nicht übernehmen. Das verhindert keine unbekannte Kamerapufferung; eine exakte physische Synchronität wird nicht behauptet. |
| Ohne Video | `Position übernehmen` erlaubt das Einrichten der drei normalen Positionen ohne Foto. Bei geänderter Position wird ein bisheriges Bild im Entwurf entfernt, damit kein altes Bild als neues Ziel erscheint. Fehlgeschlagene Aufnahme verändert gespeicherte Daten nicht. |
| Vorschaufenster | Verschiebbar und skalierbar, Seitenverhältnis erhalten, Audio aus. Erneutes Öffnen aktiviert dasselbe Fenster. Schließen beendet den Stream; das wiederverwendbare WinUI-VideoView darf verborgen erhalten bleiben. Hauptfenster schließen beendet auch Vorschau und native Medienobjekte. |
| Fehler | Steuerungs- und Videostatus getrennt. Videoausfall blockiert keine VISCA-Bedienung. Fehler in der betroffenen Ansicht anzeigen; keine wiederholten modalen Dialoge bei Netzwerkausfall. |

### Datenmodell und Speicherort

- `Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)` liefert den Windows-Benutzerpfad. Darunter `Versammlungsptzsteuerung/Data/`. Dieser Pfad ist unabhängig von Version, Downloads und EXE-Extraktion. Keine hart codierten Benutzernamen.
- `settings.json`: `schemaVersion = 1`, Verbindung, drei Positionsdatensätze, Home-Fotoreferenz und Fensterzustand. Fotoverweise sind relative Dateinamen unter `Images/`, keine beliebigen externen Pfade.
- `ConnectionSettings`: optionaler Host, gültiger Port 1..65535, optionale RTSP-URL. Steuerung und Vorschau getrennt konfigurierbar. Falls die Kamera Zugangsdaten benötigt: Windows Credential Manager mit produktspezifischem Eintragsnamen verwenden; Kennwort nicht in JSON, URL-Logs oder Diagnosen schreiben. Diese Eingaben bleiben leer und optional.
- `PresetSettings`: feste ID, Anzeigename, optionaler Zielwertsatz, optionale Bildreferenz. Pan und Tilt bleiben vorzeichenbehaftete 16-Bit-Werte. Zoom ist im durch den bisherigen Vertrag darstellbaren nichtnegativen Bereich. Keine Hardwaregrenzen aus dem Simulator übernehmen.
- `WindowSettings`: Position und Größe je Fenster sowie ursprüngliche Skalierung. Beim Wiederherstellen auf verfügbare Monitor-Arbeitsfläche begrenzen. Keine unsichtbare Fensterposition nach Monitorwechsel.
- Einstellungen als typisierte Daten mit `System.Text.Json` lesen/schreiben. Neue Datei zunächst neben der Zieldatei schreiben; danach atomar ersetzen. Für eine geänderte Bildzuordnung zuerst eine neue PNG-Datei fertig schreiben, dann den JSON-Verweis ersetzen. Nur nach erfolgreichem Wechsel unreferenzierte eigene Bilder entfernen.
- Kein `.bak`, kein Export/Import und keine Sicherungsverwaltung. Atomarer Dateiaustausch ist Bestandteil der normalen Schreibfunktion.
- Fehlende Datei bedeutet Ersteinrichtung. Ungültige oder unbekannt versionierte Daten erzeugen eine verständliche Meldung; nicht still überschreiben. Neu einrichten ist möglich. Automatische Altformatmigration ist ausgeschlossen.
- Bei ungültiger Konfiguration startet die Oberfläche mit einem getrennten, nicht verbundenen Einrichtungszustand. Erst eine ausdrückliche Neueinrichtung durch den Benutzer ersetzt die Datei. Eine fehlende oder defekte PNG-Datei zeigt nur einen Platzhalter und blockiert weder Start noch Positionsfahrt.

### Technischer Aufbau

| Projekt/Ordner | Verantwortung |
| --- | --- |
| `CameraControl.Visca/` | Vorhandene Protokollbibliothek. Bestehenden netstandard2.0-Pfad behalten; nach grüner Basis den modernen Zielpfad auf net10.0 setzen. |
| `CameraControl.AppCore/` (neu) | Typisierte Einstellungen, Positionsaktionen, Bedienabbildung und Testverträge. Keine XAML- oder Player-Abhängigkeit. |
| `CameraControl.WinUI/` (neu) | WinUI-Seiten, ViewModels, Windows-Speicheradapter, LibVLC-Adapter, Fensterverwaltung und lokale Bereitstellung. Teilbereiche als Ordner; keine zusätzliche Projektstruktur ohne Bedarf. |
| `CameraControl.Visca.Tests/` (neu) | NUnit-Referenz- und TCP-Tests für net481 und net10.0. |
| `CameraControl.AppCore.Tests/` (neu) | NUnit-Tests für Anwendungsaktionen, Speicherung und Versionswechsel. |
| `CameraControl.WinUI.Tests/` (neu) | Windows-Integrationstests, Release-Start und UI-Automation mit FlaUI.UIA3. Medien- und Bereitstellungstests getrennt auswählbar. |
| `scripts/` und `tests/fixtures/` (neu) | Wiederholbare Windows-Prüfungen, feste VISCA-Pakete und lokale Video-Testdaten. |

- ViewModels trennen Zustand und Aktionen von XAML. Keine generische Pluginarchitektur, kein eigener DI-Container, keine pauschale Dateilängengrenze. Kleine konkrete Schnittstellen nur für Kamera, Video, Speicher und Bereitstellung.
- Primärer Videopfad: `LibVLCSharp.WinUI 3.10.1` und `VideoLAN.LibVLC.Windows 3.0.24`. Der vorhandene WebEye-WPF-Control wird nicht in WinUI eingebettet.
- Initialisierung am offiziellen WinUI-Beispiel ausrichten: `VideoView.Initialized` abwarten und `SwapChainOptions` verwenden. Snapshot aus demselben Player; keine zweite RTSP-Verbindung für eine Aufnahme.
- Native VLC-DLLs und zugehörigen Pluginbaum aus dem ausgelieferten Paket laden. Pfad explizit aus dem tatsächlichen Extraktionsverzeichnis bestimmen; nicht auf Arbeitsverzeichnis, PATH oder eine vorhandene VLC-Installation vertrauen.
- Unterstützten Windows-App-SDK-Stable-Patch bei T3 anhand offizieller Releases wählen und zusammen mit allen direkten NuGet-Paketen und `global.json` sperren. Keine Floating-Versionen. Das veröffentlichte net10-Ziel der Videobibliothek ersetzt keinen Laufzeittest mit dieser konkreten Kombination.

### Lokale Einrichtung und Versionswechsel

1. Release als einzelne herunterladbare EXE erzeugen. Unpackaged und self-contained für .NET und Windows App SDK. Die EXE darf Abhängigkeiten beim Start entpacken. Der technische Nachweis muss native Video-Plugins einschließen.
2. Vor Verbindung und Fensterbetrieb den lokalen Startpfad prüfen. Neue EXE vollständig nach `%LOCALAPPDATA%/Programs/Versammlungsptzsteuerung/Versions/<Version>/` kopieren. Dafür den tatsächlichen Prozesspfad nutzen, nicht `Assembly.Location` oder einen temporären Bundlepfad.
3. Nur vollständige, geprüfte Kopien aktivieren. Version numerisch aus einer festen Releaseversion vergleichen. Gleiche Version startet die vorhandene Kopie; ältere Version startet die bereits aktive neuere Kopie. Ein stiller Downgrade ist ausgeschlossen.
4. Ein benutzerbezogener Mutex serialisiert die Einrichtung. Start aus Downloads wird vor normaler Einzelinstanz-Weiterleitung als möglicher Versionswechsel behandelt. Sonst würde eine laufende alte Instanz das Update verschlucken.
5. Laufende alte Instanz per benutzerbezogener IPC geordnet beenden. Sie beendet Eingabe, sendet bei bestehender Verbindung nötige Stopps, speichert Fensterzustand und gibt Kamera/Video frei. Kein erzwungenes Prozessende. Bleibt sie aktiv oder ist das Schreiben gesperrt, bleibt die bisherige Version aktiviert und die neue EXE zeigt den Fehler.
6. Neue lokale Kopie in einem kontrollierten Startmodus starten. Erfolgssignal erst nach Initialisierung und responsivem Hauptfenster; Kamera- oder RTSP-Erreichbarkeit ist keine Voraussetzung für erfolgreichen App-Start. Erst danach Aktivierungsmarker und Startmenüverknüpfung umschalten.
7. Der Link heißt `Versammlungs-Kamerasteuerung.lnk` im benutzerbezogenen Programmeordner aus `Environment.SpecialFolder.Programs`. Er zeigt auf die aktivierte lokale EXE. Name und AppUserModelID bleiben stabil. Nach dem Wechsel den Shell-Änderungshinweis auslösen.
8. Link und Aktivierungsmarker sind zwei Windows-Dateioperationen. Der Wechsel muss Wiederaufnahme und Wiederherstellung bei Teilfehlern definieren. Ein Fehler darf keinen Verweis auf eine unvollständige Version hinterlassen. Während des Wechsels bleibt die letzte funktionsfähige Programmversion vorhanden; dies ist keine Sicherung von Benutzerdaten.
9. Nach erfolgreicher Aktivierung nur nicht mehr laufende, eindeutig eigene alte Programmversionen entfernen. Keine rekursive Entfernung ohne Prüfung des absoluten Pfads unter dem eigenen Versionsordner. Downloads und Benutzerdaten niemals entfernen.
10. Suche muss die neue Version auch nach Löschen der heruntergeladenen EXE starten. Keine Behauptung, dass Windows fremde neue Dateien selbst erkennt oder ohne Verzögerung indiziert.

Die folgenden Zustände machen diesen Ablauf prüfbar: `Unconfigured`, `Active(A)`, `Staged(B)`, `Starting(B)`, `Ready(B)`, `Committed(B)` und `Failed(B)`. `Active(A)` bleibt die gültige Referenz bis `Ready(B)`. Ein kleines Transaktionsprotokoll enthält nur Programmversionen und absolute, validierte eigene Pfade; keine Kopie der Benutzerdaten.

- Candidate-Start erfolgt mit einem einmaligen IPC-Token und geprüftem Pfad unter dem eigenen Versionsordner. Er darf die Installationssperre nicht erneut anfordern und keine unendliche Neustartfolge auslösen.
- Kandidat liest Daten zunächst ohne Änderungen. Automatische Kameraverbindung, Schreibzugriffe und Bedienaktionen werden erst nach Aktivierung freigegeben. So verändert ein fehlgeschlagener Start keine bestehenden Daten.
- Die neue Verknüpfung vor dem Umschalten als temporäre Datei vollständig erzeugen. Nach `Ready(B)` den Aktivierungsmarker atomar ersetzen, dann den gleichnamigen Link austauschen. Bei Linkfehler den Marker auf A zurückstellen und B geordnet schließen. A bis zum vollständigen Abschluss behalten.
- Nach einem Prozessabbruch prüfen beide Startpfade zuerst das Transaktionsprotokoll. Eine alte vorhandene lokale EXE darf anhand des gültigen Aktivierungsmarkers an die aktivierte Version weiterleiten. Unvollständige Übergänge werden anhand der vorhandenen und geprüften Dateien beendet oder auf A zurückgesetzt. Kein blindes Löschen.
- Bei Erstinstallation ohne A führt ein Fehler zu einer verständlichen Fehlermeldung und einer erneut ausführbaren Einrichtung. Es darf kein scheinbar gültiger Startmenüeintrag auf unvollständige Dateien entstehen.

### Must NOT have (guardrails, anti-slop, scope boundaries)
- Keine Sicherung, kein Export/Import, keine Übernahme oder Auswertung alter `App.config` im neuen Produkt.
- Kein Internet-Updater, kein Update-Server, kein Windows-Dienst, kein Autostart, keine Cloud und keine Telemetrie.
- Keine echte Kameraverbindung während Entwicklung oder Test. Den vorhandenen Stresstest mit fester LAN-Adresse nicht ausführen.
- Keine Port-, Codec-, Authentifizierungs- oder Home-Garantie für die unbekannte Kamera aus lokalen Tests ableiten.
- Keine Änderung des Kameraprotokolls oder zusätzlicher Home-Zoombefehl im Rahmen der UI-Migration.
- Keine automatische Systemänderung, Paketinstallation, App-Registrierung oder Veröffentlichung während dieser Planung.
- Alte WPF-Anwendung bis zur erfolgreichen Ablösung als Referenz erhalten. Keine Änderungen fremder Arbeit zurücksetzen.
- Kein umfangreicher Prüfapparat mit mehreren Reviewwellen. Ein gezielter Review an den großen Übergängen und eine abschließende Prüfung genügen.

## Verification strategy
Alle Softwareprüfungen sind vom ausführenden Agenten ausführbar. Eine echte Kamera ist nicht Teil dieser Testumgebung. Sie darf nicht als geprüft ausgewiesen werden.

- Testentscheidung des Benutzers: **Tests zuerst**. NUnit mit VSTest-Adapter; Frameworkversionen bei Einrichtung sperren. Erwartete VISCA-Bytes stehen ausdrücklich in Fixture-Dateien und werden niemals mit dem Produktionsencoder erzeugt.
- T1/T2 prüfen die alte Bibliothek über net481/netstandard2.0 und über net10 mit dem bisherigen modernen Bibliotheksziel. Nach der Retargetierung laufen dieselben Fixtures erneut gegen netstandard2.0/net10.
- Bei der Retargetierung wird ausschließlich der erwartete moderne Frameworkname im Metadatennachweis von net6 auf net10 geändert. Wire-Fixtures und Verhaltensassertions bleiben unverändert.
- Golden-Daten sichern Befehle, Werte und zulässige Abfolgen. Sie sichern keine zufälligen Thread-Zeitpunkte, physische Bewegungszeiten oder bekannten Fehler. Änderungen an Referenzdaten sind getrennt zu begründen.
- Zugelassene Verhaltensänderungen: neue Speicherung ohne Altübernahme; asynchrone Abfragen statt UI-Blockade; eine gemeinsame Pan/Tilt-Abfrage statt redundanter Doppelabfrage; zusätzliche Stoppauslöser; Video startet bei Bedarf; getrennte Ports. Unverändert bleiben die fachlichen Zielwerte und die dafür gesendeten VISCA-Pakete.
- Loopback-TCP-Tests mit dynamischem Port; keine reale Netzwerkadresse. ACK vor Completion, Fehler, kurze/fragmentierte Antworten, mehrere Pakete pro Lesevorgang, EOF, Timeout, Abbruch und nächste Verbindung prüfen.
- Vorhandenen `FakeViscaCamera` nur ergänzend für Bedienabläufe nutzen. Er umgeht die echte Protokollfehlerbehandlung, simuliert Home anders und bildet Abbruch laufender Bewegungen nicht zuverlässig ab.
- RTSP-Tests: MediaMTX und FFmpeg als ausschließlich lokale Testwerkzeuge. Listener nur auf `127.0.0.1`, TCP, andere Dienste aus. Testclip H.264 mit sichtbarer Bewegung, Bildnummer und bekannten Bildmerkmalen. Produktion liefert diese Testserver nicht mit.
- Testbefehle werden in T1/T2/T3 angelegt. Sie liefern einen Fehlercode bei fehlendem Werkzeug, null gefundenen Tests, Testfehlern oder fehlenden Pflichtnachweisen. Kein übersprungener Test darf als Erfolg erscheinen.
- Basisbefehle: `pwsh -File scripts/Test-Offline.ps1 -Suite Legacy`, `pwsh -File scripts/Test-Offline.ps1 -Suite All`, `pwsh -File scripts/Test-Desktop.ps1 -Scenario All`, `pwsh -File scripts/Test-Release.ps1 -Scenario All`.
- UI-Automation auf einem aktiven Windows-Desktop über FlaUI/UIA3, nicht über Browserautomation. Tastatur, Maus, Fokus, Fehlerzustände und gerenderte Screenshots prüfen.
- Fenstergrößen: 600 x 430 physische Pixel als Referenz sowie 360 x 280 DIPs als Zielminimum. 100, 125, 150 und 200 Prozent Skalierung prüfen. Bei hoher Skalierung hat das lesbare Mindestmaß Vorrang vor exakt einem Achtel der Bildschirmfläche. Keine abgeschnittenen Tasten und kein Scrollen auf der Positionsseite.
- Release-Prüfung auf sauberem Windows 11 ohne Entwickler-Runtimes und VLC in Windows Sandbox oder einer verfügbaren isolierten Test-VM. Wenn eine solche Umgebung fehlt, als fehlenden Nachweis melden; vorhandene Runtimes auf dem Arbeitsrechner nicht entfernen.
- Belege pro Aufgabe unter `.omo/evidence/winui3-modernisierung/T<N>/`: TRX, Testanzahl, Exitcode, verwendete Paketversionen, Screenshots und kurze Ergebnismatrix. Kennwörter und Zugangsdaten nicht aufzeichnen.

### Verbindliche Referenzmatrix

Die Tabelle legt den Mindestumfang fest. Weitere Randfälle aus den genannten Methoden gehören ebenfalls zu T1/T2. Hexwerte sind literale Testdaten. Pan/Tilt-Geschwindigkeiten stehen im Drahtpaket als Beträge; die Richtung steht in eigenen Bytes.

| Eingabe/Aktion | Erwartung |
| --- | --- |
| Steuerfeld x/W = 0; 0,25; 0,5; 0,75; 1, bei y/H = 0,5 | Pan = -24; -12; 0; +12; +24. Tilt = 0. |
| Steuerfeld y/H = 0; 0,25; 0,5; 0,75; 1, bei x/W = 0,5 | Tilt = +24; +12; 0; -12; -24. Pan = 0. |
| Zoomfeld x/W = 0; 0,25; 0,5; 0,75; 1 | Zoomgeschwindigkeit = -7; -4; 0; +4; +7. |
| Zeiger unter 0 oder über Breite/Höhe | Betrag bleibt auf 24 für Pan/Tilt beziehungsweise 7 für Zoom begrenzt. |
| Rechts, Maximum, Tilt still | `81 01 06 01 18 01 02 03 FF` |
| Links, Maximum, Tilt still | `81 01 06 01 18 01 01 03 FF` |
| Oben, Maximum, Pan still | `81 01 06 01 01 18 03 01 FF` |
| Unten, Maximum, Pan still | `81 01 06 01 01 18 03 02 FF` |
| Rechts oben, beide Maximum | `81 01 06 01 18 18 02 01 FF` |
| Pan/Tilt-Stopp nach Bewegung | `81 01 06 01 01 01 03 03 FF` |
| Zoom hinein / hinaus mit Maximum | `81 01 04 07 27 FF` / `81 01 04 07 37 FF` |
| Zoom-Stopp nach Bewegung | `81 01 04 07 00 FF` |
| Ziel Pan=1, Tilt=-1, Zoom=4660 | Erst `81 01 06 02 0C 0C 00 00 00 01 0F 0F 0F 0F FF`, dann `81 01 04 47 01 02 03 04 FF`. |
| Home | `81 01 06 04 FF`; kein nachfolgender Zoom-Befehl. |
| Pan/Tilt lesen | Anfrage `81 09 06 12 FF`; Antwort `90 50 0F 0F 0F 0F 00 00 00 01 FF` ergibt Pan=-1, Tilt=1. |
| Zoom lesen | Anfrage `81 09 04 47 FF`; Antwort `90 50 01 02 03 04 FF` ergibt 4660. |
| Empfangsbestätigung und Abschluss | `90 41 FF` allein lässt den Auftrag offen; danach `90 51 FF` beendet ihn erfolgreich. |
| Gerätefehler | `90 60 02 FF` erzeugt `ViscaResponseException`; kein Erfolgsergebnis. |

Die bisherigen Setter unterdrücken identische Geschwindigkeitszustände. Tests für Stopp beginnen deshalb mit einer laufenden Bewegung. Beim Port darf eine Wiederholung nicht als Ersatz für einen fehlenden Stoppnachweis dienen. Zusätzliche Stopps bei Fokus-/Erfassungsverlust sind ausdrücklich neue Fehlerbehandlung.

## Execution strategy
### Parallel execution waves
- Abschnitt A: T1, danach T2. Danach verbindlicher Halt für Prüfung der Basisergebnisse. Kein Produktrefactor vorher.
- Abschnitt B: T3 als technischer Nachweis und T4 für die davon unabhängige Persistenz parallel nach T2. T5 darf nach dem T3-Teilnachweis M1 beginnen: sichtbare unterschiedliche Videoframes, lesbare Aufnahme und eigene native Module aus der einzelnen Release-EXE. M1 ist am 10.10.2026 belegt. Der Rest von T3 bleibt vor jeder Releasefreigabe verpflichtend. Der Acht-Zyklen-Lauf scheiterte nachweislich in WriteState der Testprobe, nicht im Decoder. Buildfenster werden zwischen Bearbeitern abgestimmt.
- Abschnitt C: Die unabhängigen AppCore-Teile von T6, T7 und T8 dürfen nach T4 entstehen. Ihre WinUI-Integration bleibt von T3/T5 abhängig. Der T7-Core verwendet einen Aufnahmevertrag ohne WinUI- oder LibVLC-Abhängigkeit. Änderungen am gemeinsamen Startpfad werden einem Bearbeiter zugeordnet.
- Abschnitt D: T9 integriert alle Ergebnisse. T10 erstellt die endgültige Lieferung und den Nachweis.
- Bei paralleler Arbeit fremde Änderungen erhalten. Keine Agentenzahl oder Aufgabenanzahl als Qualitätsziel vorgeben.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| T1 | keine | T2 | keine |
| T2 | T1 | T3, T4 | keine |
| T3 | T2 | T5, T8 | T4 |
| T4 | T2 | T6, T7, T8 | T3, T5 |
| T5 | T3 | T6, T7 | T4 |
| T6 | T4, T5 | T9 | T7, T8 bei getrennten Dateien |
| T7 | T4, T5 | T9 | T6, T8 bei getrennten Dateien |
| T8 | T3, T4 | T9 | T6, T7; Startpfad allein zuordnen |
| T9 | T6, T7, T8 | T10 | keine |
| T10 | T9 | Freigabenachweis | keine |

## Todos
Jede Aufgabe beginnt mit den zugehörigen Tests. T1/T2 bilden den vorgeschalteten Referenznachweis. Die unten genannten neuen Dateien und Skripte werden erst bei der Umsetzung erstellt.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [x] T1. Befehlsreferenz und Testgrundlage vor jeder Migration erstellen
  - Verantwortung: `CameraControl.Visca.Tests/`, `tests/fixtures/visca/`, `scripts/Test-Offline.ps1`; minimaler interner Testzugang in der Bibliothek.
  - NUnit-Testprojekt für net481 und net10 erstellen. `InternalsVisibleTo` nur für das benannte Testprojekt. Produktionssignaturen und Verhalten unverändert lassen. `IViscaClient` aufzeichnen lassen.
  - Feste Hex-Fixtures für absolute/relative Pan/Tilt-Werte, Vorzeichen, Zoom, Abfragen, alle manuellen Richtungen, Geschwindigkeitsgrenzen, Stopps und Home erstellen. Beispielreferenzen: Home `81 01 06 04 FF`; Zoom-Stopp `81 01 04 07 00 FF`; bisheriger Pan/Tilt-Stopp `81 01 06 01 01 01 03 03 FF`.
  - Antworten unabhängig von Produktionsencoder und Fake festlegen. Positive, Null- und negative Werte einschließlich Randwerten prüfen. Erfolg, Protokollfehler, Gerätefehler und Cancellation als unterschiedliche Ergebnisse prüfen.
  - Quellen: `CameraControl.Visca/IViscaClient.cs:11`, `ViscaController.cs:33`, `ViscaController.cs:96`, `ViscaController.cs:106`, `ViscaController.cs:122`, `ViscaController.cs:136`, `ViscaController.cs:160`, `ViscaPacket.cs`.
  - Abhängigkeit: keine. Sperrt T2. Keine echte Netzwerkverbindung.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite Protocol` führt beide Zielframeworks mit identischen Referenzdatensätzen aus; alle Fälle grün, Testanzahl größer null.
  - QA Erfolg: Fixtures ergeben exakt die erwarteten Bytes und Rückgabewerte. QA Fehler: vorgegebene Gerätefehler und abgebrochene Testantworten werden als Fehler/Abbruch erkannt; sie dürfen keine gültige Position liefern.
  - Belege: `.omo/evidence/winui3-modernisierung/T1/`. Commit: nur auf gesonderten Git-Auftrag, vorgeschlagener Schnitt `test(visca): preserve command contract`.

- [x] T2. TCP- und Bedienreferenz am bestehenden Code sichern
  - Verantwortung: Testserver/Tests, kleine WPF-Testzugänge, `scripts/Test-Offline.ps1 -Suite Legacy`.
  - Lokalen TCP-Testserver mit dynamischem Loopback-Port erstellen. Öffentliche `ViscaController.ForTcp` verwenden. ACK getrennt von Completion prüfen; Fragmentierung, mehrere Antworten, EOF, ungültige Antworten, Timeout, Cancellation und nächsten Verbindungsaufbau testen. Beide bedingten TCP-Kompilierungspfade abdecken.
  - Bestehendes WPF-Fenster für Tests mit injizierbarer Kamera und deaktivierbarem Video konstruierbar machen. Normalen Startpfad dabei nicht umgestalten. Tests erhalten eigene synthetische Einstellungen, keine LAN-Adresse aus `App.config`.
  - Mauspositionen am Steuerfeld einschließlich Mitte, Rändern, außerhalb der Fläche und Loslassen gegen bisherige Richtungen/Geschwindigkeiten prüfen. Positionsaufruf und Home als Befehlsfolgen erfassen. Keine Produktionsmethode lediglich in den Test kopieren.
  - Bekannte Defekte dokumentieren: Konfigurationsschlüssel, blockierende Abfragen, fehlende Freigabe. Diese werden keine Sollanforderungen. Der vorhandene StressTest wird nicht gestartet.
  - Quellen: `TcpViscaClient.cs:53`, `TcpViscaClient.cs:86`, `ViscaClientBase.cs:55`, `ReadBuffer.cs`, `MainWindow.xaml.cs:28`, `MainWindow.xaml.cs:49`, `MainWindow.xaml.cs:83`, `MainWindow.xaml.cs:115`, `MainWindow.xaml.cs:134`, `FakeViscaCamera.cs:110`.
  - Abhängigkeit: T1. Sperrt T3.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite Legacy`. Beide Frameworkpfade und WPF-Bedienreferenz grün. Baseline-Quellen und Fixture-Hashes im Beleg sichern.
  - QA Erfolg: gleiche Eingabe ergibt gleichen vollständigen Befehlsinhalt. QA Fehler: Server schließt während Antwort; Vorgang endet und nächster Auftrag kann neu verbinden. ACK allein beendet den Auftrag nicht.
  - Belege: `.omo/evidence/winui3-modernisierung/T2/`. Ein gezielter Review prüft hier Referenzunabhängigkeit und Testabdeckung. Commit nur auf Git-Auftrag: `test(camera): cover transport and existing controls`.

- [x] T3. WinUI, RTSP, Foto und einzelne Release-EXE technisch nachweisen
  - Verantwortung: minimales `CameraControl.WinUI/`, `scripts/Publish-App.ps1`, `scripts/Test-Video.ps1`, lokale Medienfixtures und dokumentierte Paketversionen.
  - Entwicklungsumgebung read-only prüfen. Fehlende Werkzeuge benennen; den WinUI-Skill für eine autorisierte Einrichtung und das native Template verwenden. Neues Projekt mit .NET 10 und unpackaged erzeugen; keine WPF-Konvertierung durch bloßes Umbenennen von XAML-Typen.
  - LibVLCSharp.WinUI 3.10.1 und VideoLAN.LibVLC.Windows 3.0.24 integrieren. Erst Tests/Prüfskript für Bild, Snapshot und Lebensdauer anlegen, dann den kleinsten Player bauen. SwapChain-Initialisierung aus offiziellem Beispiel verwenden.
  - Release-Eigenschaften: `WindowsPackageType=None`, `SelfContained=true`, `WindowsAppSDKSelfContained=true`, `PublishSingleFile=true`, `IncludeAllContentForSelfExtract=true`, `EnableMsixTooling=true`, RID `win-x64`. Native VLC-Dateien und Plugins vollständig als Bundleinhalt prüfen. Trimming/AOT nicht als Optimierung einführen.
  - MediaMTX an Loopback mit TCP konfigurieren. FFmpeg veröffentlicht einen eigenen H.264-Testclip mit Bewegung und Bildnummer. Bereitstellung der Testwerkzeuge mit festen Versionen und Prüfsummen dokumentieren; keine Abhängigkeit von einer Kamera oder einem öffentlichen Stream.
  - Quellen: Demo.csproj und MainWindow.xaml.cs:36; S1, S4, S5, S6, S7 unten.
  - Abhängigkeit: grüne T1/T2. Sperrt T4/T5/T8.
  - Abnahme: `pwsh -File scripts/Publish-App.ps1 -Runtime win-x64`; anschließend `pwsh -File scripts/Test-Video.ps1 -Scenario All -UsePublishedExe`. Bewegtes Bild innerhalb 10 s im lokalen Test, lesbare PNG mit richtigem Bildinhalt, zehn Öffnen/Aufnehmen/Schließen-Zyklen ohne weitere aktive Medienverbindung.
  - QA Fehler: RTSP-Sender stoppen; Oberfläche bleibt bedienbar. Sender neu starten; erneutes Verbinden liefert wieder Bild. Fehlende native Bibliothek im isolierten Negativpaket erzeugt verständlichen Fehler und keinen falschen Erfolg.
  - Single-file-Ausgabe tatsächlich aus einem anderen Ordner starten. Geladene native Module müssen aus den mitgelieferten Dateien stammen. Erfolg des Builds allein genügt nicht.
  - Wenn ausschließlich der SwapChain-Adapter scheitert: denselben LibVLC-Kern einmal gezielt mit einem HWND-Host prüfen. Bei grundsätzlichem Paket-/Decoderfehler keine stillschweigende Bibliotheks- oder Frameworkumstellung und kein Verschieben des Livebilds aus der Lieferung; technischen Blocker dokumentieren.
  - Belege: `.omo/evidence/winui3-modernisierung/T3/`. Commit nur auf Git-Auftrag: `feat(video): prove packaged RTSP preview and snapshots`.

- [x] T4. Anwendungsmodell und Windows-Persistenz mit Tests erstellen
  - Verantwortung: `CameraControl.AppCore/`, `CameraControl.AppCore.Tests/`, `CameraControl.WinUI/Storage/`.
  - Zuerst Tests für Ersteinrichtung, vier IDs, Wertevalidierung, Speichern/Neuladen, atomaren Bildwechsel, Schreibfehler und getrennte Programmversionen anlegen. Danach das oben definierte Modell und Repository implementieren.
  - Stable Windows Known Folder über einen testbaren Pfadadapter ermitteln. Produktionspfad genau wie oben. Kein Schreiben in App.config, Installations- oder Extraktionsverzeichnis.
  - Drei leere normale Positionen und Home-Fotoeintrag erstellen. Keine Nullkoordinaten als eingerichtete Position behandeln. Kennwörter nur im Windows-Speicher; Credentials nicht in JSON und Fehlermeldungen.
  - Quellen: MainWindow.xaml.cs:160 und :176 nur zur Abgrenzung; Datenvertrag dieses Plans; S2/S3/S8.
  - Abhängigkeit: T2. Parallel zu T3/T5. Sperrt T6/T7/T8. Persistenz ist unabhängig vom Video-/UI-Spike.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite Persistence`.
  - QA Erfolg: neue Werte und PNG erscheinen nach neuem Repository-Prozess unverändert. QA Fehler: Schreiben vor JSON-Austausch scheitert; bisheriger konsistenter Eintrag bleibt lesbar. Unbekannte Schema-Version wird nicht überschrieben. Keine Altdatei wird gesucht.
  - Belege: `.omo/evidence/winui3-modernisierung/T4/`. Commit nur auf Git-Auftrag: `feat(settings): persist fresh per-user configuration`.

- [ ] T5. Kompakte WinUI-Oberfläche und Fensterverhalten erstellen
  - Verantwortung: `DESIGN.md`, `CameraControl.WinUI/Views/`, Theme-Ressourcen, UI-Grundtests.
  - Vor Komponenten die kleine native Gestaltung dokumentieren: Systemschrift, native Brushes, helle/dunkle Darstellung und hoher Kontrast; konsistente kleine Abstände. Keine eigenen dekorativen Kontrollbibliotheken.
  - Drei feste Ziele: Positionen, Steuerung, Einstellungen. Native obere Navigation; keine dauerhaft breite Seitenleiste. Vorschau als globale Aktion. Bei wenig Platz darf Einstellungen als beschriftetes Zahnradsymbol mit Tooltip und AutomationName erscheinen.
  - Vier gleich große Bildtasten im 2x2-Raster. Bilder mit erhaltenem vollständigem Bildausschnitt (`Uniform`) anzeigen; kein Zuschnitt, der eine andere Kamerafahrt suggeriert. Text bleibt außerhalb des Bilds gut lesbar.
  - Startmaß aus physischem Zielmaß und aktueller DPI berechnen; Zielminimum 360 x 280 DIPs. Bei 200 Prozent Mindestmaß bevorzugen. Fensterzustand wiederherstellen und an aktuelle Monitor-Arbeitsfläche anpassen.
  - Einstellungen/Steuerung bei Bedarf vertikal scrollbar; Positionsseite ohne Scrollleiste. Lange deutsche Beschriftungen und Tastaturfokus berücksichtigen.
  - Quellen: MainWindow.xaml für Funktionsumfang; Microsoft WinUI-Skill `shell-navigation-and-windowing.md`, `controls-layout-and-adaptive-ui.md`; Nutzermaße dieses Plans.
  - Abhängigkeit: T3-M1. Parallel zur gezielten T3-Testzugriffskorrektur. Sperrt T6/T7. Vollständige T3-Abnahme bleibt vor Release verpflichtend.
  - Abnahme: `pwsh -File scripts/Test-Desktop.ps1 -Scenario Layout` mit Screenshots bei Ziel-/Minimalmaß und 100/125/150/200 Prozent.
  - QA Erfolg: vier vollständig anklickbare Tasten, lesbare Namen, erreichbare Navigation und Vorschau. QA Fehler: gespeicherte Position liegt auf entferntem Monitor; Fenster erscheint vollständig auf vorhandener Arbeitsfläche. Lange Labels dürfen keine andere Taste verdecken.
  - Belege: `.omo/evidence/winui3-modernisierung/T5/`. Gezielter visueller Review dieses Übergangs. Commit nur auf Git-Auftrag: `feat(ui): add compact camera control shell`.

- [x] T6. Kamerabedienung mit der gesicherten Referenz verbinden
  - Verantwortung: `CameraControl.AppCore/Control/`, WinUI-ViewModels für Positionen/Steuerung, zugehörige Tests.
  - Vor Änderungen die gespeicherten Bedienfälle gegen die neuen Aktionen ausführen. Danach vorhandene VISCA-Bibliothek anbinden und modernes Bibliotheksziel auf net10.0 setzen; netstandard2.0 für die WPF-Referenz erhalten.
  - Fachliche Richtung/Geschwindigkeit erhalten; UI-Ereignisse auf Pointer Capture umstellen. Async-Aufträge abwarten und Fehler anzeigen. Stoppauslöser aus dem Verhaltensvertrag prüfen. Eine Fahrt gleichzeitig; keine verdeckte Warteschlange für schnelle Klicks.
  - Home bleibt exakt der native Befehl. Nicht eingerichtete Positionen erzeugen keine Netzwerkpakete. Bei erneutem Verbinden vorherige Verbindung geordnet freigeben.
  - Quellen: MainWindow.xaml.cs:49-140, ViscaController.cs, TcpSendLock.cs; T1/T2-Fixtures und Verhaltensvertrag.
  - Abhängigkeit: T4/T5. Sperrt T9.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite Legacy`; `pwsh -File scripts/Test-Desktop.ps1 -Scenario Controls`.
  - QA Erfolg: alte und neue Bedienung erzeugen dieselben relevanten Pakete für die Referenzeingaben. QA Fehler: Maus außerhalb loslassen, Fokus wechseln, Verbindung abbrechen; kein hängen gebliebenes UI und kein als erfolgreich gemeldeter fehlgeschlagener Stopp.
  - Belege: `.omo/evidence/winui3-modernisierung/T6/`. Commit nur auf Git-Auftrag: `feat(control): bind WinUI to verified VISCA behavior`.

- [x] T7. Einstellungen, Vorschaufenster und Positionsfotos vollständig integrieren
  - Verantwortung: WinUI-SettingsViewModel, Video-Adapter, PreviewWindow und Bilderzuordnung.
  - Zuerst Ablauftests für Entwurf/Speichern/Abbrechen, Seitenwechsel, Aufnahme, Ersetzen und Streamfehler anlegen. Danach den in Scope definierten Workflow implementieren.
  - Einen Player/eine RTSP-Verbindung besitzen. Beim ersten Foto das Vorschaufenster sichtbar initialisieren; Bild und Werte gemeinsam in den Entwurf übernehmen. Die Aufnahmedatei muss vollständig und decodierbar sein. Bei Fehlschlag den gespeicherten Eintrag unverändert lassen.
  - Quellen: T3-Playernachweis, T4-Datenvertrag, Verhaltensvertrag; S4/S5/S6/S7. Alte WPF-Videoanzeige ist kein zu kopierender Adapter.
  - Abhängigkeit: T4/T5. Sperrt T9.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite Presets`; `pwsh -File scripts/Test-Desktop.ps1 -Scenario PhotosAndPreview`.
  - QA Erfolg: Position wählen, steuern, Foto aufnehmen, speichern, App neu starten; richtige Werte und richtiges Bild derselben ID. Zweiter Vorschau-Klick erzeugt kein zweites Fenster.
  - QA Fehler: Stream fällt bei Aufnahme aus; bisherige Daten bleiben unverändert. Positionswerte ändern sich während Aufnahme; Entwurf wird nicht als konsistentes Paar übernommen. Manuell nur Position ändern entfernt das alte Bild erst beim Speichern.
  - Zehn Vorschauzyklen und Hauptfensterschluss prüfen: keine weitere laufende Playerinstanz oder RTSP-Sitzung; Events nach Dispose verändern keine ViewModels.
  - Belege: `.omo/evidence/winui3-modernisierung/T7/`. Commit nur auf Git-Auftrag: `feat(presets): integrate preview and persistent photos`.

- [x] T8. Lokale Einrichtung und Versionswechsel ohne Assistent implementieren
  - Verantwortung: `CameraControl.WinUI/Deployment/`, Startpfad, Shell-Link-Adapter und zugehörige Tests. Startpfad nur einem Bearbeiter zuordnen.
  - Zuerst Tests mit isolierten Pfaden für Erststart, gleiche/neue/alte Version, zwei parallele Starts, Schreibfehler und abgebrochenen Versionswechsel schreiben. Danach den oben beschriebenen Ablauf implementieren.
  - Komplette lokale EXE-Kopie prüfen, geordneten Instanzwechsel durchführen, erfolgreich gestartete Version aktivieren und gleichnamigen Startmenülink umstellen. Ablaufzustände explizit und klein halten. Kein allgemeines Updatesystem bauen.
  - Entwicklungsstarts dürfen keine reguläre lokale Einrichtung verändern. QA-Builds/Tests benutzen einen eindeutig eigenen Testproduktnamen und isolierte Pfade. Produktion verwendet nur die festen bekannten Benutzerpfade.
  - Quellen: Lokaler Einrichtungsvertrag; S1/S2/S3. `AppInstance`-/Single-instance-Muster aus Microsoft WindowsAppSDK-Samples verwenden, unter Berücksichtigung des Starts einer neuen EXE vor Weiterleitung.
  - Abhängigkeit: T3/T4. Sperrt T9.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite Deployment`; `pwsh -File scripts/Test-Release.ps1 -Scenario LocalUpdate`.
  - QA Erfolg: Version A aus Test-Downloads starten, dann B; Downloadquellen entfernen; Shell-Link startet B, Daten sind identisch. A erneut starten darf B nicht ersetzen.
  - QA Fehler: B scheitert am Start oder Linkwechsel; A bleibt aktivierbar. Zwei B-Starts erzeugen eine aktive Instanz. Alte App reagiert nicht auf geordnetes Schließen; keine Zwangsbeendigung und kein Aktivierungswechsel.
  - Belege: `.omo/evidence/winui3-modernisierung/T8/`. Ein gezielter Review prüft Schreibpfade, Fehlerzustände und Versionsentscheidung. Commit nur auf Git-Auftrag: `feat(deploy): add per-user first-run setup and updates`.

- [ ] T9. Vollständige Abläufe mit ausgelieferter EXE offline prüfen
  - Verantwortung: Integrationstests, `scripts/Test-Desktop.ps1`, `scripts/Test-Release.ps1`, Belegmatrix.
  - Tests zuerst als konkrete Szenarien vervollständigen. Testserver laufen nur auf Loopback. Kameraadresse aus Testfixture konfigurieren; danach reguläre Oberfläche bedienen.
  - Abhängigkeit: T6/T7/T8. Sperrt T10.
  - Quellen: alle Verhaltensverträge und T1/T2-Referenzen; Microsoft WinUI-Build-/Launch-Prüfregeln; S1/S7.
  - Abnahme: `pwsh -File scripts/Test-Offline.ps1 -Suite All`; `pwsh -File scripts/Test-Desktop.ps1 -Scenario All`; `pwsh -File scripts/Test-Release.ps1 -Scenario All`.
  - QA Erfolg: Ersteinrichtung, drei Positionen plus Home, manuelle Bedienung, Foto, separater Preview, Neustart und Update in einem reproduzierbaren Ablauf. Die Betriebssystemintegration muss den tatsächlich installierten Testprozess starten, nicht einen Buildordner.
  - QA Fehler: Kameraantwort fehlt, Video stoppt, Datenordner ist schreibgeschützt, fehlerhafte URL, altes EXE-Paket und Updateabbruch. Keine Szenarien automatisch überspringen.
  - Release auf sauberem Windows 11 ohne separate Runtimes/VLC prüfen. App-Suche liefert nach Shell-Registrierung den vorgesehenen Eintrag; gestartete Prozessversion und absoluter Pfad müssen der aktivierten lokalen Version entsprechen.
  - Belege: `.omo/evidence/winui3-modernisierung/T9/`. Fehlende isolierte Windows-Umgebung ist ein offener Release-Nachweis, kein bestandener Test. Commit nur auf Git-Auftrag: `test(app): verify complete offline release workflow`.

- [ ] T10. Lieferung, kurze Bedienanleitung und nachvollziehbare Freigabe erstellen
  - Verantwortung: README, neue kurze Bedien-/Release-Dokumentation, versioniertes Publish-Skript und finales Paket.
  - Nur nach grüner T9 endgültige Release-EXE erzeugen. Paketversion, Prüfsumme, enthaltene native Komponenten und Lizenzen erfassen. Fehlende ursprüngliche Lizenzdatei anhand der Quellherkunft klären; Copyright-Hinweise erhalten.
  - Dokumentieren: EXE einmal starten, neue Version ebenso starten, Datenpfad, Verbindung einrichten, Foto zuordnen, Vorschaufenster. Kein Sicherungs-, Altimport- oder manuelles Runtime-Verfahren ergänzen.
  - Abhängigkeit: T9.
  - Quellen: README des Repositories und alle Prüfergebnisse; Drittanbieter-NuGet-Metadaten. Keine unbelegte Aussage zur echten Kamera.
  - Abnahme: finales Artefakt erneut mit `pwsh -File scripts/Test-Release.ps1 -Scenario Smoke` starten; Hash des geprüften Artefakts stimmt mit Lieferdatei überein. Anleitung beschreibt den geprüften Ablauf.
  - QA Fehler: fehlende Lizenz-/Native-Datei oder unpassender Artefakthash verhindert die Releasefreigabe. Kein Erfolg auf Grundlage eines anderen Debug-Builds.
  - Belege: `.omo/evidence/winui3-modernisierung/T10/`. Commit nur auf Git-Auftrag: `docs(release): document verified Windows delivery`.

## Final verification wave
Eine abschließende strukturierte Prüfung durch den Verantwortlichen. Kein zusätzlicher Agentenschwarm, keine pauschale Wiederholung aller bereits gültigen Tests. Neue Änderungen oder Fehlschläge erfordern nur die betroffenen Wiederholungen.
- [ ] F1. Plan compliance audit: Alle Anforderungen gegen T1-T10 und Belege abgleichen. Ungeprüfte Hardwareeigenschaften ausdrücklich offen nennen.
- [x] F2. Code quality review: Geänderter Diff, Async-/Dispose-Verhalten, Dateipfade und Versionswechsel gezielt prüfen. Keine willkürliche LOC-Vorgabe.
- [ ] F3. Real manual QA: Tatsächliche Windows-Oberfläche, Bilder, Video und Shell-Start wurden mit der finalen EXE bedient und beobachtet. Compiler-/Testausgabe allein reicht nicht.
- [x] F4. Scope fidelity: Keine Sicherung, keine Legacy-Übernahme, kein Internet-Updater und keine versehentliche Änderung der VISCA-Befehle.

## Commit strategy
Während dieser Planung keine Git-Änderung außer den Planartefakten. Bei Umsetzung nur auf einen gesonderten Git-Auftrag committen. Sinnvolle atomare Schnitte stehen je Aufgabe. Unabhängige Benutzeränderungen nie aufnehmen oder zurücksetzen. Bestehenden WPF-Code bis zur verifizierten Ablösung erhalten.

## Success criteria
- [ ] Alle Referenztests existierten vor der Migration und laufen danach unverändert für beide Bibliothekspfade erfolgreich.
- [ ] Windows-11-Anwendung mit vier großen Bildtasten, manueller Steuerung, Einstellungen und separater integrierter Vorschau vollständig nutzbar.
- [ ] Fotos stammen aus dem integrierten Player und bleiben zusammen mit den zugeordneten neuen Einstellungen nach Neustart und Versionswechsel erhalten.
- [ ] Neue EXE startet lokale Einrichtung/Update; Windows-Suche öffnet anschließend die aktivierte Version aus dem festen Benutzerordner, auch wenn Downloads entfernt wurden.
- [ ] Kein separater Runtime-/VLC-Installationsschritt. Keine Altübernahme, keine Sicherungsfunktion und kein Internet-Updater.
- [ ] Testfälle für Abbruch, Fehler und Updateversagen liefern die festgelegten Ergebnisse. Tatsächliche Release-EXE und Native-Dateien sind geprüft.
- [ ] Jede Aussage zur Freigabe nennt die vorhandenen Belege und die Grenze: unbekannte echte Kamera nicht geprüft.

### Primärquellen für die Umsetzung
- S1: Microsoft, unpackaged WinUI und einzelne EXE: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
- S2: Microsoft, Shell-Verknüpfungen: https://learn.microsoft.com/en-us/windows/win32/shell/links
- S3: Microsoft, Known Folders: https://learn.microsoft.com/en-us/windows/win32/shell/knownfolderid
- S4: Offizielles WinUI-Videopaket und Frameworks: https://www.nuget.org/packages/LibVLCSharp.WinUI/3.10.1
- S5: Native VLC-Abhängigkeit: https://www.nuget.org/packages/VideoLAN.LibVLC.Windows/3.0.24
- S6: VideoLAN, WinUI-Initialisierung und Player-API: https://github.com/videolan/libvlcsharp/blob/3.x/src/LibVLCSharp.WinUI/README.md und https://docs.videolan.me/libvlcsharp/api/LibVLCSharp.Shared.MediaPlayer.html
- S7: MediaMTX, lokale Installation, FFmpeg und Konfiguration: https://mediamtx.org/docs/kickoff/install ; https://mediamtx.org/docs/publish/ffmpeg ; https://mediamtx.org/docs/references/configuration-file
- S8: .NET, Windows-Sonderordner: https://learn.microsoft.com/en-us/dotnet/api/system.environment.specialfolder
- S9: .NET-Support: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core
- S10: Windows-App-SDK-Support und Stable-Kanal: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/release-channels
- S11: Bisheriger WebEye-WPF-Player: https://www.nuget.org/packages/WebEye.StreamControl.Wpf/1.0.3 und https://github.com/jacobbo/WebEye
- S12: Windows-UI-Automation: https://github.com/FlaUI/FlaUI ; Testframework: https://docs.nunit.org/articles/nunit/intro.html
- S13: Windows-Anmeldedaten im Benutzerkonto: https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credwritew

Die Quellen wurden für diesen Plan gelesen bzw. mit dem Repository abgeglichen. Verfügbarkeit von APIs/Paketen ist belegt; ihre erfolgreiche Ausführung in dieser Anwendung ist erst Gegenstand der Umsetzung und der oben genannten Tests.
