# Bedienung der WinUI-App

Stand: 13.10.2026. Diese Anleitung beschreibt Kandidat 1.0.0.6. Der Fotoablauf wurde mit einem eigenen lokalen Livebild- und VISCA-Testserver geprüft. Aufnahme, Bestätigung, Abbrechen und Datenbestand nach Neustart sind dabei die Pflichtfälle. Höhere DPI-Stufen, Hochkontrast und Betrieb mit einer realen Kamera bleiben offen. Der [Freigabestand](freigabestand.md) nennt die Belege und Grenzen.

## EXE starten

Für die vorgesehene Windows-11-x64-Lieferung wird `CameraControl.WinUI.exe` gestartet. Ein gesondertes Startskript gehört nicht zum Bedienablauf. Die veröffentlichte EXE enthält ihre Laufzeit und die Videodateien im Bundle. Der erste Start kann durch das Entpacken dieser Dateien länger dauern.

Beim normalen Release-Erststart wird die Programmversion in den eigenen Benutzerordner übernommen:

```text
%LOCALAPPDATA%\Programs\Versammlungsptzsteuerung\Versions\<Version>\CameraControl.WinUI.exe
```

Nach erfolgreicher Aktivierung zeigt der Startmenüeintrag **Versammlungs-Kamerasteuerung** auf diese lokale Version. Der weitere Start erfolgt über diesen Eintrag oder die Windows-Suche. Die ursprüngliche Download-Kopie kann danach entfernt werden. Erststart und Suchstart nach Download-Löschung wurden mit Version 1.0.0.3 im Windows-11-Gast geprüft.

Eine neue veröffentlichte EXE wird normal gestartet. Auch ein Downloadname wie `CameraControl.WinUI (1).exe` ist zulässig. Eine höhere Dateiversion wird zunächst vorbereitet. Die bisherige Instanz muss geordnet schließen. Erst nach erfolgreichem Start und Aktivierung übernimmt die neue Version den Programmeintrag. Bei einem fehlgeschlagenen Wechsel bleibt die bisherige Aktivierung erhalten oder wird wiederhergestellt. Derselbe oder ein älterer Versionsstand führt zur aktiven lokalen Version. Ein erneuter Start aktiviert das vorhandene Fenster und stellt es bei Bedarf aus dem minimierten Zustand wieder her. Es gibt keinen automatischen Download neuer Versionen.

Ein Entwicklungsbuild und eine ausdrücklich gestartete QA-Instanz verwenden eigene Daten und einen eigenen Programmbereich. Sie sind nicht die normale Produkteinrichtung.

## Verbindung einrichten

Beim ersten Start ist **Positionen** geöffnet. Der Host und die RTSP-URL sind leer. Es startet keine Verbindung zu einer realen Kamera.

1. **Einstellungen** öffnen.
2. **Host oder IP-Adresse** der Kamera eintragen.
3. Den **VISCA-Port** des Geräts eintragen. Der Anfangswert 5678 ist ein editierbarer Standardwert. Er ist keine Zusicherung für das angeschlossene Gerät.
4. Für Video die **vollständige RTSP-URL** eintragen. Sie enthält auch den erforderlichen Pfad. Die Vorschau verwendet `rtsp://`. Zugangsdaten dürfen nicht in der URL stehen.
5. Bei Bedarf **Zugangsdaten (optional)** aufklappen und Benutzername und Passwort eintragen. Neue Zugangsdaten benötigen beide Werte. Ein leeres Passwort erhält bereits gespeicherte Zugangsdaten.
6. **Speichern und verbinden** wählen. Die Aktion speichert die Daten und öffnet die Steuerungsverbindung.

**Verbindung prüfen** unter **Weitere Optionen** prüft die eingetragenen VISCA-Daten mit einer getrennten kurzen Sitzung. Diese Aktion speichert die Werte nicht und ersetzt nicht die aktive Steuerungsverbindung. Beim nächsten normalen App-Start versucht die aktivierte App, eine eingerichtete Steuerungsverbindung zu öffnen. Video startet erst bei Bedarf.

Wird Host oder VISCA-Port gespeichert geändert, wird eine bestehende alte Steuerungsverbindung getrennt. Neue Positionsaufrufe bleiben bis zu einer gültigen Steuerungsverbindung gesperrt. Während eines Kamera- oder Fotoauftrags können die Verbindungsdaten nicht geändert werden.

Der Status der Steuerung und der Status der Vorschau stehen getrennt in der Oberfläche. Eine gestörte Vorschau beendet nicht allein eine funktionierende Steuerungsverbindung.

## Vier Positionen verwenden

**Positionen** enthält vier gleich große Bildtasten in zwei Zeilen und zwei Spalten:

| Position | Wirkung |
| --- | --- |
| Leser | Gespeicherte Pan-, Tilt- und Zoomwerte aufrufen |
| Tisch | Gespeicherte Pan-, Tilt- und Zoomwerte aufrufen |
| Redner | Gespeicherte Pan-, Tilt- und Zoomwerte aufrufen |
| Home | Nativen Home-Befehl der Kamera senden |

Die drei normalen Namen können in den Einstellungen geändert werden. Die vier Zuordnungen bleiben fest. Home behält seinen Namen und hat keine lokalen Zielwerte.

Eine normale Position ohne Zielwerte zeigt **Nicht eingerichtet**. Eine nicht verbundene Kamera sperrt Positionsaufrufe. Ein fehlendes oder defektes Foto zeigt einen Platzhalter. Ein eingerichteter Zielwert benötigt kein Foto.

**Letzter Auftrag** bezeichnet den zuletzt abgeschlossenen App-Aufruf. Dieser Text bestätigt keine aktuelle physische Kameraposition.

## Kamera manuell bewegen

**Steuerung** enthält ein Pan/Tilt-Steuerfeld und ein Zoomfeld. Die Zeigerposition bestimmt Richtung und Geschwindigkeit. Weiter vom Mittelpunkt bedeutet höhere Geschwindigkeit. Das Zoomfeld verwendet links und rechts für die beiden Zoomrichtungen.

Loslassen beendet die manuelle Bewegung. Verlust der Eingabeerfassung, Seitenwechsel, Deaktivierung und geordnetes Schließen lösen ebenfalls einen Stopp aus. **Stopp** sendet den erforderlichen Stoppauftrag. Die App wartet auf dessen Protokollbestätigung. Eine fehlende Bestätigung wird als Fehler angezeigt.

Im fokussierten Pan/Tilt-Feld können die Pfeiltasten verwendet werden. Im Zoomfeld werden Links und Rechts verwendet. Beim Loslassen endet der betreffende Tastaturauftrag.

Zum Nachjustieren im Controller die gewünschte **Ansicht** wählen. **Aufrufen** fährt das gespeicherte Ziel an. Danach die Kamera manuell ausrichten und die Bewegung beenden.

**Foto** nimmt ein Bild auf. Der Dialog **Foto für „<Name>“ speichern?** zeigt dieses Bild und nennt die gewählte Ansicht. **Speichern** legt Bild und Kameraziel gemeinsam ab. Es gibt keinen zweiten Speicherschritt in den Einstellungen. **Abbrechen** erhält den bisherigen Stand. Bei Home wird nur das Foto gespeichert; der native Home-Befehl bleibt unverändert.

Für Fotos muss unter **Einstellungen → Verbindung** die vollständige **Livebild-Adresse** eingerichtet sein. Eine normale Ansicht benötigt außerdem eine gültige Kameraverbindung. Während Aufnahme, Bestätigung und Speicherung bleiben Auswahl und neue Bewegung gesperrt.

Ein breites Fenster zeigt Positionsraster und Controller nebeneinander. In einem kleinen Fenster bleiben **Positionen** und **Steuerung** getrennte Seiten. Die Auswahl bleibt beim Wechsel der Fenstergröße und bei der Navigation erhalten. Ein Größenwechsel während einer gehaltenen Bewegung löst einen Stopp aus.

## Ansichten benennen

1. **Einstellungen → Ansichten** öffnen.
2. Die gewünschte **Ansicht** wählen.
3. Den **Namen** ändern.
4. **Name speichern** wählen. Erst diese Aktion speichert den Namen. Home behält seinen festen Namen.

**Foto in der Steuerung aufnehmen** wechselt zur Steuerung mit derselben Auswahl. Diese Aktion nimmt kein Foto auf. Dort wird die Aufnahme mit **Foto** gestartet. Numerische Zielwerte und getrennte Foto-/Positionsentwürfe sind nicht Teil des Einstellungsablaufs.
## Vorschau verwenden

Die globale Aktion **Vorschau** bleibt auf allen drei Seiten verfügbar. Sie öffnet oder aktiviert ein separates Fenster. Wiederholtes Öffnen verwendet dasselbe Vorschaufenster und denselben Player. Die Vorschau beginnt erst beim Öffnen oder bei einer Fotoaufnahme.

Im Vorschaufenster verbindet **Erneut verbinden** den gespeicherten Stream erneut. Das Schließen des Vorschaufensters beendet dessen Stream. Das Schließen des Hauptfensters beendet auch Vorschau und Steuerung geordnet.

## Daten und Fenster

Die WinUI-App verwendet:

```text
%LOCALAPPDATA%\Versammlungsptzsteuerung\Data\settings.json
%LOCALAPPDATA%\Versammlungsptzsteuerung\Data\Images\
```

Die JSON-Datei enthält Verbindung, Positionsnamen, Zielwerte, Bildverweise und Fensterzustände. Passwörter stehen in der Windows-Zugangsdatenablage. Die JSON-Datei enthält nur einen Verweis. **Gespeicherte Zugangsdaten entfernen** entfernt diesen Verweis und die eigene gespeicherte Zugangsdatenzuordnung.

Alte Werte aus der WPF-`App.config` werden nicht übernommen. Bei einer ungültigen oder unbekannt versionierten Einstellungsdatei wird der Fehler angezeigt. Die Datei wird nicht still überschrieben. **Neu einrichten** ist die ausdrückliche Aktion für neue leere Daten; dabei wird die bestehende Einrichtung zurückgesetzt.

Das Hauptfenster startet ohne gespeicherten Zustand oben links mit kleinem Abstand zur Arbeitsfläche. Mit 1.0.0.6 wurden bei 100 Prozent echte Fensterbilder bei 600 × 430, 960 × 560 und 360 × 280 äußeren Pixeln erfasst. Bei kleinem Fenster sind Teile der Bedienung über Scrollen und das Navigationsmenü erreichbar. Gespeicherte Fenster werden an die vorhandene Monitor-Arbeitsfläche angepasst. Darstellung bei 125/150/200 Prozent und Hochkontrast wurden im begrenzten Gastlauf nicht nachgewiesen.


