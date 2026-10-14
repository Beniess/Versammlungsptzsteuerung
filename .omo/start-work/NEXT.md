# Aktuell: Foto direkt speichern, abgeschlossen am 13.10.2026

Der aktuelle Nutzerauftrag ist vollständig umgesetzt. Aktiver Plan: .omo/plans/foto-direkt-speichern.md, alle Aufgaben abgehakt; Boulder completed. Kein weiterer Build, Gastlauf oder Review vorgesehen.

EXE 1.0.0.6: C:/Users/bn/.codex/visualizations/2026/10/10/01a1258e-d8b2-7f73-b401-338ebaf5f1eb/Release/QA-1.0.0.6-BAFF9765/CameraControl.WinUI.exe. SHA256 BAFF97658608580508B2ABC6C411736C84C909A43DD21B220E297213F3BAD0A4.

Native 1/1 bestanden, Datumslauf guest-20261013-182300-165-af689a55. Root hat tatsächlichen Dialog, gespeicherte Bildtaste, Mindestdialog, Ergebnis-JSON, EXE-Hash und Cleanup gelesen. 45/45 Workflow- und 17/17 Service-Fälle grün, keine Skips. Physische Kamera, höhere DPI und Hochkontrast bleiben ungetestet. UI-Antwort nur in einfacher technischer deutscher Sprache. Keine arbitrary LOC-Grenzen oder Reviewschwärme.

Neue Fotoaktion speichert unmittelbar nach Bestätigung; separat Position speichern und Foto-Entwürfe in Settings sind entfernt. Flache Verbindungs-/Namenszeilen und zwei klare Settings-Bereiche. GUI- und Backend-Ergebnis liegen unter .omo/evidence/foto-direkt-speichern/README.md und root-review.md. Quellenbegleitung Sources/App-1.0.0.6 hat 157 Dateien. Alle eigenen Apps und Sandbox-Clients sind beendet; erfolgreiche CIM-Abfrage mit leerer Clientliste ist in cleanup-final.json belegt.

Der folgende Stand betrifft ältere bereits abgeschlossene Änderungen.
# Arbeitsstand am 11.10.2026

## Aktuell: Responsive Nachjustierung

Neuer Nutzerauftrag: Settings deutlich kompakter; ausgewählte Ansicht direkt am Controller nachjustieren und speichern; beim Vergrößern Bildauswahl und Controller nebeneinander. Umsetzung autorisiert. Aktiver Plan: `.omo/plans/responsive-nachjustierung.md`.

Aktueller Abschlussbefund: Responsive Nachjustierung ist abgeschlossen. Der aktive Plan ist vollständig abgehakt; Boulder-Status ist completed. EXE 1.0.0.5, SHA256 BAA783C64ABE8BFCAAC3B89464246A84F6745AE4D7DBE571B360E60FA8FE61D3, liegt dauerhaft unter Release/QA-1.0.0.5-BAA783C6 im freigegebenen C:-Lieferbereich. Der einzelne native Fall guest-20261011-160052-755-0ee835a3 ist 1/1 grün, Exit 0. Home-Fix, gemeinsame Auswahl, Resize-Stopp, nur-Tisch-Speicherung und Datenbestand nach Neustart sind belegt. Root hat Source-Fix, TRX, JSON, echte PNGs und beide EXE-Hashes gelesen. Finale Abnahme: .omo/evidence/responsive-nachjustierung/root-acceptance.md. Alle Bearbeiter sind fertig; Apps und Gast sind geschlossen, Cache/Testport bereinigt. Keine weitere Suite oder Review-Schleife. .4 ist abgelehnt. Reale Kamera, höhere DPI-Stufen und hoher Kontrast bleiben als bekannte Grenzen dokumentiert. Die folgenden Eigentümernotizen beschreiben den ausgeführten Umfang.

- toolchain_setup: ShellViewModel, Services und die einzelne neue PresetWorkflow.SaveCurrentPositionAsync-Operation plus wenige gezielte Tests. Eine gemeinsame Auswahl; Busy über Lesen/Speichern; Home gesperrt; aktueller schmutziger Draft sichtbar sperren; andere IDs/Entwürfe unverändert. Bewegung vor Commit prüfen. Keine VISCA-Änderung.
- compact_ui: MainWindow, Positions-/Control-/SettingsViews, SettingsViewModel-Anbindung, DESIGN. Responsive Reparent cached Views, StopInput bei Layoutwechsel, gemeinsame Auswahl, dichte Expander-Settings und Controller-Save. APIs mit toolchain abstimmen.
- winui_video_plan: ein gezielter QA-Gast, native Screens bei 600×430, 960×560 und 360×280; Tisch auswählen, manuell korrigieren, direkt speichern, genau dessen Zielwerte und Neustart prüfen; Resize-Stopp; kompakte Settings. Vorbereitung parallel. Erst gemeinsamer Freeze/Build, dann einmal Publish Version1.0.0.4 in eigenem C:-Temp, keine alten vollständigen Testmatrizen.
- Optionaler Nutzerentscheid zum Foto ist angefragt. Angekündigter Standard: schnelle Ziel-Speicherung plus separate Fotoaktion. Bei verändertem Ziel wird veraltetes Foto gemäß bestehendem Vertrag entfernt. Keine zusätzliche Planfreigabe nötig; aktueller Änderungsauftrag erweitert bestehende Umsetzung.
- Root macht einen gezielten Code-/Bildreview. Keine Reviewschwärme, keine beliebige LOC-Grenze. Belege: `.omo/evidence/responsive-nachjustierung/`.

Der nachstehende Abschnitt ist der abgeschlossene vorherige Prüfstand 1.0.0.3. Die neue Arbeit hat Vorrang.

Die kompakte WinUI-App ist umgesetzt. Der Nutzer hat das Grundlayout bestätigt. Steuerung und Einstellungen sind kompakter. Keine Commits oder externe Veröffentlichung sind beauftragt.

## Bereitgestellter Prüfstand

- Version: 1.0.0.3.
- SHA-256: EF648A14732672BE2331F45AD24C0B9647CDB796DE48DDF84F0764DB4B9E3227.
- Größe: 350564671 Bytes.
- Dauerhafte EXE: C:/Users/bn/.codex/visualizations/2026/10/10/01a1258e-d8b2-7f73-b401-338ebaf5f1eb/Release/QA-1.0.0.3-EF648A14/CameraControl.WinUI.exe.
- Daneben liegen release-manifest.json, Start.txt, Zuordnung.txt und LICENSES. Der vorhandene Quellenbegleitsatz liegt unter ../Sources.
- Root hat den Hash der ursprünglichen und der bereitgestellten EXE unabhängig bestätigt. Die EXE wurde dem Nutzer verlinkt.

## Abgenommen

- T1–T4, T6, T7 und T8 sind im Plan abgehakt. F2 und F4 sind geprüft. Einzelbelege stehen in .omo/start-work/reviews/.
- Offline-All: 314/314, kein Skip. Beleg T9/offline-current-20261010-222413-557. Die gesicherten VISCA-Fixtures bleiben unverändert.
- Native Bedienung: Pan/Tilt, Zoom, Loslassen außerhalb, Minimieren, Seitenwechsel, Home und Hauptfensterschluss bei gehaltener Eingabe. Der abgelehnte Stopp sperrt weitere Aufträge und zeigt den Fehler.
- Fotos: drei Bilder nach Neustart erhalten; zehn Aufnahme-/Schließzyklen mit jeweils null Playern und Lesern; Fehler erhalten die gespeicherten Daten. Tatsächlicher Bibliotheksaustausch mit sichtbarer Markierung und PNG geprüft. Belege T7/guest-photos-20261011-124331-915-5985f8af und T7/guest-photos-20261011-125604-078-d2c9ccfb.
- Lokaler Versionswechsel: zwei gezielte gültige Gastfälle nach Pipe-Fix plus fünf gültige vorherige Fehlerfälle. Kein weiterer Siebenerlauf nötig.
- Letzter normaler Windows-Lauf: T9/clean-windows-20261011-142037-732-79ac5684. 4/4 bestanden, Exit 0, QaMode=false. Erstinstallation, echter Windows-Suchstart, erneuter Suchstart nach Download-Löschung und Livebild/private Module. Root hat Ergebnis, Such-PNG, tatsächliche ListItem-Auswahl, Prozesspfade und Hashes gelesen.
- Lizenzdialog auf letzter EXE: 360 × 280 Pixel bei echten 96 DPI, 31 DIP Textbereich, Bild-ab und Escape. Beleg T10/guest-current-20261011-121206-601-EF648A14/LicenseEnvironment.
- 953 Bundle-Dateien, 422 native Dateien, 26 Lizenzkomponenten mit 48 Originaltexten und 144 Quelldateien sind zugeordnet.

## Verbleibende Grenzen

Es gibt keine uneingeschränkte Release-Freigabe. 125/150/200 Prozent, hoher Kontrast und die reale Kamera bleiben ungeprüft. Der begrenzte Kontrastbeobachter suchte ohne Bindestrich; tatsächlich war Kontrast-Designs als enabled/offscreen vorhanden. Kein allgemeiner OS-Block wird behauptet. Keine weitere Anzeige-Testschleife oder Host-Anzeigeänderung.

T5, T9, T10, F1 und F3 bleiben als vollständige formale Abnahme offen. Das bedeutet keine erneute Ausführung der bereits gültigen Kernprüfungen. Der Nutzer verlangt kurze technische Sprache und gezielte Prüfungen.

## Abschluss

Alle Bearbeiter sind fertig. Bedienung, Freigabestand, Kandidatenmanifest und Startanleitung sind aktualisiert. Root hat cleanup-final.json des letzten CleanWindows-Laufs gelesen: eigene und sonstige Sandbox-Clients sind leer, kein Kill und keine Host-Eingaben. Die originale Meldung über den verzögerten Clientauslauf bleibt als historischer Befund erhalten. Die bereitgestellte EXE ist unverändert. Keine weiteren Builds oder Testläufe geplant.

Die letzte Produktdifferenz zur vollständig geprüften B5CB45 betrifft nur LicenseView.cs und Sources-and-replacement.md. Root-Beleg: T10/publish-candidate-20261011-132043-450/root-source-delta.json, 150/150 Dateien verglichen. Spätere CleanWindows-Korrekturen betreffen ausschließlich Testbeobachter. Der schnelle Einstellungs-Speicherfall mit Rücklese ist grün; kein Produktfix wurde dafür gemacht.


