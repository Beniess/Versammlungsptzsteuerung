# T7 integration review

Status: offen. Datum: 10.10.2026.

Geprüft: PreviewCoordinator, PreviewWindow, VideoPlayerService, ApplicationServices, SettingsViewModel, SettingsView, ControlView, ShellViewModel und Startup-Anbindung.

## Gezielte Befunde

1. PreviewWindow.OnClosing ist async void. Ein Fehler aus CloseAsync erreicht keine definierte Fehleranzeige. DisposePlayerAsync und VideoPlayerService.DisposeAsync beenden bei einem StopAsync-Fehler die weitere Freigabe. toolchain_setup erstellt zuerst einen engen Fehlerfall und sichert danach die Freigabe.
2. ShellViewModel.RunAsync behandelt keine TimeoutException oder COMException. Der Video-Initialisierungsablauf kann TimeoutException liefern. ApplicationServices.CloseCoreAsync lässt mehrere Foto-Fehler aus Task.WhenAll vor der weiteren Freigabe austreten. compact_ui erstellt dafür gezielte Tests und ergänzt die Fehlergrenzen.
3. LocalDeployment bereinigt bisher das Journal und vorbereitete Links. Die im Plan geforderte Entfernung nicht mehr laufender eigener alter Programmversionen fehlt. winui_video_plan schließt den Punkt nach den echten A/B-Fällen mit Pfad- und Prozessprüfungen ab.

## Bestätigter Aufbau

- Die Vorschau besitzt einen Player und ein eigenes Fenster.
- Attach verbindet Vorschau und Credential-Adapter vor der Bedienfreigabe.
- Video wird erst bei Bedarf geöffnet. Ein decodiertes und dargestelltes Bild ist Aufnahmebedingung.
- Verbindungsänderungen sperren Kamera- und Fotoaufträge. Geänderter Host oder Port trennt die alte Sitzung.
- Der Fotoentwurf wird erst mit Speichern aktiv. Home erhält keinen zusätzlichen Zielwert.
- Der veröffentlichte Start besitzt eine eigene Aktivierungsphase. Eine Testkonfiguration verwendet eigene Pfade.

## Noch erforderliche Belege

- Gemeinsamer Build nach dem vollständigen Startup-/UI-/Preview-Satz.
- Fehlerfall beim Schließen, echte Fotos, zehn Vorschauzyklen und Hauptfensterschluss.
- Layoutmatrix mit tatsächlichen DPI-Werten und Darstellungsthemen.
- A/B-Prozessstarts, minimierte Duplikataktivierung und Shell-/Suchstart.

Ein grüner Adaptertest ist keine vollständige T7-Abnahme.

## Nachprüfung

- Decoder-/Credential-Adapter: T7-adapters-final.trx gelesen, 10/10 bestanden, 0 übersprungen.
- Gemeinsamer Debug-Build laut vollständigem Worker-Beleg grün; native Bedienprüfung bleibt offen.
- UI-Fehlergrenze behandelt TimeoutException und COMException mit lesbarem Text.
- Services-Fälle: connection-close-error-green.trx gelesen, 33/33 bestanden. Der ergänzte gehaltene Fotoauftrag nutzt CapturePhotoAsync, Home und IPositionPhotoSource. Schließen cancelt den Token und wartet vor Dispose. held-photo-close-green.trx: 3/3 bestanden. Der frühere Reflection-Test ist ersetzt.
- Foto-Fehler werden im Preset-Workflow als PhotoCaptureException ausgegeben. Diese Prüfung bestätigt die Auftragslebensdauer; die gemeinsame UI-Fehlergrenze behandelt zusätzlich den direkten Vorschau-Timeout.
- Player-Freigabe und echte T7-Fensterzyklen sind noch offen.
- Player-Freigabe nachgeprüft: VideoShutdown sichert Detach und Release in verschachtelten finally-Blöcken. PreviewWindow und VideoPlayerService verwenden diesen Pfad. OnClosing meldet den Fehler über den Vorschau-Status des Hauptfensters. T7-shutdown-red.trx und T7-shutdown-green.trx liegen vor; Grünlauf 13/13 (3 Shutdown, 10 Adapter). Echte T7-Fensterzyklen bleiben offen.

## Native Prüfung am 11.10.2026

`T7/guest-photos-20261010-221145-219-fcce5a43/guest-output/` enthält drei ausgeführte Fälle, drei Fehler und keine übersprungenen Fälle. Der Fotofall öffnet ein echtes Vorschaufenster, erreicht aber noch keine gespeicherte PNG. Der Beobachter findet `DraftTarget` nicht. Der Zyklusfall erreicht `playing` nicht. Root hat im MediaMTX-Log des ersten Falls nur den Publisher und keinen Leseclient gefunden. Die nächste Prüfung sichert daher Initialisierung, tatsächliche Fehleranzeige und UIA-Baum vor dem Schließen. Es ist noch kein Decoder- oder Netzwerkfehler bestätigt.

Der Bibliotheksaustausch erwartete eine separate `LibVLCSharp.pri`. Die veröffentlichte App verwendet die zusammengeführte `CameraControl.WinUI.pri`. Der isolierte QA-Merge in `T7/library-replacement/app-pri-merge/summary.json` rekonstruiert die originale PRI bytegleich. Die neue PRI erhält 670 Ressourcen. Nur `Generic.xbf` enthält die Testmarkierung. 669 Ressourceneinträge, ResourceMap, Qualifier und 19 Eingabedateien bleiben unverändert. Der tatsächliche Austausch und die sichtbare Markierung sind damit noch nicht geprüft.

Der gezielte Gegenlauf `T7/guest-photos-20261011-122308-981-6788b11a/guest-output/` mit unveränderter B speichert bereits ein echtes Leser-Foto (Frame 180, 00:06). Root hat die PNG und den Fotoeditor gelesen und den Editor dem Nutzer gezeigt. Der Editor zeigt Ziel Pan -1, Tilt 1, Zoom 4660 und den gespeicherten Stand. Der nächste Fehler liegt in `OwnPreview`: Die Titelsuche findet kein Fenster. Die Prüfung verwendet künftig den belegten eigenen Preview-HWND mit PID-Prüfung.

Root hat außerdem einen konkreten Wartefehler gefunden: `NativeQaSession.Find` wirft bei einem noch fehlenden Element sofort `AssertionException`. Der Foto-Wait ruft diese Methode auf, bevor `DraftTarget` sichtbar sein muss. `WaitUntil` fängt diesen Fehler nicht und beendet den Fall damit vor Ablauf der Frist. Der Bearbeiter ersetzt nur diesen asynchronen Beobachter durch eine nullable Elementsuche bei gleicher Frist. Keine Änderung des Videoprodukts ist dafür erforderlich.

## Aktuelle B: Teilabnahme Foto und Austausch

`T7/guest-photos-20261011-124331-915-5985f8af/guest-output/` verwendet B `5CB45B0505B523C38272CCA4D5CFA2D1AD43521C8C866B9CE8E7703CA767A8CC`. Drei Fälle wurden ausgeführt, keiner übersprungen. Fall 1 und Fall 3 sind bestanden.

- Drei echte nummerierte Stream-PNGs sind Leser, Tisch und Redner zugeordnet. Jeder Eintrag hat Ziel (-1, 1, 4660). Ein Neustart erhält alle drei Bildhashes. Root hat Ergebnis-JSON und Neustart-PNG gelesen und das Positionsbild gezeigt.
- Die geänderte LibVLCSharp-DLL ist im eigenen Prozess 1804 aus dem isolierten Cache geladen. Der tatsächliche Hash lautet `43E2C6397E5080F61D93C47DA7C6317D7EF2EE3BA0DF410C82277F6914DA8505`. DLL und vollständige App-PRI wurden geprüft ersetzt. Root hat den sichtbaren Marker `DLL-AUSTAUSCH QA` im laufenden Videobild gelesen. Eine echte PNG wurde gespeichert; nach Schließen sind Player- und Leserzahl null.
- Fall 2 erreicht playing, einen Player und einen RTSP-Leser. Er scheitert danach beim Schreiben optionaler Diagnosemetadaten: Ein Video-Descendant unterstützt `IsOffscreen` nicht. Nur diese Metadaten werden mit `ValueOrDefault` gelesen. Die eigentlichen zehn Zyklen, Fehlerfälle und der Hauptfensterschluss bleiben bis zum gezielten Gegenlauf offen. Fall 1 und Fall 3 müssen dafür nicht wiederholt werden.

## T7 abgenommen

Der gezielte Gegenlauf `T7/guest-photos-20261011-125604-078-d2c9ccfb/guest-output/` besteht mit derselben B5CB45... den noch offenen Fall in 154916 ms, 0 Skip. Root hat Ergebnis, zehn vollständige Lifecycle-Einträge, die tatsächlichen Vorschau-PNGs und den geprüften Fehler-/Schließpfad gelesen.

Zehn Öffnen-/Foto-/Schließen-Zyklen speichern PNG und Zielwerte derselben ID. Danach sind jeweils Player- und Leserzahl null. Zwei echte Fensteraufnahmen zeigen unterschiedliche Frames 179 und 226. Wiederholtes Öffnen hält genau ein Vorschaufenster und einen Reader. 422 native Dateien sind gegen das feste Paket geprüft.

Geänderte Positionswerte während der Aufnahme und der Abbruch des lokalen Streams lassen gespeicherte Bildhashes und Zielwerte unverändert. Das Schließen des Hauptfensters beendet die App mit Exitcode 0, meldet Disposed und null Player/Reader. Der anschließende Neustart erhält die Daten. Zusammen mit Fall 1 und Fall 3 aus dem vorigen Gast ist T7 damit abgenommen. Ein einzelner neuer Vollgast mit drei grünen Fällen wird nicht behauptet. Die reale Kamera bleibt ungeprüft.
