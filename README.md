# reMedal

**Automatischer NVIDIA ShadowPlay-Import, Rückblick-Stories & Werbefreiheit für Medal.tv (Windows)**

Medal.tv hat sowohl den automatischen ShadowPlay-/Ordner-Import als auch die beliebten „Vor einem Jahr“-Rückblicke aus dem Client entfernt. **reMedal** bringt diese Funktionen als schlanke, ressourcenschonende Windows-Hintergrund-App zurück – ohne zusätzliche FFmpeg- oder .NET-Installation.

---

## Features

- **Automatischer ShadowPlay-Import:** Überwacht deinen NVIDIA ShadowPlay-Ordner per Event-Watcher (~0 % CPU im Leerlauf), wartet bis Aufnahmen komplett fertig geschrieben sind und fügt neue Clips direkt in Medals lokale SQLite-Datenbank ein – **ohne die MP4-Dateien zu duplizieren**.
- **Automatische Spiel-Erkennung:** Ordnet ShadowPlay-Unterordner (z. B. `Counter-strike 2`, `Dark Souls III`, `Tom Clancy's Rainbow Six  Siege`) automatisch der richtigen Medal-Spielkategorie zu (inkl. eingebautem Offline-Katalog mit über 153.000 Medal-Spielen).
- **Rückblicke („Vor X Jahren“) als native Medal-Story:** Blendet auf der Medal-Startseite eine **„Rückblick“**-Story direkt neben *Your story* ein (Standard: `±3 Tage` rund um den Jahrestag aus allen vergangenen Jahren; optional auf nur das letzte Jahr begrenzbar).
  - Zeigt nur deine bei Medal hochgeladenen & zugeschnittenen Clips.
  - Merkt sich bereits gesehene Clips und dunkelt den Story-Ring ab, bis neue Erinnerungen verfügbar sind.
  - Eingebauter Story-Player mit Medal-Style-Steuerung (Titel, gelbe Seek-Leiste, Lautstärke, Qualitätswahl, Speed, **Link teilen**, **Öffnen** & **Vollbild**).
- **Werbung ausblenden (optional):** Blendet eingebettete Werbe-Banner in Medal client-seitig aus.
- **Installer, Uninstaller & Auto-Updates:** Ein-Klick-Installation über Velopack inkl. sauberer Deinstallation über Windows und automatischer Updates über GitHub Releases.

---

## Download & Installation

1. Lade den aktuellen Windows-Installer herunter:
   👉 **[reMedal-win-Setup.exe herunterladen](https://github.com/Reskeyo/reMedal/releases/latest/download/reMedal-win-Setup.exe)**
2. Starte `reMedal-win-Setup.exe`.
   > **Hinweis zu Windows SmartScreen:** Da die App nicht teuer code-signiert ist, zeigt Windows beim ersten Start eventuell *„Der Computer wurde durch Windows geschützt“*. Klicke auf **„Weitere Informationen“ → „Trotzdem ausführen“**.
3. Die App installiert sich ohne Admin-Rechte in dein Benutzerprofil und startet automatisch.

---

## Einrichtung (1 Minute)

1. **ShadowPlay-Ordner prüfen:**
   - Klicke auf **„ShadowPlay suchen“**, um deinen NVIDIA-Aufnahmeordner automatisch aus der Registry auszulesen, oder wähle deinen Clip-Ordner (z. B. `D:\Clips`) über **„Durchsuchen...“**.
   - **Auto-Import** ist sofort aktiv. Solange das grüne Statusbanner *„Auto-Import: AKTIV“* anzeigt, landen alle neuen ShadowPlay-Clips automatisch in Medal.
2. **Optional – ältere Clips nachimportieren:**
   - Mit **„Letzten Monat nachimportieren“** kannst du verpasste Clips der letzten 30 Tage einmalig gesammelt in Medal nachziehen.
3. **Rückblicke & Werbefreiheit in Medal aktivieren:**
   - Wähle bei Bedarf den gewünschten **Rückblick-Zeitraum** (`±3 Tage` Standard, `±7`, `±14` oder `±30 Tage`), ob **nur das letzte Jahr** angezeigt werden soll, und ob **Werbung ausgeblendet** wird.
   - Klicke auf **„Patch in Medal aktivieren“**. Falls Medal gerade läuft, beendet reMedal Medal kurz, spielt den Patch sicher ein und startet Medal neu.
4. **Autostart aktivieren:**
   - Setze die Haken bei **„Mit Windows starten“** und **„Minimiert im Tray starten“**, damit reMedal unbemerkt im System-Tray mitläuft.

---

## Sicherheit & Deinstallation

- **Automatische Backups:** Vor jedem Schreibzugriff auf deine Medal-Datenbank (`%AppData%\Medal\medal-*.db`) legt reMedal automatisch ein SQLite-Backup unter `%LocalAppData%\reMedal\backups` an (die letzten 5 Backups werden behalten).
- **Original-`app.asar`-Backup:** Vor dem ersten Patchen von Medal speichert reMedal eine Kopie als `app.asar.remedal.bak`. Über **„Original wiederherstellen“** lässt sich Medal jederzeit auf den Originalzustand zurücksetzen.
- **Saubere Deinstallation:** Über *Windows-Einstellungen → Apps → Installierte Apps → reMedal → Deinstallieren* wird die App restlos entfernt (inkl. Autostart-Eintrag und Wiederherstellung der originalen `app.asar`).

---

## Häufige Fragen (FAQ)

**Wie oft prüft Medal auf neue Rückblicke?**
Beim Öffnen der Medal-Startseite werden deine hochgeladenen Clips einmalig für den aktuellen Kalendertag geladen und zwischengespeichert. Läuft Medal dauerhaft (z. B. über Mitternacht), prüft ein leichter Timer alle 30 Minuten auf einen Tageswechsel und aktualisiert die Rückblicke automatisch.

**Was passiert nach einem Medal-Update?**
Wenn Medal sich selbst aktualisiert, wird `resources\app.asar` durch Medal überschrieben. Der Auto-Import funktioniert weiter, nur der Rückblicke-/AdBlock-Patch muss in reMedal mit einem Klick auf **„Patch in Medal aktivieren“** wieder eingespielt werden. Sollte ein Medal-Update die Struktur ändern, liefern wir den Fix über das automatische GitHub-Update von reMedal nach.

**Wo werden meine Einstellungen gespeichert?**
Unter `%LocalAppData%\reMedal\config.json`.

---

## Selber bauen (Entwicklung)

Voraussetzungen: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) und optional `vpk` (`dotnet tool install -g vpk`).

```powershell
# Bauen & isolierten Self-Test ausführen (tastet keine echte Medal-DB an)
dotnet build MedalShadowSync\MedalShadowSync.csproj -c Release
MedalShadowSync\bin\Release\net8.0-windows\reMedal.exe --self-test

# Velopack Installer & Update-Paket in dist\releases\ bauen
.\scripts\build-release.ps1 -Version 1.3.0
```
