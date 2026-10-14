# T6: Native Bedienung abgenommen

Stand: 11.10.2026. Root hat Testcode, Ergebnis, Drahtpakete und Prozessende gelesen.

Aktueller Beleg: `T5/guest-current-20261011-121206-601-5CB45B05/UiRemaining/`. B hat SHA-256 `5CB45B0505B523C38272CCA4D5CFA2D1AD43521C8C866B9CE8E7703CA767A8CC`.

Der native Pointerfall ist bestanden. Reale Eingaben erzeugen Pan/Tilt 12/12, bestätigten Stopp beim Loslassen außerhalb, Zoomstufe 4 und Zoomstopp. Minimieren, Seitenwechsel und Hauptfensterschluss bei gehaltener Pfeiltaste erzeugen jeweils einen zusätzlichen bestätigten Pan-Stopp. Home erzeugt `81010604FF` ohne zusätzlichen Zoom. Das nicht eingerichtete Leser-Preset bleibt gesperrt. Die eigene TCP-Verbindung endet beim Schließen. Prozess 6096 ist beendet; sein eigener Bundlecache ist entfernt.

Der bereits gültige Fehlerfall liegt unter `T6/guest-current-20261011-121206-601/Controls/control-stop-error/`. Ein abgelehnter Stopp trennt die Verbindung, sperrt weitere Aufträge und zeigt den Fehler sowie den separaten Vorschauzustand. Root hat den tatsächlichen Screenshot gelesen. Seit diesem Beleg änderte sich am Produkt nur das Positionskartenlayout.

Ergänzend bleiben Legacy 92 + 63 und Controls 47 aus `T9/offline-current-20261010-222413-557` gültig. Die gesicherten Referenzpakete bleiben unverändert. Damit ist T6 abgenommen. Die reale Kamera ist nicht geprüft.
