# Positionsentwürfe und Fotoaufnahme

`PresetWorkflow` erhält JsonSettingsRepository, CameraSession, IPositionPhotoSource und die anfangs geladenen Einstellungen. Die drei Abhängigkeiten bleiben im Besitz der Anwendung. DisposeAsync beendet nur den laufenden Entwurfsauftrag. Es trennt weder die Kamera noch die gemeinsame Vorschau.

Die vier festen IDs haben jeweils einen lokalen Entwurf. Select wählt die ID; Rename ändert nur den normalen Namen. Die Entwürfe bleiben bei Auswahl- und Seitenwechsel erhalten. CurrentDraft enthält die Einstellungen, HasNewPhoto und IsDirty. GetDraftImageAsync liefert eine eigene Kopie der neuen PNG-Bytes oder das bisher zugeordnete Bild. Änderungen an zurückgegebenen Vorschau-Bytes ändern den Entwurf nicht.

CapturePhotoAsync öffnet oder aktiviert die Vorschau sichtbar und wartet auf ein decodiertes Bild. IPositionPhotoSource muss dafür denselben Player verwenden, der auch CaptureValidatedPngAsync ausführt. Der Adapter muss die PNG-Datei vollständig schreiben und tatsächlich decodieren, bevor er Bytes zurückgibt. Die AppCore-Prüfung der Signatur, Chunkgrenzen und CRC ersetzt diese Decodierung nicht.

Bei einer normalen Position liest der Ablauf die Position vor und nach der Aufnahme. Beide Werte müssen gleich sein. Ein laufender Kameraauftrag, eine manuelle Bewegung oder eine während der Aufnahme beobachtete Bewegung verhindert die Übernahme. Das Bewegungssignal verwendet Interlocked.Exchange und Volatile.Read, weil CameraSession.Changed auf einem Workerthread eintreffen kann. Erst nach allen Prüfungen werden Bild und Zielwerte gemeinsam in den Entwurf übernommen. Home übernimmt ausschließlich das Bild und führt keine Positionsabfragen aus.

TakePositionAsync funktioniert ohne Video. Bei einem geänderten normalen Ziel entfernt es den bisherigen Bildverweis und neue Foto-Bytes nur aus dem Entwurf. Die gespeicherte Bilddatei bleibt bis zum erfolgreichen Save erhalten. Ein gleicher Zielwert behält das Bild. Home hat keinen lokalen Zielwert.

SaveAsync speichert nur die ausgewählte feste ID. Andere Entwürfe bleiben ungespeichert. Der Ablauf lädt die aktuellen Einstellungen, ersetzt darin diese Position und verwendet die atomare JSON-/PNG-Speicherung des Repositorys. Ein Schreibfehler erhält den aktiven Zustand und den Entwurf für einen erneuten Versuch. Select, Rename und Cancel sind während Save gesperrt. Caller-Abbruch vor dem atomaren JSON-Austausch verhindert die Speicherung. Ist der atomare Austausch bereits abgeschlossen, liefert die Speicherung ihren Erfolg; sie meldet keinen nachträglichen Abbruch für bereits gespeicherte Daten.

Die Kombination aus Load und Save ist kein atomarer Read-Modify-Write über unabhängige ViewModels. Die spätere WinUI-AppContext muss alle Einstellungszugriffe, die schreiben, mit einer gemeinsamen App-Schreibsperre koordinieren. Diese Sperre umfasst Laden, Ändern, Save und das Aktualisieren des gemeinsamen aktiven Modells. Verbindungs- und Fensterupdates dürfen diese Folge während eines Preset-Save nicht überholen. Die Ordnersperre des Repositorys schützt den Dateiaustausch; sie allein verhindert keine verlorenen Modelländerungen zwischen Load und Save.

Cancel verwirft den gewählten Entwurf und bricht eine laufende Aufnahme oder Positionsabfrage ab. Ein spät geliefertes Bild kann den verworfenen Entwurf nicht ändern. Nach DisposeAsync werden keine Changed-Ereignisse mehr veröffentlicht. Die GUI muss Changed über den Dispatcher verarbeiten und vor dem Schließen neue Bedienaktionen sperren.

Videofehler liefern eine sichere PhotoCaptureException ohne die ungeprüfte Fehlermeldung des Video-Adapters. Der Ablauf protokolliert keine URLs oder Zugangsdaten. Ein Videofehler beendet keine bestehende Kameraverbindung. Unbekannte Kamerapufferung und die physische Gleichzeitigkeit von Bild und Zielwerten sind durch diesen Ablauf nicht nachgewiesen.

Prüfbefehl: `pwsh -NoProfile -File scripts/Test-Offline.ps1 -Suite Presets`. Die Suite verwendet eigene temporäre Datenordner und getrennte Build-Ausgaben unter `.worktools/test-artifacts/Presets/`. Der gesamte T7-Nachweis benötigt weiterhin die GUI, den tatsächlichen Video-Adapter und die Vorschauzyklen.
