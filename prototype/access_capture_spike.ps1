# access_capture_spike.ps1 — 使い捨てスパイク(仕様 08/05 のアクセス記録方式の実現性確認用)
# 「タスク中に開いたローカル/ネットワークフォルダ・ファイル・URL」を管理者権限なしで
# 取得できるかを確認する。プライバシー配慮のため、実際のパス/URLは表示せず件数と種別のみ出す。
# 実行: powershell -ExecutionPolicy Bypass -File prototype\access_capture_spike.ps1

function Kind([string]$p) {
    if ($p -like '\\*') { 'UNC' }
    elseif ($p -match '^[A-Za-z]:\\') { 'Local' }
    elseif ($p -match '^https?://') { 'Web' }
    else { 'Other' }
}

Write-Host '== 1. 開いている Explorer ウィンドウ (Shell.Application COM) =='
$shell = New-Object -ComObject Shell.Application
$wins = @($shell.Windows())
$kinds = foreach ($w in $wins) {
    try { Kind $w.Document.Folder.Self.Path } catch { 'Unreadable' }
}
Write-Host ("windows={0}  " -f $wins.Count) ($kinds | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" })

Write-Host '== 2. 最近使った項目 (%APPDATA%\Microsoft\Windows\Recent) =='
$recent = Join-Path $env:APPDATA 'Microsoft\Windows\Recent'
$wsh = New-Object -ComObject WScript.Shell
$all = @(Get-ChildItem $recent -Filter *.lnk -ErrorAction SilentlyContinue)
$lnks = @($all | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-1) })
$rk = foreach ($l in $lnks) { try { Kind $wsh.CreateShortcut($l.FullName).TargetPath } catch { 'Unreadable' } }
Write-Host ("lnk_24h={0}  " -f $lnks.Count) ($rk | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" })
$newest = $all | Sort-Object LastWriteTime -Descending | Select-Object -First 1
$age = if ($newest) { '{0:N1}h' -f ((Get-Date) - $newest.LastWriteTime).TotalHours } else { 'none' }
Write-Host ("lnk_total={0} newest_age={1}" -f $all.Count, $age)
# 「最近使ったファイル」の記録が OS 設定やグループポリシーで無効になっていないか(値が無ければ既定=有効)
function RegVal($path, $name) { try { (Get-ItemProperty -Path $path -Name $name -ErrorAction Stop).$name } catch { '(none)' } }
Write-Host ("Start_TrackDocs={0}  (0=無効)" -f (RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Start_TrackDocs'))
foreach ($hive in 'HKCU','HKLM') {
    $pp = "${hive}:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
    Write-Host ("{0} policy NoRecentDocsHistory={1} ClearRecentDocsOnExit={2}  (1=無効化/終了時消去)" -f $hive, (RegVal $pp 'NoRecentDocsHistory'), (RegVal $pp 'ClearRecentDocsOnExit'))
}

Write-Host '== 3. 前面ウィンドウのタイトル取得 (Win32 GetForegroundWindow) =='
Add-Type @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
'@
$h = [W]::GetForegroundWindow(); $sb = New-Object Text.StringBuilder 512
[void][W]::GetWindowText($h, $sb, 512); $fpid = 0; [void][W]::GetWindowThreadProcessId($h, [ref]$fpid)
Write-Host ("process={0} title_length={1}" -f (Get-Process -Id $fpid).ProcessName, $sb.Length)

Write-Host '== 4. ブラウザ履歴DB (Edge/Chrome History, SQLite) =='
$browsers = @{
  Edge   = @{ Root = Join-Path $env:LOCALAPPDATA 'Microsoft\Edge\User Data'; Proc = 'msedge' }
  Chrome = @{ Root = Join-Path $env:LOCALAPPDATA 'Google\Chrome\User Data'; Proc = 'chrome' }
}
foreach ($k in $browsers.Keys) {
    $b = $browsers[$k]
    if (-not (Test-Path $b.Root)) { Write-Host "$k : not installed"; continue }
    $running = [bool](Get-Process -Name $b.Proc -ErrorAction SilentlyContinue)
    # プロファイル(Default, Profile 1, ...)ごとに History がある。業務用プロファイルが Default とは限らない
    $hist = @(Get-ChildItem $b.Root -Directory | Where-Object { Test-Path (Join-Path $_.FullName 'History') })
    Write-Host ("{0} : running={1} profiles_with_history={2} ({3})" -f $k, $running, $hist.Count, (($hist | ForEach-Object Name) -join ', '))
    foreach ($d in $hist) {
        $p = Join-Path $d.FullName 'History'
        $tmp = Join-Path $env:TEMP ("fc_spike_{0}_{1}.sqlite" -f $k, ($d.Name -replace '\s','_'))
        try {
            Copy-Item $p $tmp -Force -ErrorAction Stop
            $fs = [IO.File]::OpenRead($tmp); $buf = New-Object byte[] 16; [void]$fs.Read($buf, 0, 16); $fs.Close()
            $hdr = [Text.Encoding]::ASCII.GetString($buf, 0, 15)
            $wal = Test-Path ($p + '-wal')
            Write-Host ("  {0} : copy OK size={1}KB sqlite_header={2} wal_present={3}" -f $d.Name, [int]((Get-Item $tmp).Length / 1KB), ($hdr -eq 'SQLite format 3'), $wal)
        }
        catch { Write-Host ("  {0} : copy FAILED ({1})" -f $d.Name, $_.Exception.Message) }
        finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }   # 閲覧履歴を一時フォルダに残さない
    }
}
