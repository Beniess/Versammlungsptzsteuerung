# T8-Core: gezielte Prüfung

Root hat LocalDeployment, DeploymentFiles, Adapterverträge und den Red-/Green-Bericht gelesen. Versionen sind numerisch; gleicher/älterer Stand führt nicht zu einem Downgrade. Kopien werden vor Aktivierung über Länge und SHA256 geprüft. Marker und Transaktionen bleiben im eigenen validierten Programmordner. Es gibt keine rekursive Löschung und keine Änderung von Benutzerdaten.

45 Fälle bestehen, keine Überspringung. Die Belege zeigen auch echte rote Randfälle für Teilfehler und Wiederaufnahme. Die Tests verwenden reale Dateien und Dateisperren, aber nur skriptbare Prozess-/Linkadapter.

Core-Teil bestätigt. T8 insgesamt bleibt offen: native IPC, Kandidatenbereitschaft, Einzelinstanz, echte Shell-Verknüpfung und Release-Test fehlen. Adapter müssen nur andere Instanzen beenden, Cancellation beachten und erwartete Betriebsfehler typisiert melden.
