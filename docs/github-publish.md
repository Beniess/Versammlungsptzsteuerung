# GitHub-Build und Veröffentlichung

Der Workflow `.github/workflows/dotnet-desktop.yml` baut die WinUI-App als einzelne EXE für Windows 11 x64. Ein PFX-Zertifikat und ein MSIX-Projekt sind nicht erforderlich.

| Auslöser | Ergebnis |
| --- | --- |
| Push auf `main` | Offline-Tests, Windows-Adaptertests und EXE-Download unter Actions |
| Pull Request auf `main` | Dieselben Tests und derselbe Build; kein Release |
| Actions → Run workflow | Manueller Test- und Buildlauf |
| Push eines Tags, zum Beispiel `v1.0.0.7` | Dieselben Prüfungen und ein GitHub-Release-Entwurf |

Ein Release-Tag muss vier Zahlen von `0` bis `65535` enthalten. Die EXE erhält dieselbe vierteilige Dateiversion. Normale CI-Builds erhalten `0.1.<Laufnummer>.<Versuch>`. Sie sind Entwicklungsstände. Sie ersetzen keine bereits installierte höhere Release-Version.

Das Artefakt `Windows-x64-<Version>` enthält die EXE, den festen App-Quellstand, Lizenz- und Bedienungsunterlagen, `build-info.json` und `SHA256SUMS.txt`. Die Prüfsummen beziehen sich auf die tatsächlichen Download-Dateien. Build-Artefakte bleiben 30 Tage verfügbar. Diagnose-Artefakte bleiben 14 Tage verfügbar.

Bei einem Versions-Tag werden diese Dateien zusätzlich an einen Release-Entwurf angehängt. Ergänze dort den passenden Bibliotheks-Quellensatz mit `sources-manifest.json` gemäß [Quellen und Bibliotheksaustausch](../LICENSES/Sources-and-replacement.md). Dieser große Begleitsatz liegt bisher außerhalb von Git. Das App-Quellarchiv ersetzt ihn nicht. Danach kann der Entwurf veröffentlicht werden.

Der Workflow prüft VISCA, Persistenz, Kamerasitzung, Fotoabläufe, Versionswechsel und Windows-Adapter ohne reale Kamera. Sichtbare UI-Abläufe, Livebild, höhere DPI-Stufen und der Betrieb mit einer realen Kamera werden getrennt geprüft. Ein erfolgreicher CI-Lauf ändert den [Freigabestand](freigabestand.md) nicht automatisch.

Die Adaptertests verwenden mindestens acht Workerthreads. Ihr Testprozess betreibt IPC-Client und simulierten Server gemeinsam. Damit warten beide Seiten auf kleinen CI-Runnern nicht auf zusätzliche Workerthreads. Die vorhandenen IPC-Fristen und Testprüfungen bleiben erhalten. Die Einstellung gilt nur für den Testhost.

`.gitattributes` erhält die Originalbytes der Lizenztexte und Testdateien. Git darf ihre Zeilenenden beim Checkout nicht ändern. Die Lizenz-Prüfsummen bleiben unverändert.

Der Windows-Runner besitzt das Windows-SDK 26100. Der Workflow verwendet exakt den SDK-Stand aus `global.json`, damit die NuGet-Lockdatei erhalten bleibt. Die CI setzt dazu ausschließlich in ihrem Arbeitsverzeichnis `rollForward` auf `disable`. Sie lässt den Standardpfad für NuGet-Pakete unverändert. Die GitHub-Actions sind auf feste Commit-IDs gesetzt. Nur der Release-Job erhält `contents: write`; er verwendet das vorhandene `GITHUB_TOKEN`.

Referenzen: [Windows-Runner](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md), [SDK-Auswahl](https://github.com/actions/setup-dotnet#using-the-global-json-file-input), [GitHub-Release-Entwurf](https://cli.github.com/manual/gh_release_create).
