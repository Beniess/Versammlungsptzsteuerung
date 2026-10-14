# T5: Kompakte Seiten, gezielter Review

Stand: 10.10.2026. Der Nutzer bestätigt: Grundlayout behalten; Bedienseiten kompakter.

Beleg: `T7/guest-pages-20261010-211449-851/guest-output/`.

Root hat die vier PNGs und `ui-pages.json`, `runner-results.json`, `completion.json`, Seitenmetadaten und Cleanup gelesen. Es ist die reale EXE 1.0.0.2 mit SHA-256 `19FF1A32A880C332949FCDD81D68B06E5063580BE109FA71E53D2B754C7DAC55`. Das Fenster hat 600 × 430 Pixel bei gemessenen 96 DPI. Der Gast bestätigt das tatsächliche Vordergrundfenster.

Die Positionsseite hat vier gleich große Tasten. Steuerfeld und Zoom stehen nebeneinander. Host und Port stehen nebeneinander. Der Fotoeditor zeigt Auswahl, Name und Aufnahmeaktionen ohne leeren Bildblock. Die drei Hauptseiten wurden dem Nutzer als echte Screenshots gezeigt. Ohne Konfiguration sind die Kameraaktionen deaktiviert.

Ein enger Befund bleibt: Der Tastaturhinweis unter dem Steuerfeld ist bei Zielgröße auf 5 Pixel gekürzt. Der UI-Bearbeiter setzt diesen Hinweis bei breiter Ansicht in die freie rechte Spalte. Padgröße und Bedienabbildung bleiben erhalten. Dieser Befund verhindert die vollständige T5-Abnahme.

Nachprüfung: Der Hinweis steht im breiten Layout rechts. Root hat den geänderten Quelltext gelesen. Der Build vor der Speicherblockade hat keine Fehler oder Warnungen. Ein neues tatsächliches Bild steht noch aus.

Ein zweiter enger Befund wurde im Quelltext bestätigt: Ein Vorgangshinweis ersetzte den gesamten Steuerungs-/Videostatus. Der Hinweis blieb bei späteren Service-Ereignissen erhalten. Root hat die Korrektur in MainWindow-XAML und OnModelChanged gelesen: ControlStatus zeigt immer beide Statuswerte; OperationMessage zeigt den Vorgangs-/Ladehinweis separat, mit Fehlerpräfix und vollständigem Tooltip. Der Aktivierungsvertrag von T8 blieb unverändert. Build und realer Streamfehlernachweis für diesen Satz stehen noch aus.

Cleanup: App beendet, eigener Bundlecache entfernt, Gast geordnet geschlossen, keine eigenen Clients übrig, kein Kill.

Offen: tatsächliche DPI-/Themenmatrix, Mindestgröße, hoher Kontrast und Lizenzdialog. Keine Freigabe allein aufgrund dieses Teilnachweises.

## Nachprüfung am 11.10.2026

Beleg: `T5/guest-current-20261011-121206-601/CompactLayout/`. Aktuelle EXE B: SHA-256 `2A49E6E7CC258439652B6DD9E19625E60BFD4B1A90D53BBEB46EE850F75B46ED`.

Root hat die echten Light-/Dark-PNGs bei 600 × 430 und 360 × 280 Pixeln gelesen. Gemessene DPI: 96. Das Zielmaß zeigt vier gleich große Tasten und vollständige Statuszeilen. Das Dark-PNG wurde dem Nutzer gezeigt. Die langen Namen sind Testdaten. Die grauen Tasten zeigen die fehlende Kameraverbindung.

Bei 360 × 280 schneiden die zweizeiligen langen Namen den darunterliegenden Status am Kartenrand ab. Home bleibt lesbar. Die UIA-Bounds-Tests sind grün, die visuelle Mindestgrößenprüfung bleibt rot. Der UI-Bearbeiter korrigiert nur die Text-/Bildaufteilung. Raster und Mindestmaß bleiben unverändert. Andere DPI-Werte und hoher Kontrast sind damit nicht geprüft.

Der aktuelle Controls-Beleg `T6/guest-current-20261011-121206-601/Controls/control-pointer-stop/96dpi-System-pan-moving.png` zeigt 600 × 430 Pixel, 96 DPI, PID 5348 und HWND 131218. Root hat das PNG gelesen und dem Nutzer gezeigt. Steuerfeld und rechte Befehlsgruppe passen. Der Tastaturhinweis ist jetzt vollständig sichtbar. Der frühere Hinweis-Clip ist damit behoben.

Root hat auch `control-stop-error/96dpi-System-stop-error.png` gelesen. Der Kameraauftrag zeigt einen Fehler. Die Bedienfelder sind deaktiviert. Der Vorschauzustand bleibt separat sichtbar. Der weitere Pointerfall scheitert am fehlenden UIA-Knoten `ControlPage`; der tatsächliche Baum enthält `ControlScroll`. Zoom und Hauptfensterschluss bleiben bis zum gezielten Gegenlauf offen.

## Mindestgröße nach Korrektur

Aktuelle B `5CB45B0505B523C38272CCA4D5CFA2D1AD43521C8C866B9CE8E7703CA767A8CC`: Root hat `T5/guest-current-20261011-121206-601-5CB45B05/UiRemaining/Light-compact-matrix/96dpi-Light-positions-minimum.png` gelesen und gezeigt. Echte 360 × 280 Pixel bei 96 DPI, PID 7768, HWND 131404. Alle vier Statuszeilen sind vollständig sichtbar. Namen belegen eine Zeile mit Ellipsis. In dieser geringen Kachelhöhe ist die Bildfläche ausgeblendet. Das 2×2-Raster und die erreichbare native Navigation bleiben erhalten. Die gezielte visuelle Light-Mindestgrößenprüfung ist damit bestanden.
