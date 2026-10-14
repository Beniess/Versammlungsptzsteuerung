# T8 startup review

## Vor-Accept-Nachprüfung

Root hat den aktuellen DeploymentPipeServer und den echten Test ClientCloseBeforeAcceptResetsTheSamePipeAndKeepsItsExclusiveClaim gelesen. Vor der Korrektur ist1/1rot. Nach der Korrektur sind52/52DeploymentAdapters/StartupOptions ausgeführt und bestanden,0Skip.

Nur ERROR_NO_DATA vor Accept setzt denselben nativen SafePipeHandle mit DisconnectNamedPipe zurück. Der exklusive Endpunkt bleibt erhalten. Der Test weist einen zweiten Server mit ERROR_PIPE_BUSY zurück und erhält danach eine echte Ready-Antwort über denselben Handle. Der Listener startet mit Task.Run und kann den Fensterkonstruktor nicht mehr durch den sofortigen Vor-Accept-Fehler blockieren. Ein anderer unerwarteter Acceptfehler endet als Fehler und läuft nicht endlos weiter. Die Fristen bleiben gleich.

Der aktuelle Publish enthält diesen Fix. Der native Gegenlauf umfasst den normalen A/B-Wechsel und den direkt betroffenen Parallel-/Aktivierungsfall. Die anderen6/7Ergebnisse des vorherigen Laufs werden nicht als fehlender Gegenlauf ausgegeben. T8 bleibt bis zu diesen beiden tatsächlichen Ergebnissen und der Windows-Suche offen.

Status: offen. Datum: 10.10.2026.

## Echte Prozessprüfung

Erster Lauf: T8/release-20261010-193540-450/results.trx. Root hat die Zähler gelesen: 56 ausgeführt, 54 bestanden, 2 Fehler, 0 übersprungen. Zwei echte Fälle sind rot: normaler A/B-Wechsel und zwei parallele B-Starts. Die anderen fünf echten Fälle bestehen. Alle sieben eigenen Testbereiche sind nach geordnetem Schließen bereinigt.

Der vor Ready abgebrochene Kandidat bleibt read-only. A startet danach wieder aus der echten Verknüpfung. Root hat Prozesspfad, FileVersion, EXE-Hash, Settings-/PNG-Hashes und Microsoft.UI.Xaml.dll aus dem eigenen Bundlecache gelesen. Diese Verknüpfung im Temp-Ordner ist kein Windows-Suchnachweis.

Die gesicherten Startdaten des normalen Wechsels zeigen einen tatsächlichen B-Kandidaten. Er hatte ein bedienbares Fenster, erhielt aber keinen Commit. CandidateExpired beendet ihn. Die Ursache liegt im IPC-Ablauf; der LNK-Start von A ist nicht die festgestellte Ursache.

## Enger IPC-Fehler

T8-broken-pipe-red.trx prüft eine abgebrochene Teilnachricht und danach eine gültige Ready-Anfrage. Der zweite Aufruf erreicht zuerst einen Timeout. Der Server prüfte IsConnected vor Disconnect. Nach EOF ist die Pipe Broken und IsConnected false. Deshalb erfolgte kein Zustandsreset.

Der Fix merkt eine tatsächlich angenommene Verbindung. Nach jeder angenommenen Anfrage erfolgt Disconnect, auch nach EOF, solange die Serverlebensdauer aktiv ist. Die Fristen bleiben unverändert. Root hat den Code und T8-broken-pipe-green.trx gelesen: 50/50 bestanden, 0 übersprungen. Tokenwerte werden nicht protokolliert. QA-Traces enthalten nur Match-Booleans und technische Ablaufzeiten.

## Offene Abnahme

- Die beiden echten B-Fälle nach dem Fix erneut ausführen.
- Minimiertes vorhandenes Fenster durch Duplikatstart wiederherstellen und tatsächlichen Vordergrund-HWND prüfen.
- Bekannte vorherige eigene Release-Datei nach bestätigtem Commit sicher und nichtrekursiv bereinigen. Laufende/abweichende/fremde Dateien erhalten.
- Tatsächlichen Windows-Suchstart auf sauberem Windows prüfen.
- Finale Release-EXE und Lizenzlieferung gegen die Belege abgleichen.

T8 ist noch nicht vollständig abgenommen.

## Nachprüfung der Altversionen-Bereinigung

Root hat RemovePreviousReleaseAsync, TryRetirePreviousAsync und IsRunningAsync gelesen. Der aktive B-Marker und beide eigenen Release-Pfade werden geprüft. A wird nur nach Längen-/Hashprüfung aus einem geöffneten Read-Handle mit FileShare.Delete entfernt. Fremde Zusatzeinträge verhindern die Entfernung. Der leere Ordner wird nichtrekursiv entfernt. Ein Bereinigungsfehler ergibt einen Hinweis, aber keinen Rollback des bestätigten Commit.

Root hat die TRX-Zähler gelesen: T8-retirement-core-green.trx 53/53; T8-retirement-native-green.trx 51/51. Rote Vorläufe und Handoff liegen vor. Der reale neue A/B-Lauf bleibt offen. Die gewünschte sichtbare Nutzerinstanz wird nicht durch diese Prüfung geschlossen.
