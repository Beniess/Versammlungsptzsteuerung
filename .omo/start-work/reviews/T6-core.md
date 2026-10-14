# T6-Core: gezielte Prüfung

Root hat ManualControlMap, CameraSession, den Bericht und die Testsummen gelesen. Richtung, Geschwindigkeit und Befehlsbytes folgen den unveränderten Referenzen. Die Sitzung hält höchstens den jüngsten ausstehenden manuellen Zustand und verwirft alte Bewegungen beim Stopp. Verbindungen und exklusive Aufträge sind getrennt gesichert.

47 Fälle bestehen, keine Überspringung. Fehler/Abbruch beim Stopp melden keinen Erfolg. Der Caller muss laufende Recall-/Read-Aufträge abbrechen und vollständig abwarten, bevor Disconnect/Dispose folgt. Die drei Testports und Testhosts sind beendet.

Core-Teil bestätigt. T6 insgesamt bleibt offen: echte Pointer-Ereignisse, Seiten-/Fokuswechsel, Retargetierung und Windows-Oberfläche sind noch nicht freigegeben.
