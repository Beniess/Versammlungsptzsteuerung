# T8-native: gezielter Review

Root hat den Startup-Vertrag, DeploymentScope, DeploymentPipeServer und WindowsDeploymentRuntime gelesen. Die tatsächlichen finalen TRX-Zähler sind total=29, executed=29, passed=29. Der Bericht bestätigt 29 entfernte QA-Ordner und keine verbleibenden Testordner.

Die Tests verwenden echte Named Pipes und echte COM-Shell-Links. Der nachgewiesene Callbackfehler wurde gezielt behoben: Commit/Activate/Exit bleiben nach erwarteten InvalidOperation-/Zugriffsfehlern ansprechbar. Sechs neue Fälle wurden zuerst rot ausgeführt. Es gibt keinen allgemeinen Catch-All. Prozessstart und UI-Dispatcher sind weiterhin ersetzt.

Die Einzelinstanz muss den Pipe-Endpunkt besitzen, bevor Kamera und Schreiben freigegeben werden. Candidate bleibt bis Commit read-only. UI-Ready darf nicht von einer Kameraantwort abhängen. App.xaml.cs bleibt noch außerhalb dieses Nachweises.

Temp/Menu prüft keinen Eintrag in Windows-Suche. Der Suchnachweis braucht einen eigenen QA-Eintrag im tatsächlichen Programs-Ordner oder eine saubere Windows-Sandbox. Vollständiger LocalUpdate- und Startup-Nachweis bleibt offen; T8 bleibt ungeprüft.
