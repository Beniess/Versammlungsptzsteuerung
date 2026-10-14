# responsive-nachjustierung - Work Plan

## TL;DR (For humans)
<!-- Fill this LAST, after the detailed plan below is written, so it summarizes the REAL plan. -->
<!-- Plain English for a non-engineer: NO file paths, NO todo numbers, NO wave/agent/tool names. -->

**What you'll get:** Nachjustierte Ansichten direkt beim Controller speichern. Bei breitem Fenster stehen Positionsauswahl und Controller nebeneinander; die Einstellungen werden kompakter.

**Why this approach:** Eine gemeinsame Auswahl verhindert abweichende Ziele zwischen Bildtasten und Controller. Ein einzelner Speicherauftrag hält die gewählte ID über Lesen und Speichern fest.

**What it will NOT do:** Kameraprotokoll, Home-Ziel, Datenformat und Installation ändern sich nicht.

**Effort:** Medium
**Risk:** Medium - Speicherauftrag und Auswahl müssen zusammenbleiben; Resize muss laufende Eingaben beenden.
**Decisions to sanity-check:** Direkte Ziel-Speicherung und separate Fotoaktion als angekündigter Standard; optionale Nutzerfrage läuft.

Die Umsetzung ist durch den aktuellen Änderungsauftrag und die bestehende Ausführungsfreigabe autorisiert.

---

> TL;DR (machine): Drei begrenzte Aufgaben: Auswahl/Speicherung, adaptive UI, gezielte native Prüfung mit EXE 1.0.0.5. Der vorherige Prüfstand 1.0.0.4 wurde wegen eines veralteten Controllerzustands abgelehnt.

## Scope
### Must have
- Gemeinsame ausgewählte Positions-ID für Bildtaste, Controller und Details.
- Reine Auswahl bleibt offline möglich. Aufruf und direkte Positionsspeicherung benötigen eine Verbindung. Laufende Vorgänge, Bewegung und Schließen sperren die Auswahl.
- Controller: Auswahl, Aufrufen, Position speichern und Foto aktualisieren; Name der ausgewählten Ansicht sichtbar.
- Direkte Positionsspeicherung ist eine einzige atomare Read/Save-Operation für die gewählte normale ID. Busy sperrt Auswahl und weitere Bewegung während der gesamten Operation.
- Fehlgeschlagene Abfrage oder Speicherung erhält den bisherigen Stand. Andere Presets und deren Entwürfe bleiben erhalten.
- Home behält den nativen Befehl; lokale Ziel-Speicherung ist für Home gesperrt.
- Bei breitem Fenster Bildraster links und Controller rechts. Unterhalb des aus Mindestbreiten abgeleiteten Breakpoints bleiben kleine Einzelansichten erreichbar. Ein gemeinsamer Satz View-Instanzen; Auswahl bleibt bei Resize/Navigation erhalten.
- Stop vor Reparent/Seitenwechsel, damit keine gehaltene Eingabe weiterläuft.
- Dichte Settings mit kurzen Beschriftungen und Aktionen, optionalen Daten in nativen Expandern; Status nur einmal anzeigen.
### Must NOT have (guardrails, anti-slop, scope boundaries)
- Keine neue Kamera-/Videoverbindung und keine Änderung der VISCA-Pakete.
- Kein Home-Zielsatz, keine Sicherung, Migration, neuen Pakete oder Internet-Updates.
- Keine neue DPI-/Hochkontrast-Testinfrastruktur oder vollständige Wiederholung gültiger Altmatrix.
- Kein unbeabsichtigtes Speichern eines fremden Entwurfs. Ein ungespeicherter aktueller Detailentwurf wird sichtbar als Voraussetzung behandelt.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: Gezielte NUnit-Tests zuerst für Auswahlbindung und Read/Save-Fehler. Native UI-Prüfung für die geänderten Abläufe danach.
- Evidence: .omo/evidence/responsive-nachjustierung/.
- Native Szenarien in einer Sandbox: 600×430, 960×560 und 360×280 bei gemessener DPI; ausgewählten Tisch nachjustieren und direkt speichern, Neustart prüfen, andere IDs unverändert, Home gesperrt, Stop bei Resize während Bewegung, kompakte Settings sichtbar.

## Execution strategy
### Parallel execution waves
Auswahl/Speicherung und UI parallel bei klarer API-Abstimmung. QA vorbereitet parallel, Ausführung erst nach gemeinsamem Freeze. Root macht einen gezielten Code- und Bildreview. Nutzer fordert keine Reviewschwärme.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | keine | 3 | 2 |
| 2 | API aus 1 | 3 | 1 |
| 3 | 1 und 2 | Abschluss | keine |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [x] 1. Gemeinsame Auswahl und direkte Positionsspeicherung
  Eigentum: ShellViewModel.cs, ApplicationServices und die einzelne PresetWorkflow-Operation sowie zugehörige Tests.
  Quellen: CameraControl.WinUI/ViewModels/ShellViewModel.cs; CameraControl.WinUI/ApplicationServices/ApplicationServices.cs; CameraControl.AppCore/Presets/PresetWorkflow.cs.
  Abnahme: Ziel der gewählten normalen ID atomar lesen/speichern; Busy über beide Phasen; fremde IDs/Entwürfe unverändert; aktuelle schmutzige Details und Home sperren; Fehler erhalten den Speicherstand. Nur passende Presets-/Services-Tests laufen.
  Commit: Nein, kein Git-Auftrag.

- [x] 2. Zwei Bedienbereiche und kompakte Einstellungen
  Eigentum: MainWindow, PositionsView, ControlView, SettingsView, UI-Anbindung in SettingsViewModel und DESIGN.md.
  Quellen: CameraControl.WinUI/MainWindow.xaml.cs; CameraControl.WinUI/Views/; DESIGN.md; WinUI controls-layout-and-adaptive-ui.md.
  Abnahme: Eine View-Instanz je Bereich; Auswahl synchron; Position und Foto direkt am Controller erreichbar; breite Ansicht nebeneinander; kleine Ansicht ohne verdeckte Hauptaktionen; Reparent beendet Eingaben. Ein erfolgreicher gemeinsamer Build und echte PNGs.
  Commit: Nein, kein Git-Auftrag.

- [x] 3. Gezielte native Prüfung und Prüfversion 1.0.0.5
  Eigentum: Native QA, privater Runner, Publish/Delivery und kurze Anleitung.
  Quellen: CameraControl.WinUI.Tests/NativeQaSession.cs; NativeAdjustmentTests.cs; NativePreviewTests.cs/PhotoViscaFixture; vorhandene Publish-App-Pipeline.
  Abnahme: Nur geänderte Szenarien in einem Gast mit Loopback und eigener Datenablage. Menü-/Feld-Anker und HWND/PID tatsächlich beobachten. Screenshots zuerst liefern, alle Pflichtfälle benennen. EXE-Version/Hash stimmen mit bereitgestellter Datei überein. App/Gast/Caches geordnet beenden.
  Commit: Nein, kein Git-Auftrag.

## Final verification wave
Eine strukturierte Prüfung durch Root anhand des Diffs, der gezielten Resultate und echter Bilder. Keine neue Freigabeabfrage für die bereits autorisierte lokale Erweiterung.
- [x] F1. Plan compliance audit
- [x] F2. Code quality review
- [x] F3. Real manual QA
- [x] F4. Scope fidelity

## Commit strategy
Kein Commit oder Push beauftragt.

## Success criteria
- Gewählte Ansicht direkt nachjustieren und speichern, ohne Settings-Seitenwechsel.
- Große Ansicht zeigt beide Bedienbereiche; kleine Ansicht bleibt nutzbar.
- Die Settings sind dichter und die seltenen Funktionen aufklappbar.
- Bestehende Kamerabefehle bleiben gültig. Keine reale Kamera wird im Test angesteuert.
