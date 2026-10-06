# Builds a NeoFences test VM (M38, ADR-060) from a Windows ISO, without clicking through Windows Setup: Windows is written
# straight onto the VM's disk (DISM) with an answer file (local "tester" account, signs in by itself, the test waiter at
# sign-in), then the VM is started once and a "clean" checkpoint is taken at the tester's desktop.
# Run in an ADMIN Windows PowerShell (powershell.exe; mounting disks and DISM need it), once per VM. Needs Hyper-V. ASCII only.
#   tools\vm\New-TestVm.ps1 -Name NF-Win11 -Iso D:\NeoFences-VMs\iso\Win11.iso
param(
  [Parameter(Mandatory)][string]$Name,
  [Parameter(Mandatory)][string]$Iso,
  [string]$Edition = 'Pro',
  [string]$Folder = 'D:\NeoFences-VMs'
)
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw 'Run this in an admin PowerShell (it mounts a disk and writes Windows onto it).' }
if (-not (Get-Command New-VM -ErrorAction SilentlyContinue)) { throw 'Hyper-V is not turned on (see tools\vm\README.md).' }
if (Get-VM -Name $Name -ErrorAction SilentlyContinue) { throw "A VM named $Name already exists (Remove-VM it and its disk first)." }
$guest = Join-Path $PSScriptRoot 'guest'
New-Item -ItemType Directory -Force $Folder | Out-Null
$vhd = Join-Path $Folder "$Name.vhdx"
if (Test-Path -LiteralPath $vhd) { throw "$vhd already exists." }

"mounting $Iso"
$image = Mount-DiskImage -ImagePath $Iso -PassThru
try {
  $isoDrive = ($image | Get-Volume).DriveLetter
  $wim = @("${isoDrive}:\sources\install.wim", "${isoDrive}:\sources\install.esd") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
  if (-not $wim) { throw 'No install.wim / install.esd in the ISO.' }
  $index = (Get-WindowsImage -ImagePath $wim | Where-Object { $_.ImageName -match "^Windows 1[01] $Edition$" } | Select-Object -First 1).ImageIndex
  if (-not $index) { throw "No 'Windows 10/11 $Edition' edition in the ISO: $((Get-WindowsImage -ImagePath $wim).ImageName -join ', ')" }
  "edition index $index of $wim"

  "creating $vhd (64 GB, grows as needed)"
  $disk = New-VHD -Path $vhd -SizeBytes 64GB -Dynamic | Mount-VHD -PassThru | Get-Disk
  try {
    Initialize-Disk -Number $disk.Number -PartitionStyle GPT
    $efi = New-Partition -DiskNumber $disk.Number -Size 260MB -GptType '{c12a7328-f81f-11d2-ba4b-00a0c93ec93b}'
    Format-Volume -Partition $efi -FileSystem FAT32 -NewFileSystemLabel System -Confirm:$false | Out-Null
    New-Partition -DiskNumber $disk.Number -Size 16MB -GptType '{e3c9e316-0b5c-4db8-817d-f92df00215ae}' | Out-Null
    $windows = New-Partition -DiskNumber $disk.Number -UseMaximumSize
    Format-Volume -Partition $windows -FileSystem NTFS -NewFileSystemLabel Windows -Confirm:$false | Out-Null
    $efi | Add-PartitionAccessPath -AssignDriveLetter; $windows | Add-PartitionAccessPath -AssignDriveLetter
    $efiDrive = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $efi.PartitionNumber).DriveLetter
    $winDrive = (Get-Partition -DiskNumber $disk.Number -PartitionNumber $windows.PartitionNumber).DriveLetter

    "writing Windows to ${winDrive}: (a few minutes)"
    Expand-WindowsImage -ImagePath $wim -Index $index -ApplyPath "${winDrive}:\" | Out-Null
    & "$env:WINDIR\System32\bcdboot.exe" "${winDrive}:\Windows" /s "${efiDrive}:" /f UEFI | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'bcdboot failed' }

    "answer file and test files"
    New-Item -ItemType Directory -Force "${winDrive}:\Windows\Panther" | Out-Null
    (Get-Content -LiteralPath (Join-Path $guest 'unattend.xml') -Raw).Replace('__COMPUTERNAME__', $Name) |
      Set-Content -LiteralPath "${winDrive}:\Windows\Panther\unattend.xml" -Encoding UTF8
    New-Item -ItemType Directory -Force "${winDrive}:\NeoFencesTest" | Out-Null
    Copy-Item -Path (Join-Path $guest '*.ps1') -Destination "${winDrive}:\NeoFencesTest"
  } finally {
    Dismount-VHD -Path $vhd
  }
} finally {
  Dismount-DiskImage -ImagePath $Iso | Out-Null
}

"creating the VM"
$vm = New-VM -Name $Name -Generation 2 -MemoryStartupBytes 4GB -VHDPath $vhd -SwitchName 'Default Switch' -Path $Folder
Set-VMProcessor -VM $vm -Count 2
Set-VMMemory -VM $vm -DynamicMemoryEnabled $true -MinimumBytes 2GB -MaximumBytes 6GB
Set-VMFirmware -VM $vm -SecureBootTemplate MicrosoftWindows
Set-VMKeyProtector -VM $vm -NewLocalKeyProtector
Enable-VMTPM -VM $vm
Enable-VMIntegrationService -VM $vm -Name 'Guest Service Interface'
Set-VM -VM $vm -CheckpointType Standard -AutomaticCheckpointsEnabled $false

"first start: Windows sets itself up and signs in (5-15 minutes)"
Start-VM -VM $vm
$credential = New-Object PSCredential('tester', (ConvertTo-SecureString 'NeoFences-Test-1' -AsPlainText -Force))
$deadline = (Get-Date).AddMinutes(30)
$ready = $false
while (-not $ready -and (Get-Date) -lt $deadline) {
  Start-Sleep -Seconds 20
  try { $ready = Invoke-Command -VMName $Name -Credential $credential -ScriptBlock { Test-Path 'C:\NeoFencesTest\waiter.log' } -ErrorAction Stop } catch { }
}
if (-not $ready) { throw "$Name did not reach the tester's desktop in 30 minutes; look at it in Hyper-V Manager." }
Start-Sleep -Seconds 60 # first-sign-in work settles
Checkpoint-VM -VM $vm -SnapshotName 'clean'
Stop-VM -VM $vm -TurnOff
"done: $Name is ready (checkpoint 'clean'); tools\vm\Invoke-VmChecks.ps1 runs the checks without admin"
