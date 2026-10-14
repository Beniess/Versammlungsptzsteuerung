# Lizenztexte

`index.json` ordnet die verfügbaren Originaltexte den Komponenten zu. `THIRD-PARTY-NOTICES.txt` enthält die Herkunftshinweise. Der Index ist für eine lesbare Lizenzansicht mit unveränderten eingebetteten Originaltexten vorbereitet. Die zugehörige Einbettungsprüfung in der Produkt-EXE ist noch offen.

Die Texte aus NuGet-Paketen wurden unverändert kopiert. Die Dateien mit dem Namen `NOTICE` oder `THIRD-PARTY-NOTICES` bleiben erhalten. Für die Windows-SDK-Lizenz liegt das originale RTF vor. `LICENSE-readable.txt` ist eine daraus erzeugte Textfassung; das Original ist maßgeblich.

Die Zuordnung wurde gegen die B-QA-EXE mit SHA-256 `A123746E2DBC4BBE6CD3F8F8A0D3695A5A12E935098ADED2FC5C9D4D35166385` geprüft. 924 von 953 entpackten Dateien stimmen mit den Paketdateien überein. Die übrigen 29 Dateien sind App-Assemblies, XAML, Ressourcen und Projektdateien. Ein späterer Release muss den Index gegen sein eigenes Bundle prüfen.

VLC enthält unterschiedliche Lizenzgruppen. Die Paketmetadaten `LGPL-2.1-or-later` gelten nicht als Lizenzzuordnung für alle Plugins. Der enthaltene x264-Encoder verweist auf GPL 2 oder später. Die verwendete LIVE555-Bibliothek verweist auf LGPL 3 oder später; ihre LGPL-3-Datei integriert GPL 3. Alle diese Lizenztexte bleiben getrennt erhalten.

`Sources-and-replacement.md` beschreibt den Quellen- und Austauschweg sowie den Stand seiner Prüfung. Der feste konservative Quellenbegleitsatz ist lokal vorhanden: 127 SHA-512-geprüfte Contrib-Dateien, sechs feste Git-Rezeptarchive, LibVLCSharp und das SHA-256-geprüfte VLC-Release-Archiv. Alle gefundenen ursprünglichen Lizenz-/Notice-Dateien sind mit genauem Archivpfad im Quellenmanifest indiziert. Der technische Nachweis und die Texte sind keine pauschale rechtliche Freigabe. Es wurde kein Quellcodeangebot im Namen des Nutzers erstellt. Die Lizenz des eigenen neuen App-Codes wird durch diese Fremdkomponenten-Texte nicht festgelegt.
