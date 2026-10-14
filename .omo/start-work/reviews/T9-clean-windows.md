# Normaler Windows-Start und Windows-Suche

Stand: 11.10.2026. Kandidat 1.0.0.3, SHA-256 `EF648A14732672BE2331F45AD24C0B9647CDB796DE48DDF84F0764DB4B9E3227`.

Beleg: `T9/clean-windows-20261011-142037-732-79ac5684/guest-output/Probe/`. Root hat Ergebnis, Suchauswahl, tatsächliches Such-PNG und Modulnachweis gelesen. Vier von vier Szenarien bestehen. Dies ist der normale Produktstart, `QaMode=false`.

1. Die EXE richtet sich aus dem schreibgeschützten Payload im eigenen Benutzerordner ein. Der Startmenülink enthält den richtigen Pfad und die Produkt-AUMID. Der installierte Prozess hat den erwarteten Hash und die Version 1.0.0.3.
2. Die tatsächliche Windows-Suche startet einen neuen installierten Prozess, PID 6380.
3. Nach dem Entfernen der Download-EXE startet dieselbe Suche einen neuen installierten Prozess, PID 1732. Die gespeicherten Daten bleiben unverändert. Der gewählte echte Suchtreffer ist ein ListItem mit dem App-Namen; kein direkter EXE- oder Linkstart ersetzt die Suche.
4. Der normal gestartete Prozess zeigt Livebild aus dem eigenen Loopback-Stream. Der exakt gehashte Host enthält die statische CLR; WinUI und beide VLC-Kern-DLLs sind tatsächlich aus seinem eigenen Bundlepfad geladen.

Im frischen Windows-11-Gast sind kein modernes .NET-Runtime-Verzeichnis und kein VLC installiert. Die vom Betriebssystem vorhandenen CBS-Pakete sind im Umgebungsbeleg ausgewiesen. Die App braucht hier keine zusätzliche Runtime-/VLC-Installation.

Eigene App-Prozesse sind beendet; kein App-Kill. Root hat cleanup-final.json um 12:26:36 UTC gelesen: eigene und sonstige Sandbox-Clients sind leer, kein Kill und keine Host-Eingaben. Die frühere Meldung des Wrappers über den verzögerten Clientauslauf bleibt getrennt dokumentiert. Es ist kein weiterer Testlauf nötig. Zusammen mit den vorhandenen sieben gültigen A/B-/Fehlerfällen ist T8 abgenommen.

Die Gesamtfreigabe bleibt wegen der ausdrücklich ungeprüften höheren DPI und Kontrastdarstellung offen. Die reale Kamera wurde nicht geprüft.
