# ui_close_flow_probe.ps1 - 使い捨ての実機確認ツール(Windows 専用。クラウドでは実行できない)
# 隔離した保存先(FOCUSCOACH_DATA)でアプリを起動し、UI Automation / Win32 でボタンとダイアログを操作して、
# 「集中開始 → ×(WM_CLOSE)→ 確認ダイアログ → 振り返り」の流れを再現し、ダイアログの文言と app ログを表示する。
# 疑似キー入力は使わない(フォーカスを奪わない)。標準のメッセージボックスは UIA で中身が取れないため Win32 の BM_CLICK で押す。
#   -Answer はい|いいえ     閉じる確認への答え(はい=終了して閉じる / いいえ=中断して閉じる)
#   -ReviewDelay 秒          振り返りを開いたままにする秒数
#   -ClickDuringReview 名前  振り返りが開いている間に、ミニパネルのこのボタン(例: 'タスク完了')を押す
# 事前に: dotnet build FocusCoach.sln
# 実行  : powershell -NoProfile -ExecutionPolicy Bypass -File prototype\ui_close_flow_probe.ps1 -ClickDuringReview 'タスク完了'
param([string]$Answer = 'はい', [int]$ReviewDelay = 0, [string]$ClickDuringReview = '')   # 閉じる確認への答え: はい=終了して閉じる / いいえ=中断して閉じる
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$exe  = Join-Path $PSScriptRoot '..\src\FocusCoach.App\bin\Debug\net8.0-windows\FocusCoach.exe'
$data = Join-Path $env:TEMP 'fc_close_flow_probe'   # 実データと分ける(FOCUSCOACH_DATA)
if (Test-Path $data) { Remove-Item $data -Recurse -Force }
New-Item -ItemType Directory $data | Out-Null
$env:FOCUSCOACH_DATA = $data

Add-Type @'
using System; using System.Text; using System.Collections.Generic; using System.Runtime.InteropServices;
public static class W {
  delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc p, IntPtr l);
  [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr h, EnumProc p, IntPtr l);
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
  public static List<string> Children(IntPtr dlg) {
    var r = new List<string>();
    EnumChildWindows(dlg, (h, l) => { var c = new StringBuilder(64); GetClassName(h, c, 64); var t = new StringBuilder(512); GetWindowText(h, t, 512);
      r.Add(string.Format("{0}|{1}|{2}", h.ToInt64(), c, t)); return true; }, IntPtr.Zero);
    return r;
  }
  public static void Click(IntPtr btn) { SendMessage(btn, 0x00F5, IntPtr.Zero, IntPtr.Zero); }
  public static List<string> ForPid(uint pid) {
    var r = new List<string>();
    EnumWindows((h, l) => { uint p; GetWindowThreadProcessId(h, out p); if (p == pid) {
      var c = new StringBuilder(128); GetClassName(h, c, 128); var t = new StringBuilder(256); GetWindowText(h, t, 256);
      r.Add(string.Format("0x{0:X} class={1} title=[{2}] visible={3} owner=0x{4:X}", h.ToInt64(), c, t, IsWindowVisible(h), GetWindow(h, 4).ToInt64())); } return true; }, IntPtr.Zero);
    return r;
  }
}
'@
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]

function Top($pid_) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $pid_)
    $AE::RootElement.FindAll($TS::Children, $c)
}
function Buttons($el) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    $el.FindAll($TS::Descendants, $c)
}
function Texts($el) {
    $c = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    ($el.FindAll($TS::Descendants, $c) | ForEach-Object { $_.Current.Name }) -join ' / '
}
function Click($el, $name) {
    foreach ($b in Buttons $el) {
        if ($b.Current.Name -like "$name*") { $b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke(); return $true }
    }
    $false
}

$p = Start-Process $exe -PassThru
try {
    $main = $null
    for ($i = 0; $i -lt 40 -and -not $main; $i++) { Start-Sleep -Milliseconds 250; $main = Top $p.Id | Where-Object { $_.Current.Name -eq 'FocusCoach' } | Select-Object -First 1 }
    if (-not $main) { 'NO MAIN WINDOW'; return }
    "main window found. buttons: " + ((Buttons $main | ForEach-Object { $_.Current.Name }) -join ' | ')
    # 0.10A: 開始準備は空から始まり、タスク名が空だと開始できない。タスク名を入れてから開始する
    $combo = $main.FindAll($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ComboBox))) | Select-Object -First 1
    $ce = $combo.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)))
    $ce.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('確認用タスク')
    Start-Sleep -Milliseconds 500
    if (-not (Click $main '集中を始める')) { 'start button not found'; return }
    Start-Sleep -Seconds 3
    "after start. buttons: " + ((Buttons $main | ForEach-Object { $_.Current.Name }) -join ' | ')

    '--- × (WM_CLOSE) ---'
    $cm = $p.CloseMainWindow(); "CloseMainWindow returned $cm"
    Start-Sleep -Milliseconds 1500
    'win32 windows of pid:'; [W]::ForPid([uint32]$p.Id) | ForEach-Object { '   ' + $_ }
    $p.Refresh(); "Responding=$($p.Responding)"
    'top-level windows now: ' + ((Top $p.Id | ForEach-Object { '[' + $_.Current.Name + '|' + $_.Current.ClassName + ']' }) -join ' ')
    'desktop windows titled FocusCoach: ' + (($AE::RootElement.FindAll($TS::Children, (New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, 'FocusCoach'))) | ForEach-Object { '' + $_.Current.ProcessId + '|' + $_.Current.ClassName }) -join ' ')
    $t0 = Get-Date; $seen = @{}; $step = 0
    $mainHwnd = [int64]$main.Current.NativeWindowHandle
    while (((Get-Date) - $t0).TotalSeconds -lt (25 + $ReviewDelay) -and -not $p.HasExited) {
        Start-Sleep -Milliseconds 400
        foreach ($line in [W]::ForPid([uint32]$p.Id)) {
            if ($line -notmatch '^0x([0-9A-F]+) class=(.*?) title=\[(.*?)\] visible=True') { continue }
            $h = [Convert]::ToInt64($matches[1], 16); $cls = $matches[2]; $n = $matches[3]
            if ($h -eq $mainHwnd -or $n -eq '' -or $cls -like 'MSCTFIME*' -or $cls -like 'IME*' -or $cls -like 'Cicero*') { continue }
            if ($seen["$h"]) { continue }
            $seen["$h"] = $true; $step++
            if ($cls -eq '#32770') {
                $kids = [W]::Children([IntPtr]$h) | ForEach-Object { $a = $_ -split '\|', 3; [pscustomobject]@{ H = [int64]$a[0]; C = $a[1]; T = $a[2] } }
                $txt = ($kids | Where-Object { $_.C -eq 'Static' -and $_.T } | ForEach-Object { $_.T -replace "`r?`n", ' / ' }) -join ' // '
                $btns = @($kids | Where-Object { $_.C -eq 'Button' })
                '[{0:0.0}s] MESSAGEBOX #{1}: title=[{2}]' -f ((Get-Date) - $t0).TotalSeconds, $step, $n
                '        text: ' + $txt
                '        buttons: ' + (($btns | ForEach-Object { $_.T }) -join ' | ')
                $want = if ($btns | Where-Object { $_.T -like 'はい*' }) { $Answer } else { 'OK' }
                $b = $btns | Where-Object { $_.T -like "$want*" } | Select-Object -First 1
                if ($n -eq '振り返り') {
                    "        main window IsEnabled while review is up: $($main.Current.IsEnabled)"
                    "        main panel buttons now: " + ((Buttons $main | ForEach-Object { $_.Current.Name }) -join ' | ')
                    if ($ClickDuringReview) { $r = Click $main $ClickDuringReview; "        clicked panel button [$ClickDuringReview] ok=$r"; Start-Sleep -Seconds 2 }
                }
                if ($n -eq '振り返り' -and $ReviewDelay -gt 0) { "        (waiting $ReviewDelay s before dismissing review)"; Start-Sleep -Seconds $ReviewDelay }
                if ($b) { [W]::Click([IntPtr]$b.H); "        -> clicked [$($b.T)]" } else { '        -> no matching button' }
                continue
            }
            $w = $AE::FromHandle([IntPtr]$h)
            $bt = (Buttons $w | ForEach-Object { $_.Current.Name }) -join ' | '
            '[{0:0.0}s] dialog #{1}: title=[{2}] class={3}' -f ((Get-Date) - $t0).TotalSeconds, $step, $n, $cls
            '        text: ' + (Texts $w)
            '        buttons: ' + $bt
            $target = if ($bt -like '*はい*' -and $bt -like '*キャンセル*') { $Answer }
                      elseif ($bt -like '*閉じずに戻す*') { '閉じずに戻す' }
                      else { 'OK' }
            $ok = Click $w $target
            "        -> clicked [$target] ok=$ok"
        }
    }
    "exited=$($p.HasExited) after $([int]((Get-Date) - $t0).TotalSeconds)s dialogs=$step"
}
finally {
    if ($p -and -not $p.HasExited) { 'still running; killing test instance'; $p.Kill() }
}
'--- log ---'
Get-ChildItem (Join-Path $data 'logs') -Filter 'app-*.log' -ErrorAction SilentlyContinue | ForEach-Object { Get-Content $_.FullName -Encoding UTF8 }
Get-ChildItem (Join-Path $data 'logs') -Filter 'crash-*' -ErrorAction SilentlyContinue | ForEach-Object { '--- crash ---'; Get-Content $_.FullName -Encoding UTF8 | Select-Object -First 12 }
