$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
try {
  $codePath = Join-Path $PSScriptRoot "Rech.cs"
  if (-not (Test-Path -LiteralPath $codePath)) {
    throw "Не найден Rech.cs рядом с программой. Распакуйте архив целиком и запускайте из папки."
  }
  $code = Get-Content -LiteralPath $codePath -Raw -Encoding UTF8
  Add-Type -TypeDefinition $code -ReferencedAssemblies @(
    "System.Windows.Forms",
    "System.Drawing",
    "System.Net.Http",
    "System.Speech",
    "System.Web.Extensions",
    "System.Core"
  ) -Language CSharp -ErrorAction Stop
  [Rech.App]::Run()
}
catch {
  $text = $_.Exception.ToString()
  try {
    $dir = Join-Path $env:APPDATA "Rech"
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -LiteralPath (Join-Path $dir "last-error.txt") -Value $text -Encoding UTF8
  }
  catch { }
  [System.Windows.Forms.MessageBox]::Show($text, "РЕЧЬ")
  exit 1
}
