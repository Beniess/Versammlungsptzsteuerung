# Freigabestand

Stand: 11.10.2026. **Keine uneingeschränkte Release-Freigabe.** Der geprüfte Kandidat hat Version 1.0.0.3. Darstellung bei 125/150/200 Prozent, Hochkontrast und Betrieb mit einer realen Kamera bleiben offen.

| Bereich | Stand |
| --- | --- |
| Historischer Import | Archivnachweis erhalten; getrennt von der neuen App dokumentiert |
| VISCA-Referenz und AppCore | Offline-All: 314/314 bestanden, kein Fehler und kein übersprungener Fall; einschließlich 55 Deployment-Fällen |
| Technischer Video-/Einzel-EXE-Nachweis | Livebild, PNG-Aufnahme und private WinUI-/VLC-Dateien nachgewiesen; statische CLR im geprüften Single-File-Host belegt |
| Sichtbare WinUI-Oberfläche | Helles und dunkles Layout bei 96 DPI geprüft. Lizenzdialog auf Kandidat 1.0.0.3 bei 360 × 280 Pixeln geprüft; Textbereich, Bild-ab und Escape bestätigt |
| DPI und Themen | 125/150/200 Prozent und Hochkontrast im begrenzten Gastlauf nicht nachgewiesen. Dies ist kein Nachweis einer allgemeinen OS-Unfähigkeit |
| Native Pointer- und Tastaturabläufe | Native Steuerungs-/Stoppabläufe im isolierten Gast bestanden; die vorhandenen Belege gelten für den unveränderten Steuerungscode |
| Foto und Vorschau | Fotos mit Neustart, zehn Aufnahmezyklen, Fehler-/Schließabläufe und kompatibler Bibliotheksaustausch bestanden |
| Normaler EXE-Erststart und Versionswechsel | Nach engem Pipe-Fix: 2/2 Gastfälle für A→B, Download-Löschung, Verknüpfungsstart, Parallelstart und minimierte Fensteraktivierung bestanden. Die fünf weiteren Pflichtfälle sind im vorigen Gastlauf bestanden |
| Sauberes Windows 11 | Kandidat 1.0.0.3 auf Windows 11 Build 26100 ohne separat installierte .NET-Runtime oder VLC gestartet; eigenes HWND, Host-Hash und private WinUI-/VLC-Dateien bestätigt |
| Windows-Suche und gestartete Version | Echte Windows-Suche startet die installierte Version 1.0.0.3. Erneuter Suchstart nach Download-Löschung mit unveränderten Daten bestanden |
| Lizenzquellen und Drittsoftware-Unterlagen | 26 Komponenten und 48 indizierte Originaltexte direkt aus der EXE geprüft; 144 vorhandene Quelldateien über Hashes zugeordnet. Keine pauschale Lizenzfestlegung für die eigene App |
| Reale Kamera | Kein Betrieb und keine physische Bewegungs-/Bildsynchronitätsprüfung durchgeführt |

Belege: [Offline-All 314/314](../.omo/evidence/winui3-modernisierung/T9/offline-current-20261010-222413-557/all-summary.json), [Gast-Gegenlauf 2/2](../.omo/evidence/winui3-modernisierung/T8/guest-release-20261010-224728-674/guest-output/Release/release-summary.json) und [gezielter Startpfad-Review](../.omo/evidence/winui3-modernisierung/T8/targeted-startup-review-20261011-122941-445.json). Die fünf weiteren Fälle prüfen Startfehler, Abbruch, gesperrte Verknüpfung mit Wiederherstellung, verweigertes Schließen und eine nicht erreichbare Kamera. Alle sieben Pflichtabläufe sind damit über die beiden Läufe belegt; ein neuer vollständiger Siebenerlauf wird nicht behauptet. Die eigenen Testordner und Prozesse wurden jeweils ohne Kill bereinigt.

Die [aktuelle Lizenzprüfung](../.omo/evidence/winui3-modernisierung/T10/guest-current-20261011-121206-601-EF648A14/LicenseEnvironment/summary.json) belegt den Dialog am Mindestmaß. Die [beobachteten OS-Elemente](../.omo/evidence/winui3-modernisierung/T10/guest-current-20261011-121206-601-EF648A14/LicenseEnvironment/os-boundary.json) belegen nur den begrenzten Prüfversuch für höhere Skalierung und Hochkontrast; diese Punkte sind nicht bestanden.

Der Kandidat 1.0.0.3 hat SHA256 `EF648A14732672BE2331F45AD24C0B9647CDB796DE48DDF84F0764DB4B9E3227`. Der [CleanWindows-/Suchlauf](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/clean-windows-results.json) besteht alle vier Szenarien. [Suchtreffer](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/search-after-download-removal.png) und [Livebild](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/clean-windows-preview.png) wurden im echten Gast aufgenommen. Der Gast meldete Exitcode 0. Der Host-Beobachter sah den Client zunächst noch beim Schließen; die [abschließende CIM-Prüfung](../.omo/evidence/winui3-modernisierung/T9/clean-windows-20261011-142037-732-79ac5684/cleanup-final.json) bestätigt null Clients, ohne Kill.

Der [Quellvergleich](../.omo/evidence/winui3-modernisierung/T10/publish-candidate-20261011-132043-450/root-source-delta.json) zeigt gegenüber dem zuvor geprüften B-Stand nur Änderungen am Lizenzdialogkopf und am Austauschdokument. Die Video-, Steuerungs-, Deployment- und Core-Belege bleiben für diesen unveränderten Funktionscode gültig. Ein neuer vollständiger Deployment- oder Offline-Lauf wurde nicht ausgeführt.

Die aktuelle QA verwendet ausschließlich eigene Daten und lokale Testendpunkte. Ein erfolgreicher Loopback-Test bestätigt keine gerätespezifischen Eigenschaften einer realen Kamera. VISCA-Port, RTSP-Pfad, Zugangsdaten und physisches Home-Verhalten des eingesetzten Geräts bleiben vor dem echten Betrieb zu prüfen.

Offene Punkte werden erst nach einem ausgeführten Pflichtfall aktualisiert. Ein fehlender Desktop, eine gesperrte Eingabe oder ein ungültiges Bild ist kein PASS.
