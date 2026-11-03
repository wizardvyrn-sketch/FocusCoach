# workpc_check.ps1 — 業務PC用の確認ツール(FocusCoach P-0 / U-05-2、U-07-1)
# 結果は OK / NG / ?? と件数・種別だけを画面に出す。ファイル名、パス、URL、ウィンドウタイトルは表示も保存もしない。
# ファイルへの書き込みは、ブラウザ履歴のコピー確認で %TEMP% に一時ファイルを作り、すぐ削除するだけ。
#
# 事前準備(できる範囲で):
#   1. Explorer でフォルダを開いておく(ローカルと、ファイルサーバか SharePoint 同期フォルダの両方があるとよい)
#   2. Explorer から Word / Excel のファイルを2〜3件ダブルクリックで開き、開いたままにする
#   3. Edge で社内システム(SharePoint)を少し閲覧しておく
# 実行: powershell -NoProfile -ExecutionPolicy Bypass -File workpc_check.ps1
#   (ファイルで実行できない場合は、このスクリプト全体を PowerShell の画面に貼り付けて Enter)

$R = [ordered]@{}
function Put($id, $st, $title, $detail) {
    $R[$id] = $st
    $color = @{ 'OK' = 'Green'; 'NG' = 'Red'; '??' = 'Yellow'; '--' = 'Gray' }[$st]
    Write-Host ("[{0}] {1} {2}" -f $st, $id, $title) -ForegroundColor $color -NoNewline
    Write-Host ("   {0}" -f $detail)
}
function Kind([string]$p) {
    if ($p -like '\\*') { 'UNC' } elseif ($p -match '^[A-Za-z]:\\') { 'Local' } elseif ($p -match '^https?://') { 'Web' } else { 'Other' }
}
function Kinds($list) { (@($list) | ForEach-Object { Kind $_ } | Group-Object | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ' ' }
function RegVal($path, $name) { try { (Get-ItemProperty -Path $path -Name $name -ErrorAction Stop).$name } catch { $null } }

Write-Host '=== FocusCoach 業務PC確認 ===' -ForegroundColor Cyan

# C0 実行環境: スクリプトの言語モードが制限されていると、以降の確認(COM、Win32)が動かない
$lm = $ExecutionContext.SessionState.LanguageMode
$mp = Get-ExecutionPolicy -Scope MachinePolicy; $up = Get-ExecutionPolicy -Scope UserPolicy
$os = [Environment]::OSVersion.Version
$st = if ($lm -eq 'FullLanguage') { 'OK' } else { 'NG' }
Put 'C0' $st '実行環境' ("PS={0} Lang={1} GPO(Machine/User)={2}/{3} OS={4}.{5}" -f $PSVersionTable.PSVersion, $lm, $mp, $up, $os.Build, [Environment]::Is64BitOperatingSystem)

# C1 開いているフォルダ(Explorer COM)
try {
    $wins = @((New-Object -ComObject Shell.Application).Windows())
    $paths = foreach ($w in $wins) { try { $w.Document.Folder.Self.Path } catch { } }
    $st = if (@($paths).Count -gt 0) { 'OK' } elseif ($wins.Count -eq 0) { '??' } else { 'NG' }
    Put 'C1' $st 'フォルダ取得(Explorer)' ("windows={0} {1}{2}" -f $wins.Count, (Kinds $paths), $(if ($wins.Count -eq 0) { '  ※Explorerでフォルダを開いて再実行' }))
} catch { Put 'C1' 'NG' 'フォルダ取得(Explorer)' $_.Exception.Message }

# C2 最近使った項目(ファイルを開いた記録)
try {
    $recent = Join-Path $env:APPDATA 'Microsoft\Windows\Recent'
    $all = @(Get-ChildItem $recent -Filter *.lnk -ErrorAction Stop)
    $day = @($all | Where-Object { $_.LastWriteTime -gt (Get-Date).AddDays(-1) })
    $wsh = New-Object -ComObject WScript.Shell
    $tg = foreach ($l in $day) { try { $wsh.CreateShortcut($l.FullName).TargetPath } catch { } }
    $track = RegVal 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced' 'Start_TrackDocs'
    $pol = @(); foreach ($h in 'HKCU', 'HKLM') {
        $pp = "${h}:\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"
        if ((RegVal $pp 'NoRecentDocsHistory') -eq 1) { $pol += "$h.NoRecent" }
        if ((RegVal $pp 'ClearRecentDocsOnExit') -eq 1) { $pol += "$h.ClearOnExit" }
    }
    $disabled = ($track -eq 0) -or ($pol -contains 'HKCU.NoRecent') -or ($pol -contains 'HKLM.NoRecent')
    $st = if ($disabled) { 'NG' } elseif ($day.Count -gt 0) { 'OK' } else { '??' }
    $note = if ($st -eq '??') { '  ※Explorerからファイルを開いて再実行' } else { '' }
    Put 'C2' $st 'ファイル記録(最近使った項目)' ("24h={0} total={1} {2} TrackDocs={3} policy={4}{5}" -f $day.Count, $all.Count, (Kinds $tg), $(if ($null -eq $track) { 'default' } else { $track }), $(if ($pol) { $pol -join ',' } else { 'none' }), $note)
} catch { Put 'C2' 'NG' 'ファイル記録(最近使った項目)' $_.Exception.Message }

# C3 前面ウィンドウ / C4 無操作時間(Win32 API。Add-Type がブロックされると NG)
$win32 = $false
try {
    Add-Type -ErrorAction Stop @'
using System; using System.Runtime.InteropServices; using System.Text;
public static class FcW {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [StructLayout(LayoutKind.Sequential)] public struct LII { public uint cbSize; public uint dwTime; }
  [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LII p);
  public static long IdleMs() { LII l = new LII(); l.cbSize = (uint)Marshal.SizeOf(l); if (!GetLastInputInfo(ref l)) return -1; return (long)(uint)((uint)Environment.TickCount - l.dwTime); }
}
'@
    $win32 = $true
} catch { Put 'C3' 'NG' '前面ウィンドウ' ("Add-Type失敗: {0}" -f $_.Exception.Message); Put 'C4' 'NG' '無操作時間' 'Add-Type失敗' }
if ($win32) {
    Write-Host '   → 5秒以内に Excel か Word のウィンドウをクリックしてください...' -ForegroundColor Cyan
    Start-Sleep -Seconds 5
    $h = [FcW]::GetForegroundWindow(); $sb = New-Object Text.StringBuilder 512
    [void][FcW]::GetWindowText($h, $sb, 512); $fpid = 0; [void][FcW]::GetWindowThreadProcessId($h, [ref]$fpid)
    $pn = try { (Get-Process -Id $fpid -ErrorAction Stop).ProcessName } catch { '?' }
    $st = if ($sb.Length -gt 0 -and $pn -ne '?') { 'OK' } else { 'NG' }
    Put 'C3' $st '前面ウィンドウ' ("process={0} title_length={1}" -f $pn, $sb.Length)
    Start-Sleep -Seconds 2
    $idle = [FcW]::IdleMs()
    $st = if ($idle -ge 0) { 'OK' } else { 'NG' }
    Put 'C4' $st '無操作時間' ("idle_ms={0}" -f $idle)
}

# C5 Office の開いている文書のフルパス(COM。SharePoint 上の文書は Web になる)
$od = @(); $apps = @()
foreach ($a in @(@('Excel.Application', 'Workbooks'), @('Word.Application', 'Documents'), @('PowerPoint.Application', 'Presentations'))) {
    try { $app = [Runtime.InteropServices.Marshal]::GetActiveObject($a[0]) } catch { continue }
    $n = 0; foreach ($d in $app.($a[1])) { try { $od += $d.FullName; $n++ } catch { } }
    $apps += ('{0}={1}' -f $a[0].Split('.')[0], $n)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($app)
}
$st = if ($od.Count -gt 0) { 'OK' } elseif ($apps.Count -eq 0) { '??' } else { 'NG' }
Put 'C5' $st 'Office文書のパス(COM)' ("{0} {1}{2}" -f $(if ($apps) { $apps -join ' ' } else { 'Office未起動' }), (Kinds $od), $(if ($st -eq '??') { '  ※Excel/Wordでファイルを開いて再実行' }))

# C6 ブラウザ履歴DB(プロファイルごとに一時コピーできるか。コピーはすぐ削除)
$bres = @(); $okc = 0; $ngc = 0
foreach ($b in @(@('Edge', 'Microsoft\Edge\User Data', 'msedge'), @('Chrome', 'Google\Chrome\User Data', 'chrome'))) {
    $root = Join-Path $env:LOCALAPPDATA $b[1]
    if (-not (Test-Path $root)) { continue }
    $run = [bool](Get-Process -Name $b[2] -ErrorAction SilentlyContinue)
    $profs = @(Get-ChildItem $root -Directory -ErrorAction SilentlyContinue | Where-Object { Test-Path (Join-Path $_.FullName 'History') })
    $ok = 0
    foreach ($d in $profs) {
        $tmp = Join-Path $env:TEMP ('fc_check_{0}.tmp' -f [guid]::NewGuid().ToString('N'))
        try {
            Copy-Item (Join-Path $d.FullName 'History') $tmp -Force -ErrorAction Stop
            $fs = [IO.File]::OpenRead($tmp); $buf = New-Object byte[] 15; [void]$fs.Read($buf, 0, 15); $fs.Close()
            if ([Text.Encoding]::ASCII.GetString($buf) -eq 'SQLite format 3') { $ok++; $okc++ } else { $ngc++ }
        } catch { $ngc++ } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }
    }
    $bres += ('{0}:profiles={1} copyOK={2} running={3}' -f $b[0], $profs.Count, $ok, $run)
}
$st = if ($okc -gt 0 -and $ngc -eq 0) { 'OK' } elseif ($okc -gt 0) { '??' } elseif ($bres) { 'NG' } else { '--' }
Put 'C6' $st 'ブラウザ履歴(Edge/Chrome)' ($(if ($bres) { $bres -join '  ' } else { 'ブラウザ未検出' }))

# C7 割り込みアプリ(Teams / Outlook)のプロセス名で判定できるか
$ti = @(); foreach ($p in 'ms-teams', 'Teams', 'OUTLOOK', 'olk') { if (Get-Process -Name $p -ErrorAction SilentlyContinue) { $ti += $p } }
$st = if ($ti) { 'OK' } else { '??' }
Put 'C7' $st '割り込みアプリ検出' ("running={0}" -f $(if ($ti) { $ti -join ',' } else { 'none(Teams/Outlookを起動して再実行)' }))

# C8 配布方式の判断材料(.NET 8 デスクトップランタイムの有無、AppLocker の規則数)
$rt = @(Get-ChildItem "$env:ProgramFiles\dotnet\shared\Microsoft.WindowsDesktop.App" -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '8.*' }).Count
$al = try { $x = [xml](Get-AppLockerPolicy -Effective -Xml -ErrorAction Stop); @($x.SelectNodes('//FilePathRule|//FilePublisherRule|//FileHashRule')).Count } catch { 'unknown' }
Put 'C8' '--' '配布の参考情報' ("dotnet8desktop={0} AppLockerRules={1}" -f $(if ($rt -gt 0) { 'yes' } else { 'no' }), $al)

Write-Host ''
Write-Host ('SUMMARY ' + (($R.Keys | ForEach-Object { "$_=$($R[$_])" }) -join ' ')) -ForegroundColor Cyan
Write-Host '上の [..] 行と SUMMARY 行を書き写すか、そのまま伝えてください(機密情報は含みません)。'
