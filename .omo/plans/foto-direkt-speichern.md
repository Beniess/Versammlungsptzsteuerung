# foto-direkt-speichern - Work Plan

## TL;DR (For humans)
<!-- Fill this LAST, after the detailed plan below is written, so it summarizes the REAL plan. -->
<!-- Plain English for a non-engineer: NO file paths, NO todo numbers, NO wave/agent/tool names. -->

**What you'll get:** Foto direkt in der Steuerung aufnehmen, im benannten Bilddialog bestätigen und sofort speichern. Kompakte Einstellungen mit Verbindung und Ansichten.

**Why this approach:** Eine Fotoaktion speichert Bild und Kameraziel gemeinsam. Die Bestätigung hält dieselbe Ansicht fest und benötigt keinen zweiten Speicherschritt.

**What it will NOT do:** Kameraprotokoll, Datenformat, Installation und Vorschau-Player ändern sich nicht.

**Effort:** Medium
**Risk:** Medium - Auswahl und Daten müssen über Aufnahme, Bestätigung und Speicherung zusammenbleiben.
**Decisions to sanity-check:** Foto ist der gemeinsame Ablauf für Bild und Kameraziel. Home speichert nur sein Bild. Namen werden getrennt ausdrücklich gespeichert.

Der aktuelle Änderungsauftrag autorisiert die Umsetzung. Keine weitere Planfreigabe ist nötig. Der Nutzer verlangt einen begrenzten Review während der Arbeit.

---

> TL;DR (machine): Drei begrenzte Aufgaben: bestätigte Fotooperation, kompakte native UI, gezielte Prüfung und EXE 1.0.0.6.

## Scope
### Must have
- Ein Foto-Button im Controller. Benannter Dialog mit tatsächlichem PNG und Speichern/Abbrechen. Bestätigung speichert sofort die gewählte ID; normale Ansichten speichern Foto und Kameraziel, Home nur das Foto.
- Busy und feste ID während Aufnahme, Dialog und Commit. Abbrechen, Aufnahmefehler und Schreibfehler erhalten Datei und vorherigen Entwurf. Andere IDs bleiben erhalten.
- Separate Controller-Taste Position speichern und versteckte Aufnahme-/Entwurfsaktionen in Settings entfernen. Zwei kompakte native Bereiche: Verbindung und Ansichten. Name speichern bleibt ausdrücklich.
- Responsive Zwei-Pane-Layout, Kamerasteuerung, Stopp und Vorschau behalten.
### Must NOT have (guardrails, anti-slop, scope boundaries)
- Kein geändertes VISCA-Paket, neues Datenformat, Migration, Backup, Video-Player oder Installationsmodell.
- Keine beliebige LOC-Grenze oder Reviewschwärme. Ein UI-Bearbeiter, Root für Speicherablauf und gezielte Prüfung. Kein erneuter kompletter Altprüflauf.

## Verification strategy
> Zero human intervention - all verification is agent-executed.
- Test decision: TDD für Capture/Confirm/Commit mit vorhandenen NUnit-Fixtures; gezielte native Prüfung danach.
- Evidence: .omo/evidence/foto-direkt-speichern/.

## Execution strategy
### Parallel execution waves
UI und Speicherablauf parallel bei fester API. Root prüft Code und tatsächliche Bilder. Keine weitere Agentenrunde.

### Dependency matrix
| Todo | Depends on | Blocks | Can parallelize with |
| --- | --- | --- | --- |
| 1 | keine | 3 | 2 |
| 2 | API aus 1 | 3 | 1 |
| 3 | 1 und 2 | Abschluss | keine |

## Todos
> Implementation + Test = ONE todo. Never separate.
<!-- APPEND TASK BATCHES BELOW THIS LINE WITH edit/apply_patch - never rewrite the headers above. -->
- [x] 1. Foto aufnehmen, bestätigen und atomar speichern
  Eigentum: Root, PresetWorkflow.cs, ApplicationServices und ShellViewModel, gezielte Core-/Services-Tests.
  Quellen: bestehende CaptureAsync-, SaveAsync- und OwnCameraTask-Pfade, PresetWorkflowTests.
  Abnahme: echte rote/grüne NUnit-Fälle für Speicherung, Abbrechen, Home, Busy, Bewegung, Token-/Schreibfehler und fremde IDs. Callback erhält Name und PNG, Datei bleibt bis Bestätigung unverändert.
  Commit: Nein.
- [x] 2. Controller, Fotodialog und kompakte Settings
  Eigentum: simple_photo_ui, ControlView/SettingsView/MainWindow, SettingsViewModel, neuer nativer Dialog und DESIGN.
  Quellen: vorhandene native Views und WinUI-Layout-/Dialogvorgaben.
  Abnahme: klarer Foto-Button, benannter Dialog, nur ein Speicherschritt, zwei Settings-Bereiche, expliziter Namenssave, kein Entwurfsjargon. Gemeinsamer Build ohne Fehler.
  Commit: Nein.
- [x] 3. Native Bedienprüfung und EXE 1.0.0.6
  Eigentum: Root, vorhandene private QA-Runner und eigene lokale Medien-/VISCA-Fixtures.
  Abnahme: tatsächliches Bild im Dialog, Abbrechen ohne Änderung, Bestätigung speichert Ziel/Foto nur für Tisch und zeigt Bildtaste. Home hat Bild ohne Ziel. Kompakte Settings und Mindestgröße sind erreichbar. Daten bleiben nach Neustart erhalten. Eigene Prozesse/Gast bereinigen; EXE und Hash bereitstellen.
  Commit: Nein.

## Final verification wave
Ein gezielter Root-Review von Code, Tests und nativen Bildern. Keine zusätzliche Freigabe für die bereits beauftragte lokale Änderung.
- [x] F1. Plan compliance audit
- [x] F2. Code quality review
- [x] F3. Real manual QA
- [x] F4. Scope fidelity

## Commit strategy
Kein Commit oder Push beauftragt.

## Success criteria
Foto in der Steuerung drücken, benannten Dialog bestätigen, Bild und Kameraziel sind gespeichert. Die Settings sind verständlich und kompakt. Die gesicherten Kamerabefehle bleiben unverändert.
