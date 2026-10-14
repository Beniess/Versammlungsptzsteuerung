# T10: Lizenzansicht, gezielter Review

Root hat `LicenseCatalog.cs` und `Views/LicenseView.cs` gelesen. Der Katalog lädt die eingebetteten Originaltexte, prüft die indizierten SHA-256-Werte und verhindert relative Pfadsegmente. Die Ansicht zeigt die Version der laufenden EXE. Ein nativer Auswahlkasten wählt Texte; ein ScrollViewer zeigt auswählbaren Text. Originale RTF-Dateien bleiben zusätzlich unverändert eingebettet.

Der Test bestätigt 21 Komponenten und 34 indizierte Originaldateien. Der WinUI-Build hat keine Fehler oder Warnungen. Die Lizenzansicht ist noch nicht im aktuell geprüften A/B-Paar enthalten.

Offen: sichtbare Dialogprüfung bei Mindestgröße, vollständiger Quellenbegleitsatz, Bibliotheksaustausch und finaler Artefaktnachweis. Der vorläufige Quellenstatus muss nach Abschluss an den wirklichen Lieferstand angepasst werden.

Nachprüfung des Quellenbegleitsatzes: Er liegt im zweiten erlaubten Workspace unter `Release/Sources`. Root hat Manifest, Buildinfo und Text gelesen und danach alle117Archive auf SHA-256/Länge sowie alle127festen SHA-512 geprüft. Kein Fehler. Beleg: `licenses/root-source-check.json`. Das Manifest nennt435Original-Notice-Archivpfade,117Rezepte und328Patches. Die Auswahl ist konservativ; nicht jedes mitgelieferte Rezept wird als tatsächlich gelinkt bezeichnet. Ein eigener Git-Objektvergleich wird nicht behauptet.

Der Lizenzsatz enthält jetzt26Komponenten und48indizierteOriginaltexte, einschließlich FFmpeg, x264, zlib, Lua und HRTF-Metadaten. Der Katalogtest prüft Original-SHA und striktes UTF-8. Zusätzliche Herkunfts-/Scope-Felder sind explizit modelliert; unbekannte JSON-Felder bleiben gesperrt. Der portable Quellentext nennt relative Lieferpfade. Der genaue lokale QA-Pfad steht nur im Übergabebeleg.

Weiterhin offen: neuer WinUI-Build, echte Dialogansicht, tatsächlicher Bibliotheksaustausch und Zuordnung zur finalen EXE. Keine vollständige Release-Abnahme aus den Quellenhashes ableiten.
