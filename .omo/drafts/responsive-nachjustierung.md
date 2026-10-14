---
slug: responsive-nachjustierung
status: executing
intent: clear
pending-action: execute .omo/plans/responsive-nachjustierung.md
approach: gemeinsame Auswahl, atomarer direkter Save, native adaptive zwei Bereiche, dichte Settings
---

# Draft: responsive-nachjustierung

## Components (topology ledger)
1. Auswahl und Speicherauftrag: active; .omo/evidence/responsive-nachjustierung/.
2. Adaptive Bedienfläche und Settings: active; gleiche Belegablage.
3. Gezielte native Prüfung und Lieferdatei: active; gleiche Belegablage.
<!-- Lock the SHAPE before depth. One row per top-level component that can succeed or fail independently. -->
<!-- id | outcome (one line) | status: active|deferred | evidence path -->

## Open assumptions (announced defaults)
Position-only als direkte Speicherung; Foto über eigene Aktion. Nutzer wurde optional gefragt. Die Speicher-ID bleibt bis zum Ende gesperrt. Home hat weiterhin keine lokalen Zielwerte.
<!-- Record any default you adopt instead of asking, so the user can veto it at the gate. -->
<!-- assumption | adopted default | rationale | reversible? -->

## Findings (cited - path:lines)
PresetWorkflow.Select hält die gemeinsame Entwurfs-ID. TakePositionAsync entfernt bei verändertem Ziel ein veraltetes Bild. Zwei getrennte Take/Save-Aufträge würden eine Auswahl-Lücke erlauben. Daher eine einzige Operation.

## Decisions (with rationale)
Busy über Lesen/Speichern und genau eine ausgewählte ID. Sauberer aktueller Draft als Voraussetzung. Zwei UI-Bereiche aus vorhandenen Views; keine zweite Verbindung. Gezielte Tests und Root-Review nach Nutzerwunsch.

## Scope IN
Controller-Speicherung, gemeinsamer Auswahlzustand, zwei Bereiche bei breitem Fenster, kompakte Settings.

## Scope OUT (Must NOT have)
Protokoll-/Datenformat-/Deploymentänderung, neue Pakete, alte vollständige Testmatrizen, DPI-Infrastruktur.

## Open questions
Optional: soll eine neue Fotoaufnahme automatisch Teil des Save sein? Angekündigter Standard ist eine separate Taste.

## Approval gate
status: authorized
Direkter Änderungsauftrag nach bestehender Ausführungsfreigabe. Die Umsetzung läuft; keine weitere Planfreigabe erforderlich.
<!-- When exploration is exhausted and unknowns are answered, set status: awaiting-approval. -->
<!-- That durable record is the loop guard: on a later turn read it and resume at the gate instead of re-running exploration. -->
