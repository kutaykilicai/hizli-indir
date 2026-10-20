# Hızlı İndir derleme ve kurulum betiği
param([switch]$Install)
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$out = Join-Path $root "dist\HizliIndir"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish "$root\src\HizliIndir\HizliIndir.csproj" -c Release -o $out --self-contained false
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız (uygulama)" }
dotnet publish "$root\src\HizliIndir.Host\HizliIndir.Host.csproj" -c Release -o $out --self-contained false
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız (köprü)" }
Get-ChildItem $out -Filter *.pdb | Remove-Item
if ($Install) {
    $target = Join-Path $env:LOCALAPPDATA "Programs\HizliIndir"
    Get-Process HizliIndir -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 500
    robocopy $out $target /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    $desktop = [Environment]::GetFolderPath("Desktop")
    $shell = New-Object -ComObject WScript.Shell
    $lnk = $shell.CreateShortcut((Join-Path $desktop "Hızlı İndir.lnk"))
    $lnk.TargetPath = Join-Path $target "HizliIndir.exe"
    $lnk.WorkingDirectory = $target
    $lnk.IconLocation = (Join-Path $target "HizliIndir.exe") + ",0"
    $lnk.Description = "Hızlı İndir"
    $lnk.Save()
    Write-Host "Kuruldu: $target"
}
