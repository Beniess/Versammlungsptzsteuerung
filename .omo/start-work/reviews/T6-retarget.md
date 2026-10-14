# T6-retarget: gezielter Review

Der moderne Bibliothekspfad ist net10.0. Der netstandard2.0-Pfad bleibt vorhanden. Root hat den Ergebnisbericht und den tatsächlichen Frameworknachweis gelesen: net481 lädt .NETStandard,Version=v2.0; net10 lädt .NETCoreApp,Version=v10.0.

Die Referenzläufe bestehen mit 92 und 63 Tests. Die 149 AppCore-Fälle bestehen gegen den neu gebauten net10-Bibliothekspfad. Keine Fälle sind übersprungen. Alle 14 C#-Protokollquellen und die feste Wire-Fixture sind unverändert. Der einzige geänderte Verhaltenstestwert ist der erwartete Frameworkname; die Paketassertions bleiben erhalten.

Belege: .omo/evidence/winui3-modernisierung/T6-retarget/result.md und actual-frameworks.json. 31 Testports sind frei, 102 temporäre Ordner sind entfernt, kein Testprozess bleibt aktiv.

Dies ist keine vollständige T6-Freigabe. Pointer Capture, Seitenwechsel, Deaktivierung und geordnetes Schließen müssen mit der gebundenen WinUI-Oberfläche geprüft werden.
