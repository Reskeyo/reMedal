<div align="center">

<img src="docs/remedal-logo.png" alt="reMedal Logo" width="110" />

# reMedal

**Hol dir den automatischen ShadowPlay-Import & die „Vor einem Jahr“-Rückblicke in Medal.tv zurück.**

[![Latest Release](https://img.shields.io/github/v/release/Reskeyo/reMedal?style=for-the-badge&color=ef4444&label=Download)](https://github.com/Reskeyo/reMedal/releases/latest)
[![Platform](https://img.shields.io/badge/Windows-10%20%2F%2011%20(x64)-0f172a?style=for-the-badge&logo=windows)](https://github.com/Reskeyo/reMedal/releases/latest/download/reMedal-win-Setup.exe)
[![Auto-Updates](https://img.shields.io/badge/Auto--Updates-GitHub%20Releases-16a34a?style=for-the-badge)](https://github.com/Reskeyo/reMedal/releases)

*Leichtgewichtige Windows-App (~0 % CPU im Leerlauf) – keine FFmpeg- oder .NET-Vorinstallation nötig.*

</div>

---

## ⬇️ Download & Installation

1. Lade den aktuellen Windows-Installer herunter:

   ### 👉 [**reMedal-win-Setup.exe herunterladen**](https://github.com/Reskeyo/reMedal/releases/latest/download/reMedal-win-Setup.exe)

2. Starte `reMedal-win-Setup.exe` – die App installiert sich **ohne Admin-Rechte**, legt Startmenü-/Desktop-Verknüpfungen an und startet direkt.

> [!NOTE]
> **Windows SmartScreen beim ersten Start:** Da `reMedal` ein kostenloses Open-Source-Tool ohne teures Code-Signing-Zertifikat ist, zeigt Windows eventuell *„Der Computer wurde durch Windows geschützt“*. Klicke einfach auf **„Weitere Informationen“ → „Trotzdem ausführen“**.

---

## ✨ Features

| Feature | Beschreibung |
| :--- | :--- |
| 🎬 **Automatischer ShadowPlay-Import** | Überwacht deinen NVIDIA ShadowPlay-Ordner per Event-Watcher, wartet bis Aufnahmen fertig geschrieben sind und trägt neue Clips direkt in Medals lokale Datenbank ein – **ohne Dateien zu kopieren oder Speicherplatz zu verschwenden**. |
| 🎮 **Automatische Spiel-Erkennung** | Ordnet ShadowPlay-Unterordner (z. B. `Counter-strike 2`, `Dark Souls III`, `Rainbow Six Siege`) automatisch der passenden Medal-Spielkategorie zu – dank eingebautem Offline-Katalog mit **153.000+ Spielen**. |
| 🕰️ **Rückblick-Stories in Medal** | Bringt die beliebte **„Rückblick“**-Story direkt neben *Your story* auf deine Medal-Startseite (`1 Tag (selber Tag)` Standard, einstellbar bis `±30 Tage`; alle vergangenen Jahre oder optional nur letztes Jahr). Zeigt deine hochgeladenen & zugeschnittenen Clips inkl. Medal-Style-Player, **Link teilen** & Gesehen-Status. |
| 🚫 **Werbung ausblenden** | Blendet eingebettete Werbe-Banner in Medal client-seitig aus (optional per Checkbox abschaltbar). |
| 🔄 **Vollautomatische Updates** | Prüft beim PC-/App-Start still im Hintergrund auf neue Releases über GitHub und aktualisiert sich ohne nervige Popups selbst. |

---

## 🚀 Einrichtung (in 1 Minute)

1. **📁 ShadowPlay-Ordner prüfen**
   Klicke auf **„ShadowPlay suchen“**, um deinen NVIDIA-Aufnahmeordner automatisch aus der Registry zu laden – oder wähle über **„Durchsuchen...“** deinen eigenen Clip-Ordner (z. B. `D:\Clips`).
   Sobald oben **„Auto-Import: AKTIV“** steht, werden neue Clips vollautomatisch importiert.
2. **📦 Optional: Verpasste Clips nachholen**
   Klicke einmalig auf **„Letzten Monat nachimportieren“**, um alle Clips der letzten 30 Tage gesammelt nachzuziehen.
3. **🌟 Rückblicke & Werbefreiheit in Medal aktivieren**
   Wähle deinen gewünschten **Rückblick-Zeitraum** (`1 Tag (selber Tag)` Standard) und klicke auf **„Patch in Medal aktivieren“**. Falls Medal läuft, startet reMedal Medal kurz neu und bindet den Patch ein.
4. **⚡ Autostart aktivieren**
   Hake **„Mit Windows starten“** und **„Minimiert im Tray starten“** an – danach läuft reMedal unbemerkt im System-Tray.

---

## 🛡️ Sicherheit, Backups & Deinstallation

- **Automatische DB-Backups:** Vor jedem Schreibzugriff auf deine Medal-Datenbank (`%AppData%\Medal\medal-*.db`) legt reMedal ein SQLite-Backup unter `%LocalAppData%\reMedal\backups` an (die letzten 5 Backups werden aufbewahrt).
- **Original-`app.asar`-Backup:** Vor dem ersten Patchen wird Medals `app.asar` als `app.asar.remedal.bak` gesichert. Über **„Original wiederherstellen“** lässt sich Medal jederzeit mit einem Klick in den Originalzustand zurücksetzen.
- **Saubere Deinstallation:** Über *Windows-Einstellungen → Apps → Installierte Apps → reMedal → Deinstallieren* wird die App restlos entfernt (inkl. Autostart und Wiederherstellung der originalen `app.asar`).

---

## ❓ Häufige Fragen (FAQ)

<details>
<summary><strong>Wie oft prüft Medal auf neue Rückblicke?</strong></summary>

Beim Öffnen der Medal-Startseite werden deine hochgeladenen Clips einmalig für den aktuellen Kalendertag geladen und zwischengespeichert. Läuft Medal dauerhaft (z. B. über Mitternacht), prüft ein leichter Timer alle 30 Minuten auf einen Tageswechsel und aktualisiert die Rückblicke automatisch.

</details>

<details>
<summary><strong>Was passiert, wenn Medal ein Update installiert?</strong></summary>

Wenn Medal sich selbst aktualisiert, überschreibt Medal seine `resources\app.asar`. Der automatische ShadowPlay-Import läuft einfach weiter – nur für die Rückblicke/Werbefreiheit reicht in reMedal ein Klick auf **„Patch in Medal aktivieren“**. Falls ein Medal-Update größere Änderungen bringt, kommt der Fix automatisch über reMedals GitHub-Updater.

</details>

<details>
<summary><strong>Wo liegen Konfiguration & Backups?</strong></summary>

- Konfiguration: `%LocalAppData%\reMedal\config.json`
- Datenbank-Backups: `%LocalAppData%\reMedal\backups`

</details>

---

## 🛠️ Selber bauen (Entwicklung)

Voraussetzungen: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) und optional `vpk` (`dotnet tool install -g vpk`).

```powershell
# Bauen & isolierten Self-Test ausführen (tastet keine echte Medal-DB an)
dotnet build MedalShadowSync\MedalShadowSync.csproj -c Release
MedalShadowSync\bin\Release\net8.0-windows\reMedal.exe --self-test

# Velopack Installer & Update-Paket in dist\releases\ bauen
.\scripts\build-release.ps1 -Version 1.3.3
```
