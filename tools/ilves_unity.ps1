param(
  [Parameter(Mandatory=$true)][string]$Method,
  [string]$Log = "D:\UnityProjects\ILVES\Logs\run.log",
  [string]$Pattern = "ILVES"
)
$unity = 'D:\Unity\Editors\6000.3.27f1\Editor\Unity.exe'
$psi = @{
  FilePath = $unity
  ArgumentList = @('-batchmode','-projectPath','D:\UnityProjects\ILVES','-executeMethod',$Method,'-logFile',$Log)
  Wait = $true
  PassThru = $true
  NoNewWindow = $true
}
$p = Start-Process @psi
Write-Output ("[runner] method=" + $Method + " exit=" + $p.ExitCode + " log=" + $Log)
if (Test-Path $Log) {
  Get-Content $Log -Encoding UTF8 | Select-String -Pattern $Pattern | ForEach-Object { $_.Line }
  Write-Output "--- error/exception lines ---"
  Get-Content $Log -Encoding UTF8 | Select-String -Pattern 'error CS|Exception|error=' | Select-Object -First 40 | ForEach-Object { $_.Line }
} else {
  Write-Output "[runner] log missing"
}
exit $p.ExitCode
