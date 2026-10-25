param(
  [string]$GameArgs,
  [string]$Out,
  [int]$WaitSec = 7,
  [int]$ClickX = -1,
  [int]$ClickY = -1,
  [int]$Clicks = 0,
  [int]$GapMs = 700
)
$exe = 'D:\UnityProjects\ILVES\Builds\Windows\Ilves.exe'
$py  = 'C:\Users\Admin\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
$shot = 'D:\DeepSeek_工作区\GitHub-3\tools\shot_window.py'
$click = 'D:\DeepSeek_工作区\GitHub-3\tools\click_window.py'
$argArray = @()
if ($GameArgs) { $argArray = @($GameArgs -split '\s+' | Where-Object { $_ -ne '' }) }
Write-Output ("[shot] args=" + ($argArray -join ' '))
$p = Start-Process -FilePath $exe -ArgumentList $argArray -PassThru
Start-Sleep -Seconds $WaitSec
if ($Clicks -gt 0 -and $ClickX -ge 0 -and $ClickY -ge 0) {
  & $py $click --pid $p.Id --x $ClickX --y $ClickY --count $Clicks --gap ($GapMs / 1000.0)
  Start-Sleep -Milliseconds 600
}
& $py $shot --pid $p.Id --out $Out --activate
Start-Sleep -Milliseconds 400
Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
