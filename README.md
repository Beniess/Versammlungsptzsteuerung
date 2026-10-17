# Versammlungs-Kamerasteuerung

Eine Windows-App zum Schwenken, Neigen und Zoomen einer PTZ-Kamera.
Drei Bildtasten rufen gespeicherte Ansichten auf, zum Beispiel Leser, Tisch und Redner.
Eine vierte Taste fährt die Kamera in ihre Home-Position.
Ein Livebild hilft beim Ausrichten.

**[EXE herunterladen](https://github.com/Beniess/Versammlungsptzsteuerung/releases/download/v1.0.0.7/CameraControl.WinUI.exe)** · [Alle Dateien zu Version 1.0.0.7](https://github.com/Beniess/Versammlungsptzsteuerung/releases/tag/v1.0.0.7)

## Voraussetzungen

- Windows 11 auf einem x64-PC.
- Eine über das Netzwerk erreichbare Kamera mit VISCA über TCP.
- Für Livebild und Fotos: ein RTSP-Stream der Kamera.

Die EXE enthält .NET und VLC. Eine separate Installation dieser Komponenten ist nicht erforderlich.
VISCA-Port und RTSP-Pfad hängen von der Kamera ab.

## Starten und verbinden

1. `CameraControl.WinUI.exe` herunterladen und öffnen.
2. In der App **Einstellungen** öffnen.
3. Die IP-Adresse oder den Hostnamen der Kamera und ihren **VISCA-Port** eintragen.
4. Für das Livebild die vollständige **RTSP-URL** eintragen. Benutzername und Passwort bei Bedarf in die getrennten Zugangsdatenfelder eingeben.
5. **Speichern und verbinden** wählen.

Beim ersten Start richtet die App einen Eintrag im Startmenü ein.
Danach kann sie über **Versammlungs-Kamerasteuerung** im Startmenü oder über die Windows-Suche geöffnet werden.

## Kamera bedienen

- **Ansicht aufrufen:** Eine eingerichtete Bildtaste wählen.
- **Kamera ausrichten:** Das Schwenk-/Neigefeld oder das Zoomfeld gedrückt halten. Loslassen beendet die Bewegung.
- **Ansicht speichern:** In der Steuerung die gewünschte Ansicht wählen, die Kamera ausrichten und **Foto** wählen. Mit **Speichern** werden das Bild und die Kameraposition gemeinsam gespeichert.
- **Livebild öffnen:** **Vorschau** wählen.
- **Home:** Sendet den Home-Befehl der Kamera. Diese Taste speichert keine eigene Zielposition.

Die drei normalen Ansichten sind anfangs leer. Ihre Namen können in den Einstellungen geändert werden.
Die [Bedienungsanleitung](docs/bedienung.md) erklärt Tastatursteuerung, Fotos und Versionswechsel.

## Stand der Version

Version **1.0.0.7** ist eine Vorabversion.
333 Offline-Tests und 108 Windows-Adaptertests sind bestanden.
Der Betrieb mit einer realen Kamera, höhere Anzeigeskalierungen und Hochkontrast sind noch nicht geprüft.
Prüfergebnisse und offene Anforderungen stehen im [Freigabestand](docs/freigabestand.md).

## Technik und Entwicklung

Die Anwendung verwendet WinUI 3, .NET 10 und LibVLC.
Die Steuerung sendet VISCA-Befehle über TCP. Das Livebild verwendet RTSP.
Einstellungen und Fotos liegen unter `%LOCALAPPDATA%\Versammlungsptzsteuerung\Data`.
Passwörter liegen in der Windows-Zugangsdatenablage.

Für die aktuelle App das Projekt `CameraControl.WinUI/CameraControl.WinUI.csproj` öffnen.
Die Solution enthält bisher die älteren WPF-Projekte.

- [Architektur und Entwicklung](docs/architektur.md)
- [Build und Veröffentlichung](docs/github-publish.md)
- [Drittsoftware und Lizenztexte](LICENSES/README.md)
- [Herkunft des Projekts](docs/importstand-2026-10-10.md)
