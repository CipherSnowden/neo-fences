# NeoFences test VM (M38, ADR-060)

A Hyper-V virtual machine with the current Windows 11 — the only target (ADR-059) — where NeoFences' checklist runs by
itself: install the previous release, the first-run welcome, fences behind windows and after Win+D, desktop icons hidden
and back after an exit, a kill and an Explorer restart, game mode, Peek and the keyboard, updating to the release
candidate, and uninstall. Run it for every release candidate.

## Once (admin)

1. **Hyper-V** — in an admin PowerShell, then restart:

   ```powershell
   dism.exe /Online /Enable-Feature /FeatureName:Microsoft-Hyper-V /All /NoRestart
   Add-LocalGroupMember -Group 'Hyper-V Administrators' -Member $env:USERNAME   # or: net localgroup "Hyper-V Administrators" $env:USERNAME /add
   Restart-Computer
   ```

   (In PowerShell 7, `Enable-WindowsOptionalFeature` fails with "Class not registered"; `dism.exe` works everywhere.)

2. **The ISO** (English (United States), 64-bit) into `D:\NeoFences-VMs\iso\`: https://www.microsoft.com/software-download/windows11
   → Windows 11 (multi-edition ISO) → `Win11.iso`.

3. **The VM** — in an admin **Windows PowerShell** (powershell.exe), from the repo folder (5–20 minutes; Windows sets
   itself up without questions and signs in a local "tester" account):

   ```powershell
   powershell -ExecutionPolicy Bypass -File tools\vm\New-TestVm.ps1 -Name NF-Win11 -Iso D:\NeoFences-VMs\iso\Win11.iso
   ```

   It ends with a checkpoint named `clean` at the tester's desktop. The VM is unactivated (fine for testing) and uses
   about 20–25 GB on `D:\NeoFences-VMs`. Wait an hour before the first run: for the first hour after an account first
   signs in, Windows reports "quiet time" instead of a full-screen app, so the game-mode check cannot pass.

## Every release candidate (no admin)

```powershell
pwsh -File tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11 -PreviousSetup <the last release's NeoFences.App-win-Setup.exe> -Feed <folder with the candidate's releases.win.json and .nupkg files> -Candidate 0.25.0
```

It goes back to `clean`, copies the run in, lets the guest script work on the VM's desktop (watch it in Hyper-V Manager if
you like; up to ~25 minutes), copies the results, screenshots and logs out to `%TEMP%\neofences-vm-<name>` and prints the
report (one line per check, PASS / FAIL). The VM is turned off afterwards.

To watch, untick **View → Enhanced session** in the VM window: enhanced session is a remote sign-in that shows a
password box over the tester's already signed-in desktop. Do not sign in there — it takes the desktop away from the
running checks.

## Live checks of a branch build (M38.1, no admin)

Live checks and probes run in a second VM, `NF-Win11-Dev`, so the PC stays free. Make it once as a copy of `NF-Win11`
(about a minute, ~25 GB more on `D:`; the copy keeps the `clean` checkpoint):

```powershell
Export-VM -Name NF-Win11 -Path D:\NeoFences-VMs\export
Import-VM -Path (Get-Item 'D:\NeoFences-VMs\export\NF-Win11\Virtual Machines\*.vmcx').FullName -Copy -GenerateNewId `
  -VirtualMachinePath D:\NeoFences-VMs\NF-Win11-Dev -VhdDestinationPath 'D:\NeoFences-VMs\NF-Win11-Dev\Virtual Hard Disks' `
  -SnapshotFilePath D:\NeoFences-VMs\NF-Win11-Dev | Rename-VM -NewName NF-Win11-Dev
Remove-Item D:\NeoFences-VMs\export -Recurse -Force
```

Then, from the checkout under test:

```powershell
pwsh -File tools\vm\Invoke-VmChecks.ps1 -Name NF-Win11-Dev -Live [-Data <a copy of a NeoFences data folder>] [-Files <folder>, ...]
```

It publishes the checkout self-contained (the VM has no .NET; or pass `-Build <publish folder>`), starts it on a copy of
`-Data` (else a fresh start) with the `-Files` folders on the tester's Desktop, and runs `guest\live-checks.ps1`: Peek
and the keyboard, the fence under the mouse, Tab through every fence, Esc in Properties (with data), Esc after switching
apps, a full-screen app ending Peek, a rolled-up fence opening for the keyboard. Same report as above. Only copies go into
the VM; the originals are never changed.

## Removing them

```powershell
Remove-VM NF-Win11, NF-Win11-Dev -Force; Remove-Item D:\NeoFences-VMs -Recurse -Force
```

The tester account's password (`NeoFences-Test-1`) is in `guest\unattend.xml`: a throwaway VM on the PC's own NAT network
with nothing to protect.
