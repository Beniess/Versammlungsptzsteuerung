# Versammlungsptzsteuerung

PTZ-Kamerasteuerung fuer Versammlungen. Dieses Repository ist die Ausgangsbasis
fuer die spaetere Weiterentwicklung des importierten CameraControl-Projekts.

## Projektaufbau

| Projekt | Aufgabe | Zielplattform |
| --- | --- | --- |
| `CameraControl.Visca` | VISCA ueber TCP, Kamerabefehle und simulierte Kamera | .NET Standard 2.0 / .NET 6 |
| `CameraControl.Visca.Demo` | Windows-Oberflaeche mit Pan/Tilt/Zoom, Positionen, Einstellungen und RTSP-Video | WPF / .NET Framework 4.8.1 |
| `CameraControl.Visca.StressTest` | Gleichzeitige Abfragen an eine echte Kamera | .NET 6 |

Die Solution heisst `Versammlungsptzsteuerung.sln`. Die bestehenden Projekt-
und Namespace-Namen wurden beim Import beibehalten.

## Entwicklung

Die Desktop-Anwendung benoetigt Windows und passende .NET-/WPF-Buildwerkzeuge
fuer .NET Framework 4.8.1. Die Solution kann dort in Visual Studio geoeffnet
werden. NuGet-Abhaengigkeiten sind `Microsoft.Extensions.Logging.Abstractions`
6.0.0 und `WebEye.StreamControl.Wpf` 1.0.3.

Kameraadresse, Port und Positionen stehen in
`CameraControl.Visca.Demo/App.config`. Die Anwendung verbindet sich beim Start
mit der konfigurierten Kamera und startet den Videostream. Der Stresstest hat
eine eigene, fest eingetragene Kameraadresse in `Program.cs` und ist kein
automatisierter Unit-Test. `FakeViscaCamera` ist vorhanden, aber bisher nicht
mit dem Start der Desktop-Anwendung verbunden.

## Importstand

- Importiert am 10.10.2026 aus `DemoCode-CameraControl-2.zip`.
- Alle 566 entpackten Dateien wurden bytegenau gegen das Archiv geprueft.
- Quellcode und Projektdateien sind versioniert; `.vs`, `bin` und `obj` bleiben
  lokal und werden durch `.gitignore` ausgeschlossen.
- Beim Import wurden keine Funktionen erweitert. Build und Kamerabetrieb
  wurden nicht getestet; auf dem Import-Mac war `dotnet` nicht im PATH.
- Die vorhandenen Positionsschluessel in `App.config` stimmen teilweise nicht
  mit den Schluesseln im Anwendungscode ueberein. VISCA und RTSP verwenden im
  Anwendungscode denselben Port. Diese Punkte sind vor dem Kameratest zu pruefen.
- Die urspruenglichen Quellcode-Header nennen Jon Skeet und Apache License 2.0.
  Die dort referenzierte `LICENSE.txt` ist im gelieferten Archiv nicht enthalten.

SHA-256 des Originalarchivs:
`441a556c58a3074bc9dbf0217cde5f8ad1cdcda96de24e0ac4ecec61eb0a0832`
