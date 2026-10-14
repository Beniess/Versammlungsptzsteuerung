# Quellen und Bibliotheksaustausch

## Vorgesehener Lieferweg

Die portable App bleibt eine einzelne EXE. Die EXE erhält eine lesbare Lizenzansicht mit unveränderten eingebetteten Originaltexten. Ein getrenntes Quellenarchiv enthält die korrespondierenden Bibliotheksquellen, die Patches und die Buildrezepte. Dieses Archiv muss zusammen mit dem zugehörigen Release bereitstehen. Ein Verweis auf eine Repository-Startseite ersetzt dieses Archiv nicht.

Das Quellenarchiv enthält:

1. VLC 3.0.24 als unverändertes Release-Archiv, SHA-256 `E7CAB503D1D7D5849B89D2CF0E1EE60D0EF6D012407791B644B9CFC0CC225FDF`. Der Wert stimmt mit der offiziellen `.sha256`-Datei überein.
2. Die benötigten Contrib-Quellarchive. Die festen Versionen, SHA-512-Werte und Patches stehen in `contrib/src` des VLC-Archivs. Kein Archiv darf allein wegen eines ähnlichen Namens oder einer neueren Version ersetzt werden.
3. LibVLCSharp-Quellen aus Revision `4896d0e06d19ac40b46802cf7dc167d432f3fd63`. Das passende Projekt ist `src/LibVLCSharp.WinUI/LibVLCSharp.WinUI.csproj`. Die WinUI-Ausgabe heißt `LibVLCSharp.dll`.
4. Die Lizenztexte und Notices der übrigen tatsächlich enthaltenen Komponenten. Der Dateiindex und die Paket-Prüfsummen liegen im zugehörigen Release-Nachweis.
5. Einen festen App-Quellstand mit `global.json`, Projektdateien, NuGet-Lockdateien und `scripts/Publish-App.ps1`, falls der unten beschriebene vollständige Buildweg als Austauschweg verwendet wird. Vor einer externen Weitergabe müssen die Nutzungsrechte für diesen App-Quellstand feststehen.

## Nachgewiesener VLC-Stand

Die 420 DLL-, Plugin-, Lua- und Datendateien im x64-Paket stimmen mit dem offiziellen `vlc-3.0.24-win64.zip` überein. Dieses ZIP hat SHA-256 `FCF30850371AD10C9373CC4F0F4501E7DEE49E3E9AE9F20C72FB2661A1CA6323`. Die zusätzlich enthaltenen `libvlc.lib` und `libvlccore.lib` sind Importbibliotheken für einen Linker. Sie sind keine Laufzeit-DLLs. `NativeVideoBundle` prüft DLLs und den Plugin-Ordner; `Core.Initialize` lädt den DLL-Pfad. Der App-Code lädt keine `.lib`-Datei. Die Importbibliotheken bleiben im Bundle. Ihr eigener Buildnachweis ist noch offen.

Das binäre `libvlccore.dll` nennt den Host `x86_64-w64-mingw32` und einen Build mit aktivierten VLC-Contrib-Komponenten. Das Release-Archiv enthält 117 Contrib-Rezepte und 328 Patches. Die Rezepte pinnen unter anderem FFmpeg 8.1.2, x264 Revision `e067ab0b530395f90b578f6d05ab0a225e2efdf9` und LIVE555 2026.06.24. Das LIVE555-Archiv wurde bereits mit dem SHA-512 aus dem VLC-Rezept geprüft. Seine Lizenzdateien sind unverändert enthalten.

Die vollständige Auswahl der statisch enthaltenen Contrib-Bibliotheken ist aus dem NuGet-Paket allein nicht unabhängig bestätigt. Deshalb wird ein begrenzter konservativer Begleitsatz geliefert: alle 127 Dateien aus den Release-SHA512SUMS sind lokal vorhanden und bestehen die feste SHA-512-Prüfung. Ihre Gesamtlänge beträgt 713.076.469 Bytes. Vier veraltete DirectX-Header im VideoLAN-Mirror wurden aus den bereits im selben Release-Rezept gepinnten Original-Gitrevisionen bezogen; ihre ursprünglichen Soll-Prüfsummen wurden beibehalten. Hinzu kommen die sechs festen Git-Quellarchive sowie LibVLCSharp aus der Paketrevision. Sie wurden direkt aus den offiziellen Repositories unter der jeweiligen Revision bezogen; ihre lesbaren Archivwurzeln nennen diese Revision und ihre SHA-256-Werte sind gespeichert. Ein zusätzlicher unabhängiger Git-Objektvergleich wird dadurch nicht behauptet.

Der Begleitsatz liegt im mitgelieferten Ordner `Sources` des ursprünglichen Lieferpakets. `Sources/sources-manifest.json` nennt die tatsächlichen Archivdateien mit relativen Pfaden, die SHA-256-/SHA-512-Werte, 18 feste Header und 435 genaue Original-Lizenz-/Notice-Dateipfade in den Archiven. Die 117 Buildrezepte und 328 Patches bleiben unverändert im VLC-Release-Archiv unter `vlc-3.0.24/contrib/src/` erhalten. Der Begleitsatz enthält auch Komponenten anderer Plattformen. Es wird nicht behauptet, dass jedes mitgegebene Rezept in die App gelinkt wurde.

Die ursprünglichen Texte von FFmpeg, x264, zlib und Lua sowie die ursprünglichen Lizenzmetadaten der tatsächlich enthaltenen HRTF-Datei sind zusätzlich im lesbaren Lizenzdateisatz indiziert. FFmpeg enthält separate Originaltexte für LGPL 2.1, LGPL 3, GPL 2 und GPL 3; `LICENSE.md` beschreibt deren Bedingungen. Diese Texte bestimmen ohne den Buildkontext keine pauschale Lizenz des gesamten nativen Bundles.

## Buildweg für geänderte Bibliotheken

Dieser vollständige Neubuildweg verwendet die vorhandene App-Buildpipeline. Er ist vorbereitet, aber noch nicht mit einer geänderten Bibliothek ausgeführt. Der direkte Austausch im folgenden Abschnitt wurde dagegen tatsächlich im isolierten Windows-Gast geprüft.

1. Den freigegebenen festen App-Quellstand in einen eigenen Arbeitsordner kopieren. SDK 10.0.400 gemäß `global.json` verwenden. Die ursprünglichen EXE- und Quellen-Prüfsummen prüfen.
2. LibVLCSharp aus der oben genannten Revision auschecken. Änderungen an der Bibliothek als Änderungen kennzeichnen. Das WinUI-Projekt für `net10.0-windows10.0.19041` und x64 bauen. Seine originale Assembly-Identität und benötigten öffentlichen APIs erhalten. Die Bibliotheksquellen und ihre Builddateien mitgeben.
3. Eine eigene NuGet-Paketversion erzeugen, die geänderte `LibVLCSharp.dll` enthält. In der eigenen App-Kopie nur die entsprechende Paketversion und ihren neuen Lockdatei-Eintrag ändern. Eine originale Lockdatei darf die geänderte Paket-Prüfsumme nicht vortäuschen.
4. Für native Änderungen die exakten VLC- und Contrib-Quellen mit den zugehörigen Patches verwenden. Die passenden x64-DLLs und Plugins als eigenes natives Paket liefern. `libvlc.dll`, `libvlccore.dll`, die benötigten Plugins und die SwapChain-Unterstützung müssen zusammenpassen. Keine Plugin-Auswahl allein nach Dateinamen vornehmen.
5. In der eigenen App-Kopie `scripts/Publish-App.ps1` ausführen. Die Pipeline baut das Objektprogramm mit den gewählten Bibliotheken und erzeugt eine neue EXE. Die neue Paket-Lockdatei, Dateiversion und alle Bibliotheks-Prüfsummen zum Ergebnis speichern.
6. Die neue EXE in einem ausdrücklich isolierten UI-QA-Kontext und mit leerer eigener Datenablage prüfen. Nur einen Loopback-RTSP-Teststream verwenden. Den geladenen DLL-Pfad und die neue Prüfsumme, ein tatsächlich gerendertes Bild, ein PNG-Foto und das vollständige Schließen von Player und RTSP-Leser nachweisen.

Die Lizenzbedingungen dürfen Änderungen an den LGPL-Bibliotheken und deren Fehlersuche durch Reverse Engineering nicht ausschließen. Die genauen Bedingungen stehen in den Original-Lizenztexten.

## Geprüfter direkter Austausch im eigenen QA-Cache

Die EXE entpackt mit `IncludeAllContentForSelfExtract` auch verwaltete Assemblies. Für eine eigene QA-Prüfung kann `DOTNET_BUNDLE_EXTRACT_BASE_DIR` auf einen neuen eigenen Temp-Ordner gesetzt werden. Nach der ersten vollständigen Extraktion und dem geordneten Beenden aller zugehörigen App-Prozesse kann dort eine API-kompatible geänderte Bibliothek ersetzt werden. Der Video-Adapter verwendet den eigenen entpackten `libvlc/win-x64`-Ordner.

Diese Route wurde mit der WinUI-EXE Version `1.0.0.2`, SHA-256 `5CB45B0505B523C38272CCA4D5CFA2D1AD43521C8C866B9CE8E7703CA767A8CC`, in einem eigenen Windows-11-x64-Gast ausgeführt. Der Test verwendete eigene QA-Daten und einen nummerierten Loopback-RTSP-Stream. Er änderte keine Produktionsdaten, keine installierte Bibliothek und keine NuGet-Produktpakete.

1. Die EXE einmal mit neuer eigener QA-Datenablage und neuem `DOTNET_BUNDLE_EXTRACT_BASE_DIR` starten. Danach alle zugehörigen App-Prozesse geordnet beenden. Den tatsächlichen Extraktionsordner und die Original-Prüfsummen sichern.
2. Die kompatibel geänderte `LibVLCSharp.dll` aus der festen Quellrevision bauen. Die originale Assembly-Identität und die benötigten APIs erhalten. Die geprüfte QA-Änderung fügte nur die sichtbare Markierung `DLL-AUSTAUSCH QA` im ursprünglichen Video-Control-Template hinzu. Alle 71 gemeinsamen und Windows-C#-API-Quelldateien blieben unverändert.
3. Die Bibliotheksressourcen vollständig neu in `CameraControl.WinUI.pri` zusammenführen. Die separate `LibVLCSharp.pri` ist in diesem App-Bundle bereits in die App-PRI eingebettet. Sie darf deshalb nicht einfach zur App-PRI umbenannt werden. Der geprüfte Merge behielt die ursprüngliche Eingabeliste, ResourceMap und Qualifier bei. Der Kontroll-Merge mit den Originaldaten war bytegleich zur Original-App-PRI. Von 670 Ressourcen blieb nur `Files/LibVLCSharp/Themes/Generic.xbf` geändert; 669 Ressourcen blieben unverändert.
4. Im beendeten eigenen QA-Cache genau `LibVLCSharp.dll` und die vollständige `CameraControl.WinUI.pri` ersetzen. Beide neuen Datei-Prüfsummen prüfen. Keinen Cache einer laufenden App und keine Produktionsdatenablage ändern.
5. Dieselbe EXE mit demselben eigenen QA-Cache neu starten. Den tatsächlichen geladenen DLL-Pfad und Hash, die sichtbare Markierung, ein echtes Livebild und ein decodiertes PNG-Foto prüfen. Danach Vorschau und Hauptfenster geordnet schließen. Null aktive Player und null RTSP-Leser prüfen und den eigenen Cache entfernen.

Der tatsächliche Prozess lud die geänderte DLL aus diesem eigenen Extraktionsordner mit SHA-256 `43E2C6397E5080F61D93C47DA7C6317D7EF2EE3BA0DF410C82277F6914DA8505`. Die ersetzte vollständige App-PRI hatte SHA-256 `FD2E3933B4A845A6804252E6C36EF38393FCCCF381AE310EC0A9681713DBCBC7`. Die Markierung war im gerenderten Fenster sichtbar. Das Livebild und das 640×360-PNG enthielten die tatsächlichen nummerierten RGB-Testframes. Nach dem Schließen waren null Player und null Leser aktiv; die eigene App endete und ihr QA-Cache wurde entfernt. Dieser Beleg bestätigt die geprüfte kompatible Änderung und diese Route. Er behauptet keine Kompatibilität beliebiger anderer DLLs oder eine Freigabe aller Lizenzzuordnungen.

## Noch offene Release-Prüfungen

- Den konservativen Quellen-Begleitsatz samt Manifest zum endgültigen Release-Hash zuordnen. Die tatsächliche Linkauswahl bleibt als technische Grenze ausgewiesen; zusätzliche Plattformquellen bleiben im Begleitsatz enthalten.
- Den vollständigen Neubuildweg zusätzlich prüfen, falls er als Lieferweg verwendet wird. Der direkte QA-Cache-Austausch ist mit der oben genannten kompatiblen Änderung geprüft.
- Alle benötigten Originaltexte unverändert in die finale EXE einbetten und dort lesbar anzeigen.
- Den finalen EXE-Hash dem Quellenarchiv, den Lizenztexten und dem Bundle-Inventar zuordnen.

Primärquellen: [VLC 3.0.24](https://download.videolan.org/pub/videolan/vlc/3.0.24/), [LibVLCSharp 3.10.1](https://www.nuget.org/packages/LibVLCSharp.WinUI/3.10.1), [Single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview).
