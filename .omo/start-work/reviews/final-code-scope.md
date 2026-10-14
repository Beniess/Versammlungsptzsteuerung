# Abschließender Code- und Umfangsreview

Stand: 11.10.2026. Kandidat 1.0.0.3, SHA-256 `EF648A14732672BE2331F45AD24C0B9647CDB796DE48DDF84F0764DB4B9E3227`.

F2: Der gezielte Review ist abgeschlossen. Die Einzelreviews T1–T8 prüfen Befehle, Datenpfade, atomare Schreibvorgänge, Auftragslebensdauer, Stoppen, Dispose und lokalen Versionswechsel. Die letzten Produktänderungen betreffen nur den Lizenzkopf und den Quellenhinweis. Root hat diese Änderungen und den Vergleich aller 150 Quellhashes gelesen. Der Kandidat ist fehlerfrei gebaut; sein tatsächlicher Lizenzdialog ist geprüft. Die späteren CleanWindows-Korrekturen betreffen ausschließlich Beobachter im Testwerkzeug. Der schnelle Speichertest mit Feldrücklese ist grün; ein Verlust sichtbar gesetzter Daten ist in diesem Gegenlauf nicht nachgewiesen. Keine zusätzliche Produktänderung erfolgt aufgrund einer Vermutung.

F4: Der Umfang ist eingehalten. Die neue App hat keine Sicherungs-, Altimport- oder Internet-Updatefunktion. Einstellungen und Bilder liegen im eigenen Benutzerpfad; Zugangsdaten liegen im Windows-Speicher. Der lokale EXE-Versionswechsel ersetzt keinen Internet-Updater. Die alten VISCA-Pakete sind durch die vorab gesicherte Referenz und die tatsächlichen nativen Eingaben bestätigt. Home bleibt der native Befehl ohne zusätzlichen Zoom. Der WPF-Referenzpfad behält den ursprünglichen Befehlsablauf; seine Änderungen betreffen den Testzugriff und das zusätzliche moderne Bibliotheksziel.

Offene Nachweise sind davon getrennt: tatsächlicher Windows-Suchstart, höhere DPI und hoher Kontrast. Der bekannte Kontrast-Selektorfehler liegt im begrenzten Prüfwerkzeug. Keine vollständige Release-Freigabe aus diesem Code-Review ableiten. Die echte Kamera wurde nicht geprüft.
