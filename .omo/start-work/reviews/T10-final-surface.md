# Letzter Kandidat: gezielte Oberflächenprüfung

Stand: 11.10.2026. Version 1.0.0.3, SHA-256 `EF648A14732672BE2331F45AD24C0B9647CDB796DE48DDF84F0764DB4B9E3227`.

Root hat die EXE unabhängig gehasht. Der Vergleich der 150 Quelldateien zum geprüften Stand B5CB45... ergibt genau zwei Änderungen: `LicenseView.cs` und `Sources-and-replacement.md`. Keine Datei ist entfernt. Kamera-, Foto-, Speicher-, Start- und Versionswechselcode bleiben unverändert. Die vorhandenen gültigen Prüfungen werden deshalb nicht pauschal wiederholt.

Beleg: `T10/guest-current-20261011-121206-601-EF648A14/LicenseEnvironment/`. Der Lizenzfall besteht bei echten 96 DPI und 360 × 280 Pixeln. Der Dokumentbereich ist 31 DIP hoch. Bild-ab und Escape funktionieren. Root hat `license-dialog-minimum/96dpi-Light-sources-keyboard-scroll.png` gelesen. Versionsnummer, Dokumentauswahl, Text und Schließen-Taste sind sichtbar.

`os-boundary.json` präzisiert die Grenze: Der Texttreffer auf der System-Seite betrifft nur das Anzeigen installierter KI-Komponenten, keine Skalierung. Die Barrierefreiheit-Seite enthält einen aktivierten, aber außerhalb des sichtbaren Bereichs liegenden Eintrag `Kontrast-Designs`. Der begrenzte Beobachter hat diesen nicht aktiviert. Das echte OS-Kontrastflag bleibt aus; beide strengen Kontrastfälle bestehen deshalb nicht. Andere DPI und hoher Kontrast bleiben ausdrücklich ungeprüft. Eine allgemeine Unfähigkeit des Betriebssystems wird nicht behauptet. Der Nutzer verlangt begrenzte Prüfungen; es wird keine neue Infrastruktur oder Host-Anzeigeänderung dafür eingeführt.

Der konkrete Fehler des Kontrastbeobachters ist dokumentiert: Er suchte nach `Kontrastdesign` ohne Bindestrich. Der tatsächliche Name lautet `Kontrast-Designs`. Dies ist eine offene Grenze des Prüflaufs, kein nachgewiesener App- oder OS-Fehler. Es erfolgt keine weitere Anzeige-Testschleife.

Der aktuelle Windows-Suchstart und der abschließende Startnachweis stehen noch aus. Dies ist keine vollständige Release-Freigabe.
