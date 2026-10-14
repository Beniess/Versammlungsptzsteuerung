Die Datei `packets.tsv` enthält feste Referenzbytes. Die Einträge wurden aus der Referenzmatrix des Plans und dem vorhandenen VISCA-Vertrag festgelegt. Die Tests verwenden keinen Produktionsencoder zur Berechnung der erwarteten Bytes.

Das Format lautet `Name|Hexbytes`. Alle Drahtpakete enthalten den Abschluss `FF`. Der Aufnahmeclient erhält vollständige sendende Pakete. Empfangende `ViscaPacket`-Werte enthalten keinen Abschluss; `Fixtures.Response` entfernt dafür nur das feste letzte Byte. Diese Testfunktion berechnet keine Werte oder Antwortinhalte.

Die positiven, negativen und Grenzwerte gelten für den bestehenden 16-Bit-Bibliotheksvertrag. Ein negativer Zoomwert ist ein Referenzfall der Bibliothek. Er ist keine erlaubte Eingabe der neuen Oberfläche und keine Aussage zur realen Kamera.

Die Steuerfeldtests verwenden die vorhandene WPF-Geschwindigkeitsberechnung. Die Testzugänge übergeben Koordinaten an denselben Code, den die Mausereignisse verwenden. Die Tests beginnen Stoppfälle mit einer laufenden Bewegung. Identische Zustände erzeugen weiterhin keinen zweiten Befehl.

`Test-Offline.ps1 -Suite Protocol` prüft die festen Befehle und Antworten für beide Bibliotheksziele. `-Suite Legacy` ergänzt TCP-Fälle für beide Ziele und WPF-Fälle auf net481. Andere geplante Suiten geben bis zu ihrer Implementierung Exitcode 1 zurück.
