# Versammlungs-Kamerasteuerung — native Gestaltung

Stand: 13.10.2026. Dieser Vertrag gilt für die WinUI-App. Die App nutzt native WinUI- und Fluent-Steuerelemente. Der Vertrag ergänzt den Plan `.omo/plans/winui3-modernisierung.md`.

## 1. Erscheinungsbild und Zweck

Die Oberfläche ist eine kleine Windows-Kamerasteuerung. Vier gleich große Bildtasten stehen im Mittelpunkt. Klare Namen und Statusmeldungen geben die erforderliche Information. Die Oberfläche verwendet die Windows-Systemdarstellung. Sie hat keine eigene dekorative Kontrollbibliothek.

## 2. Farben

Farben kommen aus WinUI-Systemressourcen. `ThemeResource` löst die Ressource für Hell, Dunkel und hohen Kontrast auf. Keine feste RGB-Farbe ersetzt eine Systemressource. Foto- und Video-Pixel sind davon ausgenommen.

| Funktion | WinUI-Ressource oder native Darstellung | Anwendung |
| --- | --- | --- |
| Fensterfläche | `ApplicationPageBackgroundThemeBrush` | Seiten und Rückfall ohne Material |
| Gruppenfläche | `LayerFillColorDefaultBrush` | Nur wenn eine eigene Fläche erforderlich ist |
| Bildplatzhalter | `CardBackgroundFillColorDefaultBrush` | Fehlendes oder defektes Foto |
| Primärtext | `TextFillColorPrimaryBrush` | Namen und Eingaben |
| Sekundärtext | `TextFillColorSecondaryBrush` | Hilfetext und Verbindungsstatus |
| Deaktivierter Text | Native `Button`-Darstellung | Nicht eingerichtete oder gesperrte Position |
| Kontur | `CardStrokeColorDefaultBrush` | Nur für eine erforderliche Abgrenzung |
| Aktion und Fokus | Native `Button`- und `NavigationView`-Darstellung | Windows-Akzent, Zeiger- und Fokuszustände |
| Gewählte Position | `AccentFillColorDefaultBrush` | Schmale Auswahlkontur innerhalb der Bildtaste |
| Information, Warnung, Fehler | Native `InfoBar` mit `Severity` | Betroffene Seite; Text nennt die Ursache |

Die App folgt dem Systemthema. Hoher Kontrast muss Bildtasten, Namen, Fokus und Status lesbar halten. Eine Meldung verwendet Text und ein natives Symbol. Farbe allein übermittelt keinen Zustand.

## 3. Schrift und Symbole

Die App verwendet `ContentControlThemeFontFamily` und native Textstile. Die Plattform wählt die passende Segoe-Systemschrift. Es gibt keine heruntergeladenen Schriftdateien.

| Funktion | Native Grundlage | Regel |
| --- | --- | --- |
| Bedienname und normaler Text | `BodyTextBlockStyle` | Mindestens 14 DIP; normale Systemskalierung |
| Bildtastenname | `BodyTextBlockStyle`, `SemiBold` | Immer außerhalb des Bildes; maximal zwei Zeilen |
| Abschnittstitel in Einstellungen | `SubtitleTextBlockStyle` | Kein großer Seitentitel im kleinen Fenster |
| Hilfetext und Status | `BodyTextBlockStyle` | Zeilenumbruch; keine kleine Pflichtinformation |
| Symbole | `SymbolIcon` oder `FontIcon` mit Systemschrift | Ein einheitlicher Fluent-Symbolsatz |

Lange Namen dürfen keine andere Taste verdecken. `TextTrimming` darf nur bei zusätzlichem vollständigem `ToolTip` und `AutomationProperties.Name` verwendet werden. Formulare verwenden sichtbare Beschriftungen. Ein Platzhalter ersetzt keine Beschriftung.

## 4. Abstände und Layout

Layoutmaße stehen in DIP. Fenster- und Bildschirmmaße in physischen Pixeln werden ausdrücklich umgerechnet. Die normale Referenz ist ein Außenmaß von ungefähr 600 × 430 Pixeln auf einem Windows-11-Desktop mit 1920 × 1080 Pixeln.

| Token | Wert | Zweck |
| --- | --- | --- |
| `SpaceTight` | 4 DIP | Symbol zu Text; kleine innere Trennung |
| `SpaceCompact` | 8 DIP | Rasterabstand; kompakter Seitenrand; Formularabstand |
| `SpaceStandard` | 16 DIP | Normaler Seitenrand; Abschnittsabstand |
| `MainMinimumWidth` | 360 DIP | Lesbares Mindestmaß des Hauptfensters |
| `MainMinimumHeight` | 280 DIP | Lesbares Mindestmaß des Hauptfensters |
| `MainTargetWidthPixels` | 600 Pixel | Erster Start bei vorhandener Arbeitsfläche |
| `MainTargetHeightPixels` | 430 Pixel | Erster Start bei vorhandener Arbeitsfläche |
| `PositionsHostMinimum` | 344 DIP | Zwei 160-DIP-Kacheln, 8-DIP-Lücke und 8-DIP-Seitenränder |
| `ControllerHostMinimum` | 440 DIP | Zwei 200-DIP-Steuerfelder, 8-DIP-Lücke und native Seitenränder |
| `CombinedHostGap` | 16 DIP | Abstand zwischen Positionsraster und Controller |
| `CombinedHostMinimum` | 800 DIP | Summe der beiden Mindestbreiten und des Abstands |

- Das Hauptfenster nutzt die normale Windows-Titelleiste. Verschieben, Minimieren, Maximieren und Schließen bleiben native Funktionen.
- Beim ersten Start steht das Fenster oben links mit 16 DIP Abstand zur Arbeitsfläche. Eine gespeicherte Position hat danach Vorrang.
- Drei feste Ziele stehen in der oberen Navigation: Positionen, Steuerung, Einstellungen. Eine dauerhaft offene Seitenleiste ist ausgeschlossen. Die Startseite ist immer Positionen.
- Unter 800 DIP nutzbarer Shellbreite bleiben Positionen und Steuerung eigene Ziele. Ab 800 DIP zeigen beide Ziele denselben gemeinsamen Bedienbereich: Positionsraster links, Controller rechts. Einstellungen bleibt eine eigene Ansicht. Bei 600 × 430 Pixeln gilt das schmale Layout; bei 960 × 560 Pixeln gilt bei 100 % das gemeinsame Layout.
- Die App besitzt genau eine Positions- und eine Controller-Instanz. Vor jedem Layoutwechsel mit Reparenting und vor einem Seitenwechsel wird die aktive Eingabe gestoppt. Die gewählte ID bleibt im einzigen Positionsworkflow erhalten.
- Die globale Aktion `Vorschau` bleibt auf jeder Seite erreichbar. Die Befehlsfläche nutzt einen nativen `CommandBar`. Bei engem Platz sind native Überlaufaktionen zulässig. Tooltip und AutomationName nennen jede Symbolaktion.
- Die Positionsseite nutzt genau zwei Zeilen und zwei Spalten mit gleichem Maß und `SpaceCompact` als Abstand. Reihenfolge: Leser, Tisch, Redner, Home. Die Seite hat keine Scrollleiste.
- Der verfügbare Platz bestimmt die Bildfläche. Ein Foto nutzt `Stretch="Uniform"`. Das ganze Bild bleibt sichtbar. Die Textfläche wird vor der Bildfläche geschützt.
- Unterhalb von 480 DIP verfügbarer Breite nutzt die Seite `SpaceCompact` als Außenrand. Darüber nutzt sie `SpaceStandard`. Einstellungen dürfen bei engem Platz in den nativen Navigationsüberlauf wechseln.
- Steuerung und Einstellungen besitzen je einen vertikalen `ScrollViewer`. Es gibt keine verschachtelte vertikale Scrollfläche.
- Das Startmaß ergibt sich aus dem Pixelziel und der aktuellen Skalierung. Bei 200 % ist das Mindestmaß wichtiger als das Pixelziel. Die Mindestgröße und gespeicherte Größe werden auf die aktuelle Monitor-Arbeitsfläche begrenzt.
- Gespeicherte Fensterpositionen auf entfernten Monitoren werden in eine vorhandene Arbeitsfläche verschoben. Kein wiederhergestelltes Fenster darf vollständig unsichtbar sein.
- `WindowPlacement.X/Y/Width/Height` speichern äußere Fenstergrenzen in physischen Bildschirm-Pixeln. `Scale` speichert das Verhältnis zur DIP-Größe, zum Beispiel `1.25`. Beim Wiederherstellen wird die Größe mit `neue Skalierung / gespeicherte Skalierung` umgerechnet. Danach gelten Mindestmaß und Arbeitsfläche. Die Bildschirmposition wird auf die gewählte Arbeitsfläche begrenzt.

## 5. Native Bausteine

### Bildtaste

Ein nativer `Button` enthält ein `Grid` mit Bildbereich und Textbereich. Der ganze Button ist anklickbar. Der native Button stellt Zeiger-, Druck-, Fokus- und Sperrzustände bereit. Es gibt keinen zusätzlichen äußeren Kartenrahmen.

Der Text enthält den Anzeigenamen. Bei fehlendem Zielwertsatz lautet der sichtbare Zustand `Nicht eingerichtet`. Bei fehlendem oder defektem Bild erscheint ein neutraler Platzhalter. Der Platzhalter blockiert keine eingerichtete Fahrt. Drei normale Namen sind editierbar. Home bleibt Home.

Die Auswahl bezeichnet die Position für den nächsten Aufruf oder Speichervorgang. Sie bezeichnet keine bestätigte Kameraposition. Bildtaste, Controller und Detailseditor verwenden dieselbe ID. Eine Auswahlkontur und der native Positionswähler zeigen diese ID. Während einer Fahrt sind weitere Positionsaufrufe gesperrt. Nicht verbundene oder nicht eingerichtete Positionen senden keinen Kamerabefehl. Home benötigt für den Aufruf eine Steuerungsverbindung.

### Navigation und Befehle

`NavigationView` verwendet `PaneDisplayMode="Top"` und klare deutsche Namen. Native Überlaufregeln müssen am Mindestmaß geprüft werden. Die globale `Vorschau`-Aktion öffnet oder aktiviert genau ein zusätzliches Fenster. Die Vorschau-Aktion meldet einen fehlenden RTSP-Wert verständlich.

### Steuerung und Formulare

Die Steuerung erhält das bekannte Pan/Tilt-Steuerfeld und das Zoomfeld als native WinUI-Elemente. Die Zeigerposition bestimmt Richtung und Geschwindigkeit nach der gesicherten bisherigen Abbildung. Richtungstasten und ein separater Geschwindigkeitsregler ersetzen diese Bedienung nicht. Loslassen und Verlust der Eingabeerfassung stoppen eine Bewegung. Seitenwechsel, Deaktivierung und geordnetes Schließen lösen ebenfalls einen Stopp aus.

Der Controller zeigt `Ansicht`, den gemeinsamen Wähler, `Aufrufen` und die sichtbare Aktion `Foto`. `Foto` nimmt ein Bild auf und zeigt einen nativen Bestätigungsdialog mit dem Namen der gewählten Ansicht. `Speichern` sichert das Bild und die aktuelle Kameraposition gemeinsam. Home erhält nur das Bild. `Abbrechen` ändert keine gespeicherten Daten. Das Bild bleibt mit `Uniform` vollständig sichtbar. Auswahl und Bewegung sind von Aufnahme bis Bestätigung gesperrt. Es gibt keine getrennte Taste zum Speichern der Position und keinen Fotoentwurf in den Einstellungen.

Einstellungen verwenden zwei native kompakte Pivot-Bereiche: `Verbindung` und `Ansichten`. Jede Seite verwendet 8 DIP Rand, 4 DIP Abstand innerhalb einer Zeile und 8 DIP Abstand zwischen Formularzeilen. Host und Port stehen auch im kleinen Fenster nebeneinander. `Livebild-Adresse` bleibt sichtbar. `Speichern und verbinden` ist die einzige primäre Verbindungsaktion. Optionale Zugangsdaten und erweiterte Aktionen bleiben in nativen Expandern. `Ansichten` enthält den gemeinsamen Wähler, das lokale Namensfeld, `Name speichern` und den Weg `Foto in der Steuerung aufnehmen`. Das Eingeben eines Namens ändert keinen Workflow-Entwurf. Erst `Name speichern` schreibt den Namen. Es gibt keine numerischen Zielwerte, Fotoentwürfe oder zusätzlichen Speicher- und Abbruchaktionen auf dieser Seite. Globale Status-/Vorgangsmeldungen werden nicht als zweite InfoBar wiederholt.

## 6. Bedienung und Bewegung

Native Übergänge bleiben aktiv. Die App fügt keine dekorativen Animationen hinzu. Die Windows-Einstellung für reduzierte Bewegung hat Vorrang.

Tab und Umschalt+Tab erreichen jede freigegebene Aktion in sichtbarer Reihenfolge. Enter oder Leertaste aktiviert einen nativen Button. Die Fokusmarkierung bleibt sichtbar. Navigation und Fensterwechsel dürfen keinen Bewegungsauftrag zurücklassen.

Steuerungs- und Videostatus sind getrennt. Ein Videoausfall sperrt keine funktionierende Steuerung. Fehler stehen in der betroffenen Ansicht. Wiederholte Netzfehler erzeugen keine Folge modaler Dialoge.

## 7. Flächen und Prüfnachweis

Native WinUI-Flächen bestimmen die Tiefe. Normale Steuerelemente behalten ihre Windows-Kontur. Gruppen verwenden Abstand und Schrift. Ein zusätzlicher Rahmen muss eine konkrete Funktion haben. Mica ist optional; die Systemfläche ist der sichere Rückfall. Hoher Kontrast verwendet die Systemdarstellung.

Die Layoutprüfung verwendet die tatsächlich laufende native App und UIA3. Sie prüft Ziel- und Mindestmaß bei 100, 125, 150 und 200 %. Sie prüft Hell, Dunkel, hohen Kontrast, Fokus, leere Daten, lange Namen und Fehler. Screenshots und gemessene Fenster-/Elementgrenzen liegen unter `.omo/evidence/winui3-modernisierung/T5/`.

Die UI-Prüfung verwendet einen eigenen QA-Produktnamen und isolierte Datenpfade. Sie führt keine Produktregistrierung aus. Sie verbindet keine reale Kamera. Ein erfolgreicher Build ersetzt keinen Screenshot und keine Bedienprüfung.

Die Verbindungsfelder verwenden flache 32-DIP-Zeilen mit sichtbaren, über LabeledBy zugeordneten Beschriftungen. Die Labelspalte misst 64 DIP, die Portbeschriftung 32 DIP und das Portfeld 88 DIP. Die Namenszeile verwendet dieselbe Labelspalte. Der Dialog passt Breite und Inhaltsfläche bei XamlRoot-Änderungen an; Speichern und Abbrechen liegen außerhalb seiner einzigen Scrollfläche.
