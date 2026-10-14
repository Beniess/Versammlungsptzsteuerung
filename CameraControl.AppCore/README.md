# Einstellungen und Datenspeicher

`AppSettings.CreateDefault()` erstellt die vier festen IDs `leser`, `tisch`, `redner`, `home`. Die drei normalen Positionen haben zunächst keinen Zielwert. Ein ausdrücklich gespeicherter Zielwert 0/0/0 ist zulässig. Home hat keinen lokalen Zielwert. Die Oberfläche muss Home zusätzlich an den Verbindungsstatus binden.

`ConnectionSettings` hält Host, VISCA-Port, RTSP-URL und einen optionalen Verweis auf Windows-Zugangsdaten. Der anfängliche Host und die RTSP-URL sind leer. Der anfängliche Port ist 5678; er ist ein editierbarer Standardwert und keine Gerätegarantie. Eine URL mit eingebetteten Zugangsdaten wird abgewiesen. Ein Credential-Verweis beginnt mit `Versammlungsptzsteuerung/`. Die Windows-Zugangsdaten selbst gehören nicht zu diesem Projekt.

`WindowPlacement` speichert äußere physische Pixel für X, Y, Width und Height. Scale ist das Verhältnis der Windows-Skalierung, zum Beispiel 1,25. T4 prüft gültige Maße. Die WinUI-Fensterverwaltung begrenzt die Wiederherstellung an Monitor-Arbeitsfläche und Mindestgröße.

`WindowsDataPath.GetDirectory()` liefert `LocalApplicationData/Versammlungsptzsteuerung/Data`. Die Funktion legt keine Dateien an. Die Anwendung erstellt `JsonSettingsRepository` mit diesem Pfad. Tests verwenden einen eigenen absoluten Datenordner.

`LoadAsync` liefert `SettingsLoadResult` mit Status `Missing`, `Loaded`, `Invalid`, `UnsupportedVersion` oder `Unavailable`. Bei einem Fehler enthält das Ergebnis unkonfigurierte Startdaten und eine Meldung. Der Aufrufer darf diese Startdaten anzeigen; er darf einen Fehler nicht als gespeicherte Konfiguration behandeln. `SaveAsync` überschreibt eine ungültige, unbekannt versionierte oder unlesbare Bestandsdatei nicht. `ResetAsync` ist die ausdrückliche neue Einrichtung.

`SaveAsync` erhält die vollständigen Einstellungen und optional neue PNG-Bytes je fester Positions-ID. Das Ergebnis enthält die aktivierten eigenen Bildnamen. Neue PNG-Dateien werden vollständig geschrieben und geflusht. Danach wird die benachbarte temporäre JSON-Datei atomar eingesetzt. Erst nach dem JSON-Wechsel entfernt das Repository nicht mehr referenzierte eigene Bilder. Eine blockierte Bereinigung darf eine erfolgreich gespeicherte Einstellung nicht als fehlgeschlagen melden; verbleibende eigene Bilder werden beim nächsten erfolgreichen Speichern erneut geprüft. Es gibt keine Sicherungsdateien.

Alle Repository-Instanzen im selben Prozess verwenden eine gemeinsame Schreibsperre je normalisiertem Datenordner. Die spätere App-Einzelinstanzverwaltung muss mehrere schreibende Produktprozesse verhindern. Ein Kandidatenprozess für einen Versionswechsel liest Daten bis zur Aktivierung nur.

Bildnamen haben das Format `img-<32 Hexzeichen>.png`. `TryReadImageAsync` liefert bei fehlenden oder strukturell beschädigten Bildern null. Die Speicherprüfung kontrolliert PNG-Signatur, Chunkgrenzen und CRC. Der WinUI-/Video-Adapter muss das Bild zusätzlich decodieren und bei einem Decoderfehler den Platzhalter anzeigen. Diese Bildprüfung ändert den Status der Kameraverbindung nicht.

Prüfbefehl: `pwsh -NoProfile -File scripts/Test-Offline.ps1 -Suite Persistence`. Das Skript prüft Exitcode, Pflichtfälle, Testanzahl und fehlende oder übersprungene Tests. Belege stehen unter `.omo/evidence/winui3-modernisierung/T4/`.
