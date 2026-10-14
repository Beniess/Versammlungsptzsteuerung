---
slug: winui3-modernisierung
status: execution-active
intent: clear
pending-action: T1 and T2 baseline verification before WinUI migration
approach: Tests vor Migration; WinUI 3; lokale Einrichtung durch Start einer neuen EXE; native Windows-Datenpfade; integrierte RTSP-Vorschau und Fotos.
---

# Draft: winui3-modernisierung

## Components (topology ledger)
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
<!-- id | outcome (one line) | status: active|deferred | evidence path -->
| ID | Ergebnis | Status | Beleg |
| --- | --- | --- | --- |
| K1 | Bestehende VISCA-Befehle und Bedienabbildung durch feste Referenztests sichern | active | CameraControl.Visca/ViscaController.cs; CameraControl.Visca.Demo/MainWindow.xaml.cs |
| K2 | Kleines WinUI-Hauptfenster mit vier Bildtasten und drei Seiten | active | Benutzeranforderung |
| K3 | Windows-Benutzerdaten unabhängig vom Programm speichern | active | Benutzerkorrektur: keine Sicherung und keine Altübernahme |
| K4 | Integrierte RTSP-Vorschau und Aufnahme von Positionsbildern | active | CameraControl.Visca.Demo/MainWindow.xaml.cs:36; Demo.csproj |
| K5 | Neue EXE starten; lokale Einrichtung, Startmenü und Versionswechsel automatisch | active | Benutzerantwort vom 10.10.2026 |

## Verbindliche Benutzervorgaben
- Nur planen. Noch keine Produkt- oder Testimplementierung.
- Windows 11; 1920 x 1080; Hauptfenster ungefähr ein Achtel der Fläche und weiter verkleinerbar.
- Vier große Bildtasten. Standardansicht: Positionen. Weitere Seiten: Steuerung und Einstellungen.
- Vorschau in einem eigenen Fenster. Kein externer Player als Betriebsanforderung.
- Kamera ist nicht verfügbar. Zuerst Referenztests am bisherigen Code; diese sind maßgeblich für die Migration.
- Keine Sicherungsfunktion, kein Export/Import und keine Übernahme aus App.config. Benutzer richtet alles neu ein.
- Benutzer wählte ausdrücklich: Neue EXE selbst starten; Einrichtung und Versionswechsel automatisch.
- Kein Internet-Updater, kein Installationsassistent und keine manuelle Runtime-Installation im Zielablauf.
- Keine willkürlichen Dateilängengrenzen. Ein gezielter Planreview; keine Review-Schleifen oder Agentenschwärme.

## Open assumptions (announced defaults)
<!-- Record any default you adopt instead of asking, so the user can veto it at the gate. -->
<!-- assumption | adopted default | rationale | reversible? -->
- Zielarchitektur zunächst Windows x64. ARM64 ist kein angeforderter Lieferumfang.
- Vier feste IDs: leser, tisch, redner, home. Die drei normalen Namen sind editierbar. So blockieren ungeklärte Beschriftungen die Umsetzung nicht.
- Home bleibt der vorhandene native VISCA-Befehl. Ein zusätzliches Zoom-Kommando wäre eine gesonderte Änderung. Die reale Gesamtansicht ist ohne Kamera nicht belegbar.
- UI-Entwurfsmaß: 480 x 344 DIPs bei 125 Prozent entspricht 600 x 430 physischen Pixeln; Zielminimum 360 x 280 DIPs. Tatsächliche Minimalmaße durch WinUI-QA bestätigen, nicht durch Abschneiden erzwingen.
- Kamera und Video getrennt konfigurieren: Host und VISCA-Port sowie vollständige RTSP-URL. Kein automatischer Zugriff auf eine Adresse aus dem alten Repository.
- Audio der Vorschau ausgeschaltet. Video nur bei offener Vorschau oder Fotoaufnahme aktiv.
- .NET 10 LTS und unterstützter Windows-App-SDK-Stable-Zweig; genaue NuGet-Versionen beim ersten technischen Nachweis sperren.

## Findings (cited - path:lines)
- CameraControl.Visca.Demo/CameraControl.Visca.Demo.csproj: WPF/net481; WebEye.StreamControl.Wpf 1.0.3.
- CameraControl.Visca/CameraControl.Visca.csproj: netstandard2.0 und net6.0; keine WPF-Abhängigkeit.
- MainWindow.xaml.cs:28-46 startet VISCA und RTSP aus dem Fensterkonstruktor. RTSP und VISCA verwenden denselben Port.
- MainWindow.xaml.cs:83-140 enthält Richtung, Geschwindigkeitsabbildung, Stopp und Positionen mit Geschwindigkeiten 12/12.
- MainWindow.xaml.cs:160-222 schreibt App.config und blockiert Positionsabfragen mit Result. Keine Übernahme dieser Speicherlösung.
- ViscaController.cs:33 erlaubt internen Testclient; :40 öffentliche ForTcp-API für Loopback-Tests.
- ViscaClientBase.cs:55-101 verarbeitet ACK, Completion, Fehler und Disconnect.
- TcpViscaClient.cs:53 hat getrennte Abbruchpfade für netstandard2.0 und NET5_0_OR_GREATER.
- FakeViscaCamera.cs:234 setzt bei Home zusätzlich Zoom zurück. Fake umgeht ViscaClientBase; daher kein alleiniger Protokollnachweis.
- Kein Snapshot-Verfahren im bisherigen Anwendungscode gefunden.
- Microsoft dokumentiert unpackaged/self-contained WinUI und Single-file-EXE mit Extraktion: https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/unpackage-winui-app
- Windows-Shell-Verknüpfungen: https://learn.microsoft.com/en-us/windows/win32/shell/links
- LibVLCSharp.WinUI 3.10.1 enthält ein net10-Windows-Ziel: https://www.nuget.org/packages/LibVLCSharp.WinUI/3.10.1

## Decisions (with rationale)
- Benutzerantwort ist die Freigabe für den gewählten lokalen Bereitstellungsablauf. Keine erneute Bestätigung zur Planerstellung erforderlich.
- Programm unter einem festen Windows-Benutzerpfad einrichten; nie Startmenü auf Downloads oder den Extraktionscache richten.
- Benutzerstart einer neuen EXE ist der einzige Update-Auslöser. Ältere EXE darf keinen stillen Downgrade durchführen.
- Ein einzelner lokaler Versionswechsel mit sicherer Aktivierung. Kein allgemeiner Update-Server, Dienst oder Hintergrundmonitor.
- Native Windows-Known-Folder-API für einen stabilen Datenpfad; JSON für kleine typisierte Einstellungen, PNG für Bilder. Keine Daten im Programm- oder Downloadordner.
- Vorhandene Kamerabibliothek behalten. Nur Testzugänge, Zielframework-Anpassung und gesondert belegte Korrekturen zulassen.
- Referenztests prüfen alte und neue Kompilierungspfade. Tests für neue Funktionen entstehen vor ihrer Implementierung.
- Video-Port ist echte Implementierungsarbeit: Der vorhandene WPF-Control ist nicht als WinUI-Control verwendbar.

## Scope IN
- Kompakte native Oberfläche, Einstellungen, Positionsbilder, Vorschaufenster, lokale Einrichtung, Referenztests und Offline-Integrationstests.

## Scope OUT (Must NOT have)
- Altübernahme, Sicherung, Export/Import, Datenbank, Cloud, Telemetrie, Internet-Updater, Kamerasuche, Mehrkamerabetrieb, Geräte-Preset-Protokollwechsel.
- MSIX, Store-Veröffentlichung und Paketidentität sind für den beschlossenen EXE-Ablauf nicht erforderlich.
- Keine Kamera- oder Echtzeitgarantie aus Simulatorergebnissen ableiten.

## Open questions
- Kein Produktentscheid blockiert den Plan. Namen sind editierbar. Kameramodell, tatsächlicher Streamcodec und Home-Bildausschnitt bleiben externe Fakten, die ohne Gerät nicht geprüft werden können.
- Technischer Nachweis für native Videobibliotheken im ausgelieferten EXE-Paket erforderlich. Er ist eine frühe Abnahmebedingung, kein Grund, Livebild zu verschieben.

## Gezielter Planreview und Abschluss
- Ein Metis-Review am 10.10.2026 hat den Anforderungsentwurf geprüft. Keine Widersprüche gemeldet. Keine weiteren Reviewläufe beauftragt.
- Geforderte Präzisierungen wurden in den Plan übernommen: feste VISCA-/Mausreferenzmatrix; Installationszustände und Fehlerbehandlung; konkreter Datenvertrag; NUnit-Testprojekte und Prüfskripte; TCP-Loopback statt alleiniger Fake; RTSP-/Snapshot-/Release-Abnahme; durchgehender Start-/Updatefall.
- Videoentscheidung: LibVLCSharp.WinUI 3.10.1 plus VideoLAN.LibVLC.Windows 3.0.24. Native Bibliotheken werden mitgeliefert. Ein früher technischer Test prüft die vollständige Release-EXE.
- Der Plan enthält zehn umsetzbare Aufgaben, Abhängigkeiten, konkrete Prüfungen, Fehlerfälle und Belegpfade. Die erwarteten Skripte sind ausdrücklich zukünftige Implementierungsartefakte.
- Nur Plan und Entscheidungsentwurf wurden geschrieben. Keine Anwendung gestartet, keine Tests implementiert, keine Umgebung verändert, keine Kamera angesprochen.
- Strukturprüfung bestanden: acht vorgeschriebene Hauptabschnitte, zehn eindeutige Aufgaben, zehn konsistente Abhängigkeitszeilen, Belegpfad je Aufgabe, keine unbefüllten Templatefelder. Git-Status zeigt ausschließlich die beiden neuen Markdown-Artefakte.

## Approval gate
status: authorized
- Der Benutzer fordert ausdrücklich: "plane alles sauber im vorraus durch mit omo".
- Die Wahl "Neue EXE selbst starten; Einrichtung und Versionswechsel erfolgen automatisch" wurde bestätigt.
- Diese Freigabe gilt für das Schreiben des Plans. Sie startet keine Implementierung.
- Am 10.10.2026 hat der Benutzer mit "beginne die arbeit" die Umsetzung des Plans beauftragt. Die Ausführung ist in .omo/boulder.json und .omo/start-work/ledger.jsonl erfasst.
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
