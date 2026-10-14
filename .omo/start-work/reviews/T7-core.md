# T7-core: gezielter Review

Root hat PresetWorkflow.cs, den Ergebnisbericht und die tatsächlichen TRX-Zähler gelesen. Der finale Lauf hat total=26, executed=26, passed=26 und keine nicht ausgeführten Fälle. Die ersten 24 Fälle waren vor der Implementierung rot.

Die Aufnahme übernimmt erst nach gleichen Positionswerten vor und nach dem Foto. Beobachtete Bewegung sperrt die Übernahme auch nach einem Stopp. Home erhält ausschließlich ein Bild. Entwurf, Bildbesitz, Speichern und Abbruch sind getrennt. Der Kommentar zum Bewegungssignal wurde mit Volatile.Read umgesetzt.

Freigabe betrifft nur AppCore. Der PNG-Adapter ist kontrolliert und liefert keinen Beleg für einen echten WinUI-Decoder. Die spätere AppContext muss alle Load/Modify/Save-Abläufe unter einer gemeinsamen Schreibsperre ausführen. Der Repository-Lock allein genügt dafür nicht. T7 bleibt bis zur nativen Integration und Oberflächenprüfung offen.

Beleg: .omo/evidence/winui3-modernisierung/T7-core/run-20261010-171609-671/net10.0/results.trx. Alle 26 temporären Datenordner sind entfernt. Keine echte Kamera und kein Medienprozess wurden gestartet.
