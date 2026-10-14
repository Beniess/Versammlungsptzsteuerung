# Aufbau und Entwicklung

Stand: 10.10.2026. Die neue Anwendung ist eine native WinUI-3-App für Windows 11 x64. Die WPF-App bleibt als importierte Referenz erhalten. Sie wird nicht durch Umbenennen von XAML-Typen zur neuen App.

## Zuständigkeiten

| Bereich | Aufgabe |
| --- | --- |
| `CameraControl.WinUI/MainWindow` | Kompakte Shell, obere Navigation, globale Vorschauaktion und Hauptfensterzustand |
| `CameraControl.WinUI/Views` | Vier Bildtasten, Pointer-Steuerfelder und native Einstellungsformulare |
| `CameraControl.WinUI/ViewModels` | Anzeigezustand, Bedienfreigabe und UI-Fehleranzeige |
| `CameraControl.WinUI/ApplicationServices` | Gemeinsamer Lebenszyklus, eigene Aufgaben, QA-Bereich und gemeinsame Schreibsperre |
| `CameraControl.WinUI/Video` und `PreviewWindow` | Sichtbarer RTSP-Player, PNG-Decodierung und Windows-Zugangsdatenadapter |
| `CameraControl.WinUI/Startup` und `Deployment` | EXE-Start, lokale Instanzaktivierung, Versionswechsel und Windows-Verknüpfung |
| `CameraControl.AppCore/Storage` | Typisierte JSON-Daten und atomare JSON-/PNG-Speicherung |
| `CameraControl.AppCore/Control` | VISCA-Sitzung, Zielaufruf und begrenzte manuelle Aufträge |
| `CameraControl.AppCore/Presets` | Vier unabhängige Entwürfe, Fotoablauf, Speichern und Abbrechen |
| `CameraControl.AppCore/Deployment` | Staging, Aktivierung, Wiederherstellung und Versionsvergleich |
| `CameraControl.Visca` | Bestehende TCP-/VISCA-Befehle und Antwortverarbeitung |

Die WinUI-Oberfläche verwendet native Steuerelemente und Systemressourcen. Der Gestaltungsvertrag steht in [DESIGN.md](../DESIGN.md). Fotos verwenden `Uniform`; Text steht außerhalb des Bilds. Steuerung und Einstellungen besitzen je eine vertikale Scrollfläche. Die Positionsseite hat keine Scrollleiste.

## Lebenszyklus und Daten

Ein neuer Startzustand liest zuerst die Einstellungen. Ein Versionskandidat darf vor der Aktivierung weder Einstellungen schreiben noch Kameraaufträge ausführen. Die Bedienfreigabe folgt erst nach dem erfolgreichen lokalen Startvertrag. Kamera- und Videoerreichbarkeit sind getrennte Betriebszustände.

Die App besitzt eine Kamerasitzung und einen Vorschau-Player. Die Kamerasitzung verarbeitet höchstens einen Positionsauftrag gleichzeitig. Manuelle Eingaben ersetzen ausstehende Zustände derselben Achsengruppe. Ein bestätigter Stopp wird abgewartet.

Alle Einstellungsänderungen teilen eine App-Schreibsperre. Sie umfasst Laden, Ändern, Speichern und Aktualisieren des aktiven Modells. Verbindung, Fensterzustand und Positionsspeicherung können deshalb nicht getrennte veraltete Modelle speichern. Ein Positionsentwurf bleibt bis zum erfolgreichen Speichern vom aktiven Datensatz getrennt.

Beim Schließen werden Eingaben beendet. Laufende eigene Aufgaben werden abgebrochen und abgewartet. Vorschau und Kamera werden anschließend freigegeben. Bildladevorgänge verwenden eine Revision und prüfen den Seiten- und Schließzustand vor einer späten UI-Übernahme.

## Build

Die neue App benötigt für die Entwicklung den .NET-10-SDK sowie die Windows-Buildwerkzeuge. Das moderne Projekt ist `CameraControl.WinUI/CameraControl.WinUI.csproj`. Es wird direkt geöffnet; die bestehende `.sln` enthält bisher die historischen Projekte. Das WinUI-Projekt enthält die festen Paketversionen. `packages.lock.json` sperrt den Paketbestand. Der normale Release-Build wird aus dem Repository mit PowerShell ausgeführt:

```powershell
pwsh -NoProfile -File scripts/Publish-App.ps1 -Runtime win-x64
```

Die Ausgabe liegt unter `artifacts/win-x64/CameraControl.WinUI.exe`. Das Skript prüft, dass die Ausgabe nur diese EXE enthält. Es setzt eine numerische vierteilige Dateiversion. Ein erfolgreicher Publish ist keine Release-Freigabe.

Entwicklungs- und UI-QA-Starts verwenden einen eigenen temporären Datenpfad. Lokale Release-QA verwendet einen ausdrücklich isolierten Programm-, Daten- und Menübereich. Der Produktdatenpfad ist für QA ausgeschlossen. Eine QA-Verbindung darf nur Loopback verwenden.

## Prüfungen

Die Prüfbefehle sind von der normalen Bedienung getrennt:

| Befehl | Zweck |
| --- | --- |
| `scripts/Test-Offline.ps1` | VISCA-Referenz, Speicher, Kamerasitzung und Positionsabläufe |
| `scripts/Test-Video.ps1` | Eigene lokale RTSP-Fixture, sichtbare Frames, Snapshot und Player-Lebensdauer |
| `scripts/Test-Desktop.ps1` | Native Shell-, Steuerungs- und Vorschaufälle |
| `scripts/Test-Release.ps1` | Lokaler EXE-Erststart, Instanzaktivierung und Versionswechsel |
| `CameraControl.WinUI.Tests/NativeRunner/NativeRunner.csproj` | Quelle der selbständigen nativen Prüfung im Sandbox-Gast ohne gesonderten SDK |

Native visuelle Belege verwenden das tatsächlich laufende Fenster. `PrintWindow` erfasst das eigene HWND. PNGs und UIA-Grenzen ersetzen keine echte DPI-Messung. Die tatsächliche Fenster-DPI wird mit `GetDpiForWindow` erfasst. Ein vergrößertes Bild oder ein reiner Policy-Unit-Test gilt nicht als geprüfte Skalierungsstufe.

Die QA verwendet eigene Prozesse, Datenpfade, Bundlecaches und Loopback-Ports. Cleanup-Belege nennen diese Ressourcen. Es wird keine reale Kameraadresse aus dem WPF-Bestand verwendet. Host- und Gast-Eingaben werden seriell abgestimmt.

Die Belege stehen unter `.omo/evidence/winui3-modernisierung/`. Der [Freigabestand](freigabestand.md) bleibt maßgebend. Keine Prüfung darf wegen eines fehlenden Pflichtbelegs als bestanden gelten.
