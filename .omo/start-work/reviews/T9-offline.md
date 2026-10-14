# T9 offline review

## Aktueller Nachweis nach Admission-Fix

Root hat `T9/offline-current-20261010-222413-557` geprüft. Alle sechs tatsächlichen TRX-Zähler sind grün: Legacy92 aufnet481 und63 aufnet10; Persistence31; Controls47; Presets26; Deployment55. Insgesamt314 ausgeführt und bestanden,0Fehler und0nicht ausgeführt.

Die drei Fixture-Kopien haben unverändert SHA-256 `15DC37B4D0231158B60E335AA33AC103AB323832E830DACD51C80ED0A722B471`. Die aktuelle Quellenprüfung enthält189Einträge für52eindeutigePfade und keine Abweichung. Alle112gemeldeten eindeutigen Testdatenpfade sind entfernt. Es gibt keine neuen verbleibenden Scopes. Eigener C:-Buildscope und exakt zugeordnete Junction sind nach kanonischer Zielprüfung bereinigt.

Dieser aktuelle Nachweis ersetzt den früheren312-Fälle-Stand für die Offline-Suite. Die gesamte T9-Abnahme bleibt bis zu den echten Windows-/Foto-/Such-/DPI-Abläufen offen.

Status: Offline-Teil abgenommen. Vollständige T9-Abnahme offen. Datum: 10.10.2026.

Root hat summary.json, all-summary.json, framework-proof.json, fixture-copy-hashes.json, scope-cleanup-verified.json und den Handoff gelesen.

- Sechs Läufe mit 312/312 ausgeführten und bestandenen Tests. Keine Fehler oder übersprungenen Fälle.
- Legacy: net481 mit 92 Fällen und tatsächlich geladener .NET Standard 2.0-Bibliothek; net10.0 mit 63 Fällen und tatsächlich geladener .NET CoreApp 10.0-Bibliothek.
- AppCore: Persistenz 31, Bedienung 47, Entwürfe 26, Deployment 53.
- Alle drei Testausgaben enthalten die unveränderte packets.tsv mit SHA256 15DC37B4D0231158B60E335AA33AC103AB323832E830DACD51C80ED0A722B471.
- 110 eindeutige eigene Testordner sind entfernt; keine neuen Reste. Doppelte TRX-Ausgabemeldungen erhöhen diese Zahl nicht.
- Die gewünschte sichtbare Nutzerinstanz PID 52948 bleibt erhalten. Der Lauf hat keine WinUI-App geöffnet und keine Desktop-Eingabe gesendet.

Belege: T9/run-20261010-202451-601. Der Offline-Runner führt nun All und Deployment tatsächlich aus. Kein Test und keine Referenzfixture wurde für diesen Gesamtlauf geändert.

Offen: vollständige native Bedien-/Foto-/DPI-Matrix, echte neue A/B-Prüfung, Windows-Suche und sauberes Windows mit finaler EXE. Die reale Kamera ist nicht verfügbar. Der Offline-Erfolg ist keine vollständige Release-Freigabe.
