# Kamerasitzung

`CameraSession` besitzt genau einen VISCA-Controller. `ConnectAsync` verwendet den konfigurierten Host und VISCA-Port. Die Sitzung gilt erst nach einer gültigen Antwort auf GetPowerStatus als verbunden. Ein erneuter Verbindungsaufbau stoppt bestätigte manuelle Bewegung und gibt den vorherigen Controller frei.

`ReadPositionAsync` liest Pan/Tilt einmal und anschließend Zoom. `RecallAsync` sendet für Leser, Tisch und Redner zuerst absolute Pan/Tilt-Werte mit 12/12 und danach Zoom. Home sendet ausschließlich GoHome. Ein nicht eingerichtetes normales Ziel erzeugt keine Pakete. `RecallAsync` liefert false bei fehlendem Ziel oder einem bereits laufenden Auftrag. Ein Geräte-, Transport- oder Timeoutfehler erzeugt CameraOperationException. Ein durch den Caller angeforderter Abbruch erzeugt OperationCanceledException. Fehler trennen die Sitzung.

`ManualControlMap` übernimmt die vorhandene WPF-Berechnung: `(position/size*2-1)*(maximum+1)`, in einen int abgeschnitten und auf den Betrag begrenzt. Pan/Tilt verwenden 24, Zoom verwendet 7. Tilt kehrt das Vorzeichen der Bildschirmkoordinate um. Die Aufrufer müssen eine positive Fläche und endliche Koordinaten übergeben.

`SetPanTiltAsync` und `SetZoomAsync` halten je Achsengruppe höchstens einen ausstehenden Zustand. Eine laufende Übertragung wird abgewartet. Weitere Eingaben ersetzen den ausstehenden Zustand. Die zurückgegebenen Tasks warten auf den gemeinsamen Durchlauf; sie behaupten nicht, dass jede Zwischenposition übertragen wurde. Identische Zustände erzeugen keinen zusätzlichen Befehl.

`StopAsync` entfernt ausstehende Fahrzustände. Der Dienst wartet auf einen bereits übertragenen Befehl und sendet dann den nötigen Stopp. Bis zum bestätigten Stopp werden neue Bewegungen abgewiesen. Ein Stopp ohne bestehende Verbindung kann keinen Erfolg melden. Ein fehlgeschlagener Stopp trennt die Sitzung und liefert einen Fehler. Der Dienst behauptet keine physische Kamerabewegung oder Position; IsMoving bezeichnet den laufenden manuellen Auftrag.

`Changed` kann auf einem Workerthread ausgelöst werden. Der Windows-Adapter muss den Dispatcher verwenden. Er kann IsConnected, IsBusy, IsMoving, PanSpeed, TiltSpeed, ZoomSpeed und LastError lesen. Ereignisbehandler müssen vor dem endgültigen Fensterschluss entfernt werden.

Der Caller führt den Schließ- und Updateablauf in dieser Reihenfolge aus:

1. Neue Bedienaufträge sperren.
2. Das Caller-Token eines laufenden Recall-/Read-Auftrags abbrechen.
3. Diesen Auftrag vollständig abwarten und seinen Abbruch behandeln.
4. Bei noch bestehender Verbindung nötigen manuellen Stopp abwarten.
5. DisconnectAsync und DisposeAsync abwarten.

Nach einem abgebrochenen exklusiven Auftrag ist die Sitzung bereits getrennt. In diesem Zustand darf der Caller keinen erfolgreichen Stopp anzeigen. DisconnectAsync weist einen noch laufenden exklusiven Auftrag zurück; der Caller muss ihn zuerst beenden. DisposeAsync gibt den Controller auch bei einem solchen Aufruffehler frei. Die geplanten GUI-Schließszenarien bleiben Teil der T6-/T8-Integration.

Prüfbefehl: `pwsh -NoProfile -File scripts/Test-Offline.ps1 -Suite Controls`. Die Suite verwendet die unveränderten festen Pakete unter `tests/fixtures/visca/packets.tsv` und einen eigenen Build-Ausgabeordner unter `.worktools/test-artifacts/Controls/`. Die moderne VISCA-Bibliothek bleibt bis zur technischen T3-Freigabe net6.0.
