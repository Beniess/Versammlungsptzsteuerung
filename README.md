# Versammlungsptzsteuerung

Native PTZ-Kamerasteuerung für Windows 11 x64. Die neue App verwendet WinUI 3,
.NET 10 und die vorhandene VISCA-Bibliothek. Vier Bildtasten rufen Leser, Tisch,
Redner und Home auf. Steuerung, Einstellungen und Vorschau ergänzen die
Positionsseite.

Der aktuelle Stand ist ein Entwicklungs- und QA-Stand. Die Oberfläche ist
sichtbar nutzbar. Die vollständige Release-Prüfung ist noch offen. Der Betrieb
mit einer realen Kamera wurde noch nicht geprüft.

- [Bedienung, Erststart und Versionswechsel](docs/bedienung.md)
- [Aufbau und Entwicklungsablauf](docs/architektur.md)
- [GitHub-Build und Veröffentlichung](docs/github-publish.md)
- [Offene Freigabeprüfungen](docs/freigabestand.md)
- [Historischer Importstand](docs/importstand-2026-10-10.md)

## Projektaufbau

| Projekt | Aufgabe | Zielplattform |
| --- | --- | --- |
| `CameraControl.WinUI` | Neue native Oberfläche, Vorschau und lokaler EXE-Start | WinUI 3 / .NET 10 / Windows 11 x64 |
| `CameraControl.AppCore` | Einstellungen, Kamerasitzung, Positionsentwürfe und Versionswechsel | .NET 10 |
| `CameraControl.Visca` | VISCA über TCP und Kamerabefehle | .NET Standard 2.0 / .NET 10 |
| `CameraControl.WinUI.Tests` | Native UIA- und Adapterprüfungen | .NET 10 / Windows |
| `CameraControl.AppCore.Tests` und `CameraControl.Visca.Tests` | Speicher-, Ablauf- und Protokollprüfungen | Ziele der jeweiligen Testprojekte |
| `CameraControl.Visca.Demo` | WPF-Referenz aus dem Import | .NET Framework 4.8.1 |
| `CameraControl.Visca.StressTest` | Historischer Stresstest mit realer Kameraadresse | .NET 6 |

Die Solution `Versammlungsptzsteuerung.sln` enthält bisher die historischen
Projekte. Für die neue Anwendung das Projekt
`CameraControl.WinUI/CameraControl.WinUI.csproj` öffnen. Die WPF-Referenz und der
Stresstest sind keine Startanleitung und keine Freigabe der neuen WinUI-App.

## Entwicklung

Die neue App wird mit dem .NET-10-SDK und den Windows-Buildwerkzeugen gebaut.
Paketversionen stehen in den Projekt- und Lockdateien. Der Release-Build erzeugt
eine selbständige Datei `CameraControl.WinUI.exe`. Die Laufzeit und die nativen
Videodateien gehören zum Bundle. Der vollständige Nachweis auf einem sauberen
Windows-11-System ist noch Teil der Release-Prüfung.

Die WinUI-App übernimmt keine alten Werte aus `App.config`. Neue Daten liegen
unter `%LOCALAPPDATA%\Versammlungsptzsteuerung\Data`. Host, VISCA-Port und
vollständige RTSP-URL werden getrennt eingerichtet. Ein frischer Datenordner hat
keinen Host, keinen Videostream und keine eingerichteten normalen Positionen.

Entwicklungs- und QA-Starts verwenden eigene Datenpfade. Im QA-Modus sind nur
Loopback-Endpunkte erlaubt. Die aktuelle sichtbare QA-Instanz ist deshalb kein
Nachweis für eine reale Kameraverbindung.

## Historie und Lizenzunterlagen

Der ursprüngliche Import erfolgte am 10.10.2026 aus
`DemoCode-CameraControl-2.zip`. Der Archivnachweis und das vollständige damalige
README bleiben im [Importstand](docs/importstand-2026-10-10.md) erhalten.

Das Lizenzquelleninventar und die Drittsoftware-Unterlagen werden getrennt
erstellt. Diese README erklärt keine abgeschlossene Lizenz- oder
Release-Freigabe.
