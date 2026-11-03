# publish-portable.ps1 - 持ち運び版を作って、別の端末で使えるフォルダ(既定 E:\FocusCoach)に置く。
# 自己完結(.NET の実行環境ごと同梱)なので、配布先には何もインストールしなくてよい。
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\publish-portable.ps1 [-Dest E:\FocusCoach]
# 置き換えるのは app\ と起動用ファイルだけ。data\(持ち運び版の記録)は触らない。
# 配布先で使う手順は deploy\README.txt。Windows 専用(WPF)。
param([string]$Dest = 'E:\FocusCoach', [string]$ZipPath = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path $root 'publish\portable'          # .gitignore 済み
if (Test-Path $work) { [IO.Directory]::Delete($work, $true) }   # 古いファイルが残らないよう毎回作り直す

$info = Get-Content (Join-Path $root 'src\FocusCoach.Core\AppInfo.cs') -Raw
if ($info -notmatch 'Version = "([^"]+)"') { throw 'AppInfo.Version が読めません' }
$version = $Matches[1]
"version $version -> $Dest"

dotnet publish (Join-Path $root 'src\FocusCoach.App\FocusCoach.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:DebugType=none -p:DebugSymbols=false -o (Join-Path $work 'app') --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish に失敗しました' }

New-Item -ItemType Directory -Force -Path $Dest | Out-Null
# app\ は丸ごと同期(古いファイルは消える)。data\ には触らない
robocopy (Join-Path $work 'app') (Join-Path $Dest 'app') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy に失敗しました (code $LASTEXITCODE)" }
foreach ($f in 'FocusCoach.cmd', 'FocusCoach-portable.cmd', 'README.txt') {
    Copy-Item (Join-Path $root "deploy\$f") (Join-Path $Dest $f) -Force
}
# ライセンスと第三者の表記(自己完結の配布物には .NET・SQLite・フォントが入っているため、同梱が必要)
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $Dest 'LICENSE.txt') -Force
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $Dest 'THIRD-PARTY-NOTICES.txt') -Force
"FocusCoach $version`r`n作成: $(Get-Date -Format 'yyyy-MM-dd HH:mm')`r`nコミット: $(git -C $root rev-parse --short HEAD)" |
    Set-Content (Join-Path $Dest 'VERSION.txt') -Encoding UTF8
$size = (Get-ChildItem $Dest -Recurse -File | Measure-Object Length -Sum).Sum / 1MB
'done: {0:N0} MB, {1}' -f $size, $Dest
if ($ZipPath) {
    # 配布用の ZIP(フォルダ名 FocusCoach の下に app\ などが入る)と SHA-256。data\(持ち運び版の記録)は入れない
    if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
    $stage = Join-Path $work 'zipstage'
    if (Test-Path $stage) { [IO.Directory]::Delete($stage, $true) }
    robocopy $Dest (Join-Path $stage 'FocusCoach') /MIR /XD data /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy に失敗しました (code $LASTEXITCODE)" }
    Compress-Archive -Path (Join-Path $stage 'FocusCoach') -DestinationPath $ZipPath -CompressionLevel Optimal
    $hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path $ZipPath -Leaf)" | Set-Content ($ZipPath + '.sha256') -Encoding ascii
    'zip: {0:N0} MB  sha256 {1}' -f ((Get-Item $ZipPath).Length / 1MB), $hash
}
