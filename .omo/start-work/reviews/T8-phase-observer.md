# T8: Phasen des Testbeobachters

Der erste echte Gastlauf hat einen zu frühen Testabbruch gezeigt. Root hat `T8/guest-phase-clock/phase-clock.diff` und `debug-journal.txt` vollständig gelesen.

Der bisherige 90-Sekunden-Timer beginnt nach Process.Start. Er umfasst die EXE-Extraktion, das lokale Staging und den Kandidatenstart gemeinsam. Fall1045 begann Cleanup vor candidate.spawned. Fall1046 begann Cleanup vor einem danach erfolgreichen Commit. Die erhaltenen Pipe-Werte passen zu Parent, Token und Pfad. Die gültigen Roundtrips dauern5–8ms. Dies ist kein neuer Beleg des alten Broken-Pipe-Fehlers.

Der Testbeobachter verwendet jetzt60Sekunden bis zum passenden Managed-Started, danach90Sekunden für Managed-Quelle/Staging. Ein echter candidate.spawned-Eintrag der aktuellen Quelle wechselt einmal in die vorhandene120-Sekunden-Kandidatenphase. Die absolute Gesamtgrenze ist270Sekunden ab echter Prozessstartzeit. Aktuelle Source-PID, Pfad, Version und Ereigniszeit begrenzen den Wechsel. Keine beliebige Pipe-Aktivität setzt eine Frist zurück.

Die Produktfristen und die tatsächlichen Fenster-, Versions-, Pipe-, Daten- und Cleanup-Assertions bleiben unverändert. Der minimierte Duplikatfall prüft IsIconic und GetForegroundWindow ohne einen Aktivierungshelfer als Ersatz.

Der neue Beobachter ist für den echten Gegenlauf freigegeben. Der erste vollständige rote Lauf bleibt erhalten. Noch keine vollständige T8-Abnahme.

## Eigene Wartephase für parallele Quellen

Der erste vollständige Gastlauf endet mit4/7bestanden,3Fehlern und0Skip. Der zusätzliche echte Fehler betrifft zwei B-Quellen: Die zweite Quelle erreicht die bisherige30-Sekunden-Sperrfrist, während die erste Quelle noch innerhalb des erlaubten Kandidatenstarts später erfolgreich committet.

Root hat LocalDeployment und DeploymentFiles.AcquireAsync gelesen. Die Aufnahme in den Einrichtungsablauf hat jetzt eine eigene, auf5Minuten begrenzte Frist. AcquireAsync verwendet den eigenen Stopwatch und CancellationToken. Es wird nicht mehr durch die kürzere Operationsfrist begrenzt. Exit, IPC, Link und Kandidatenstart behalten ihre eigenen bisherigen Grenzen.

Root hat die TRX-Zähler gelesen. Zwei relevante Regressionen sind zuerst rot: SecondStartWaitsPastTheOperationBudgetForTheCurrentValidCandidate und AdmissionWaitHasItsOwnBoundAndPreservesTheActiveRelease. Danach sind55/55Deployment-Corefälle ausgeführt und bestanden,0Skip.

Der Testbeobachter verwendet genau eine belegte Aufnahme-/Freigabephase der aktuellen Quelle. Seine Gesamtgrenze lautet jetzt60+300+90+120=570Sekunden. Diese Änderung entspricht dem getrennten Produktvertrag. Unbelegte Aktivität setzt keine Frist zurück.

Der erste Gast wurde geordnet beendet. Der Launcher wartet künftig nur auf den Runner-Prozess, nicht auf dessen gesamten Kindprozessbaum. Bekannte eigene Nachläufer werden getrennt erfasst und geschlossen. Ein leerer Host-Prozessbeleg bestätigt keinen Fortbestand historischer Host-PIDs.

Aktueller gemeinsamer A/B-Publish und tatsächlicher Gegenlauf sind noch offen. UI-/Lizenzsatz ist für diesen Publish vollständig kompiliert.

## Gegenlauf mit aktuellem Paar

Root hat `T8/guest-release-20261010-221838-070/guest-output/Release/release-summary.json` gelesen:6/7bestanden,1Fehler,0Skip. Alle bisherigen Fehler-/Recoveryfälle und der Parallelstart bestehen. `1044/two-starts.json` bestätigt einen B-Writer mit der aktuellen SHA0974B27F...A1E91, tatsächliches Minimieren, Wiederherstellen und Vordergrund-HWND393582. Kein Restore-Fix ist erforderlich. Der eigene erfolgreiche1044-Scope ist entfernt,0Kill.

Der normale A/B-Fall1042 scheitert an einem echten Vor-Accept-Pipefehler. Kandidat3816 bleibt Started/HWND0. ERROR_NO_DATA tritt vor server.connected auf. Das ist ein neuer Zustand vor dem früher reparierten angenommenen Verbindungsfehler. Der Test beendet den korrekten Warteabschnitt; neue Fristen sind nicht gerechtfertigt.

Der fehlgeschlagene Scope und Prozess3816 sind ausdrücklich als nicht geordnet bereinigt dokumentiert. Der Gast selbst ist beendet, keine eigenen Clients übrig, kein Kill. Daraus wird kein erfolgreicher App-Cleanup abgeleitet.

Ein echter roter Pipefall und enger Reset-/Listenerstart-Fix laufen. Danach sind normaler A/B-Wechsel und der direkt betroffene Parallel-/Aktivierungsfall erneut auszuführen. Die bereits gültigen Fehlerpfade werden nur bei einem betroffenen neuen Befund wiederholt. T8 bleibt offen.
