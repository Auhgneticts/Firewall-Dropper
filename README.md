# Firewall Dropper

Drag a program, or its shortcut, onto the Firewall Dropper icon to block or allow it in Windows Defender Firewall.

## Using it

| You do | What happens |
|---|---|
| Open the app normally | The **settings** window opens: inbound/outbound Block / Allow / Don't add a rule, network profiles, and what to do if a rule already exists. Changes save automatically. |
| Drop programs or shortcuts onto the **desktop shortcut**, or use right-click › **Send to › Firewall Dropper** | A small dialog lists each item and the rules that will be created, with problems shown on each line. **OK** asks for administrator permission (UAC) once, adds the rules and shows the result for each program. |
| **Rules › View firewall rules** | Lists every rule the app created. You can search, turn rules on or off, remove them, or remove all of them. |
| **Help › About** | Version, file locations and how-to. |
| **App › Install on this PC** | Per-user install, no admin needed: copies the app to `%LocalAppData%\Programs\Firewall Dropper`, adds Start menu, desktop and Send to shortcuts, and lists it under Settings › Apps for uninstall. |

Tracking: every rule is created in the Windows Firewall rule group **"Firewall Dropper"**. Windows Firewall itself is the
record of what the app manages, so nothing gets out of sync and the rules keep working when the app isn't running.

Note: on Windows 11, dropping a file onto a **pinned taskbar icon** doesn't open it with the app (Windows doesn't support
that). Use the desktop shortcut, the Send to menu, or the drop area in the settings window instead.

## Command line

```
FirewallDropper.exe                       settings window
FirewallDropper.exe <files...>            confirm dialog for the dropped files
FirewallDropper.exe --apply <files...>    (used internally) elevated copy that applies and shows results
FirewallDropper.exe --rules <op> <names>  (used internally) elevated remove / enable / disable / remove-all
FirewallDropper.exe --uninstall           uninstall (used by Settings › Apps)
```

## Building

Requires the .NET 11 SDK.

```
dotnet build
dotnet publish -p:PublishProfile=FolderProfile     # -> bin\publish\FirewallDropper.exe (standalone, no .NET needed)
```

## Layout

- `App.xaml(.cs)`: startup and how command-line arguments are routed; Fluent theme and shared styles
- `Views/`: `MainWindow` (settings), `DropWindow` (drop dialog), `RulesWindow`, `AboutWindow`
- `Services/FirewallService.cs`: Windows Firewall COM API (`HNetCfg.FwPolicy2`)
- `Services/ProgramResolver.cs`, `ShellLink.cs`: shortcut → .exe resolution, including MSI "advertised" shortcuts
- `Services/DropProcessor.cs`: applies the settings to one program
- `Services/RuleOperations.cs`, `Elevation.cs`: UAC elevation only when the firewall is changed
- `Services/Installer.cs`: per-user install/uninstall and shortcuts
