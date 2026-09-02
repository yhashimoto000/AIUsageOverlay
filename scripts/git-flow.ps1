<#
.SYNOPSIS
    GIT_WORKFLOW.md の運用（wip 1 本使い回し + 退避）を補助するヘルパー。

.DESCRIPTION
    .claude/git-rules.json に記録したルールに沿って、手順の多い操作をまとめる。
    とくに squash 取り込みは「取り込み → コミット → 退避 → wip 作り直し」の 4 手あり、
    退避を飛ばすと wip 側の細かい履歴が復元できなくなる。ここを 1 コマンドにする。

    コンソール出力は ASCII 英語に固定する（CLAUDE.md の環境規約。日本語だと
    実行環境のコードページによって文字化けするため）。コード内コメントは日本語。

    破壊的な操作（push --force / reset --hard / clean -fd）は一切行わない。
    push は対象を明示した push のみで、--all / --mirror は使わない。

.PARAMETER Command
    実行するサブコマンド。省略すると status を表示する。

.EXAMPLE
    .\scripts\git-flow.ps1 start feat usage-dashboard
    .\scripts\git-flow.ps1 squash
    .\scripts\git-flow.ps1 integrate
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('status', 'start', 'wip', 'squash', 'integrate', 'archives', 'show', 'help')]
    [string]$Command = 'status',

    [Parameter(Position = 1)]
    [string]$Arg1,

    [Parameter(Position = 2)]
    [string]$Arg2,

    # 確認プロンプトを飛ばす（自動実行用）
    [switch]$Yes
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# ── 設定の読み込み ────────────────────────────────────────────────

# リポジトリのルートを起点にする（scripts/ から呼ばれても動くように）
$RepoRoot = (git rev-parse --show-toplevel 2>$null)
if (-not $RepoRoot) {
    Write-Error 'Not inside a git repository.'
    exit 1
}
Set-Location $RepoRoot

$RulesPath = Join-Path $RepoRoot '.claude/git-rules.json'
if (-not (Test-Path $RulesPath)) {
    Write-Error "git-rules.json not found: $RulesPath"
    exit 1
}
$Rules = Get-Content $RulesPath -Raw -Encoding UTF8 | ConvertFrom-Json

$BaseBranch = $Rules.base_branch
$WipBranch = $Rules.work_branch.branch
$ArchiveNs = $Rules.work_branch.archive_namespace
$TypeNames = @($Rules.types | ForEach-Object { $_.name })
$PushBase = @($Rules.push_targets.base)
$PushType = @($Rules.push_targets.type)

# ── 共通処理 ──────────────────────────────────────────────────────

<#
    情報・注意・エラーの出し分け。ASCII のみを使う。
#>
function Write-Info { param([string]$Message) Write-Host "[info] $Message" }
function Write-Step { param([string]$Message) Write-Host "[ >> ] $Message" -ForegroundColor Cyan }
function Write-Warn { param([string]$Message) Write-Host "[warn] $Message" -ForegroundColor Yellow }
function Write-Fail { param([string]$Message) Write-Host "[fail] $Message" -ForegroundColor Red }

<#
    git を実行し、失敗したらその場で止める。
    戻り値を使いたい場合は -Capture を付ける（その場合も失敗時は停止する）。
#>
function Invoke-Git {
    param(
        [Parameter(Mandatory)][string[]]$GitArgs,
        [switch]$Capture,
        [switch]$AllowFailure
    )
    if ($Capture) {
        $output = & git @GitArgs 2>&1
    }
    else {
        Write-Host "       git $($GitArgs -join ' ')" -ForegroundColor DarkGray
        & git @GitArgs
        $output = $null
    }
    if ($LASTEXITCODE -ne 0 -and -not $AllowFailure) {
        Write-Fail "git $($GitArgs -join ' ') failed (exit $LASTEXITCODE)."
        exit 1
    }
    return $output
}

<# 現在のブランチ名を返す。 #>
function Get-CurrentBranch {
    return (git rev-parse --abbrev-ref HEAD).Trim()
}

<#
    作業ツリーが汚れていないことを確かめる。
    ブランチを切り替える操作の前に必ず呼ぶ（未コミットの変更を持ち回らないため）。
#>
function Assert-CleanTree {
    $dirty = git status --porcelain --untracked-files=no
    if ($dirty) {
        Write-Fail 'Working tree has uncommitted changes. Commit or stash first.'
        git status --short
        exit 1
    }
}

<# 現在が種別ブランチ（<type>/<topic>）かを判定し、種別とトピックを返す。 #>
function Get-TypeBranchParts {
    param([string]$Branch)
    $parts = $Branch.Split('/', 2)
    if ($parts.Count -ne 2) { return $null }
    if ($TypeNames -notcontains $parts[0]) { return $null }
    return [pscustomobject]@{ Type = $parts[0]; Topic = $parts[1] }
}

<# 続行してよいかを尋ねる。-Yes 指定時は常に true。 #>
function Confirm-Step {
    param([string]$Message)
    if ($Yes) { return $true }
    $answer = Read-Host "$Message [y/N]"
    return ($answer -eq 'y' -or $answer -eq 'Y')
}

<# 指定ブランチを、記録された push 先すべてへ送る。 #>
function Push-Branch {
    param(
        [Parameter(Mandatory)][string]$Branch,
        [Parameter(Mandatory)][string[]]$Remotes
    )
    foreach ($remote in $Remotes) {
        Write-Step "push $Branch -> $remote"
        Invoke-Git @('push', $remote, $Branch)
    }
}

# ── サブコマンド ──────────────────────────────────────────────────

<#
    現状を 1 画面で見せる。
    どのブランチにいるか、wip があるか、退避が何本あるか、未 push があるか。
#>
function Invoke-Status {
    $branch = Get-CurrentBranch
    Write-Host ''
    Write-Host "Repository : $RepoRoot"
    Write-Host "Branch     : $branch"

    $parts = Get-TypeBranchParts $branch
    if ($branch -eq $WipBranch) {
        Write-Host 'Kind       : work branch (local only, never pushed)'
    }
    elseif ($parts) {
        Write-Host "Kind       : type branch (type=$($parts.Type) topic=$($parts.Topic))"
    }
    elseif ($branch -eq $BaseBranch) {
        Write-Host 'Kind       : base branch (do not commit directly)'
    }
    else {
        Write-Host 'Kind       : other'
    }

    $dirty = git status --porcelain --untracked-files=no
    Write-Host "Tree       : $(if ($dirty) { 'dirty' } else { 'clean' })"

    $wipExists = git rev-parse --verify --quiet "refs/heads/$WipBranch"
    Write-Host "Work branch: $(if ($wipExists) { "$WipBranch exists" } else { "$WipBranch not created" })"

    Write-Host ''
    Write-Host 'Archived work histories:'
    $archives = git for-each-ref --format='%(refname:short)  %(objectname:short)' $ArchiveNs
    if ($archives) { $archives | ForEach-Object { Write-Host "  $_" } }
    else { Write-Host '  (none)' }

    Write-Host ''
    Write-Host "Unpushed commits on $BaseBranch :"
    foreach ($remote in $PushBase) {
        $ahead = git rev-list --count "$remote/$BaseBranch..$BaseBranch" 2>$null
        if ($LASTEXITCODE -eq 0) { Write-Host "  $remote : $ahead commit(s) ahead" }
        else { Write-Host "  $remote : (no tracking ref)" }
    }
    Write-Host ''
}

<#
    種別ブランチを作り、そこから wip を用意する。
    base を最新化してから分岐する（古い base から生やすと後のマージが荒れる）。
#>
function Invoke-Start {
    param([string]$Type, [string]$Topic)

    if (-not $Type -or -not $Topic) {
        Write-Fail 'Usage: git-flow.ps1 start <type> <topic>'
        Write-Host "       type: $($TypeNames -join ', ')"
        exit 1
    }
    if ($TypeNames -notcontains $Type) {
        Write-Fail "Unknown type '$Type'. Allowed: $($TypeNames -join ', ')"
        exit 1
    }
    Assert-CleanTree

    $branch = "$Type/$Topic"
    Write-Step "create type branch $branch from $BaseBranch"
    Invoke-Git @('switch', $BaseBranch)
    # 最初の push 先から早送りのみで取り込む（マージコミットを作らない）
    Invoke-Git @('pull', '--ff-only', $PushBase[0], $BaseBranch) -AllowFailure
    Invoke-Git @('switch', '-c', $branch)

    Push-Branch -Branch $branch -Remotes $PushType

    Write-Step "create work branch $WipBranch from $branch"
    Invoke-Git @('switch', '-C', $WipBranch, $branch)

    Write-Info "Ready. Commit freely on '$WipBranch' (it is never pushed)."
    Write-Info "When the subtask is done: .\scripts\git-flow.ps1 squash"
}

<#
    現在の種別ブランチから wip を作り直す。
    squash 済みの wip を捨てて次のサブタスクへ移るときに使う。
#>
function Invoke-Wip {
    Assert-CleanTree
    $branch = Get-CurrentBranch

    if ($branch -eq $WipBranch) {
        Write-Fail "Already on '$WipBranch'. Switch to a type branch first."
        exit 1
    }
    $parts = Get-TypeBranchParts $branch
    if (-not $parts) {
        Write-Fail "Current branch '$branch' is not a type branch (<type>/<topic>)."
        exit 1
    }

    Write-Step "recreate $WipBranch from $branch"
    Invoke-Git @('switch', '-C', $WipBranch, $branch)
    Write-Info "Ready. Commit freely on '$WipBranch'."
}

<#
    wip の内容を種別ブランチへ squash で取り込み、細かい履歴を退避してから
    wip を作り直す。GIT_WORKFLOW.md の手順 3 に対応する。

    コミットは -m を付けずに実行する。--squash が .git/SQUASH_MSG へ書き出した
    wip 側のログがエディタの初期内容になるため、先頭 1 行を残せば
    wip と種別ブランチのメッセージが一致する（-m を付けると SQUASH_MSG は無視される）。
#>
function Invoke-Squash {
    param([string]$Topic)

    Assert-CleanTree
    $current = Get-CurrentBranch

    # wip にいる場合は、分岐元の種別ブランチを推定して戻る
    if ($current -eq $WipBranch) {
        $typeBranch = $null
        foreach ($candidate in (git for-each-ref --format='%(refname:short)' refs/heads)) {
            $parts = Get-TypeBranchParts $candidate
            if (-not $parts) { continue }
            # wip が候補ブランチの子孫なら、その候補が分岐元
            git merge-base --is-ancestor $candidate $WipBranch 2>$null
            if ($LASTEXITCODE -eq 0) { $typeBranch = $candidate; break }
        }
        if (-not $typeBranch) {
            Write-Fail "Cannot determine the type branch for '$WipBranch'. Switch to it manually."
            exit 1
        }
    }
    else {
        $parts = Get-TypeBranchParts $current
        if (-not $parts) {
            Write-Fail "Current branch '$current' is not a type branch (<type>/<topic>)."
            exit 1
        }
        $typeBranch = $current
    }

    $wipExists = git rev-parse --verify --quiet "refs/heads/$WipBranch"
    if (-not $wipExists) {
        Write-Fail "Work branch '$WipBranch' does not exist."
        exit 1
    }

    # 取り込む差分が無ければ何もしない
    $pending = git rev-list --count "$typeBranch..$WipBranch"
    if ([int]$pending -eq 0) {
        Write-Info "Nothing to squash ($WipBranch has no commits beyond $typeBranch)."
        return
    }

    if (-not $Topic) { $Topic = (Get-TypeBranchParts $typeBranch).Topic }
    $archiveRef = "$ArchiveNs/$Topic"

    Write-Host ''
    Write-Host "Squash $pending commit(s) from '$WipBranch' into '$typeBranch'."
    Write-Host "Archive ref : $archiveRef"
    Write-Host ''
    git log --oneline "$typeBranch..$WipBranch"
    Write-Host ''
    if (-not (Confirm-Step 'Proceed?')) { Write-Info 'Aborted.'; return }

    Write-Step "switch to $typeBranch"
    Invoke-Git @('switch', $typeBranch)

    Write-Step "merge --squash $WipBranch"
    Invoke-Git @('merge', '--squash', $WipBranch)

    Write-Info 'An editor opens. Keep only the first line of SQUASH_MSG, then save.'
    Write-Step 'commit (no -m: reuse SQUASH_MSG)'
    Invoke-Git @('commit')

    # 退避は wip を作り直す前に行う。順序を逆にすると細かい履歴が失われる
    Write-Step "archive work history to $archiveRef"
    Invoke-Git @('update-ref', $archiveRef, $WipBranch)

    Push-Branch -Branch $typeBranch -Remotes $PushType

    Write-Step "recreate $WipBranch from $typeBranch"
    Invoke-Git @('switch', '-C', $WipBranch, $typeBranch)

    Write-Info "Done. Read the archived history with: git log $archiveRef"
}

<#
    種別ブランチを base へ --no-ff でマージし、記録された push 先すべてへ送る。
    マージコミットにブランチ名が残るため、後からトピック単位で追える。
#>
function Invoke-Integrate {
    Assert-CleanTree
    $current = Get-CurrentBranch

    if ($current -eq $WipBranch) {
        Write-Fail "On '$WipBranch'. Run 'squash' first, then switch to the type branch."
        exit 1
    }
    $parts = Get-TypeBranchParts $current
    if (-not $parts) {
        Write-Fail "Current branch '$current' is not a type branch (<type>/<topic>)."
        exit 1
    }

    $pending = git rev-list --count "$BaseBranch..$current"
    if ([int]$pending -eq 0) {
        Write-Info "Nothing to integrate ('$current' has no commits beyond $BaseBranch)."
        return
    }

    Write-Host ''
    Write-Host "Merge '$current' into '$BaseBranch' with --no-ff ($pending commit(s))."
    git log --oneline "$BaseBranch..$current"
    Write-Host ''
    Write-Host "Then push $BaseBranch to: $($PushBase -join ', ')"
    Write-Host ''
    if (-not (Confirm-Step 'Proceed?')) { Write-Info 'Aborted.'; return }

    Write-Step "switch to $BaseBranch"
    Invoke-Git @('switch', $BaseBranch)
    Invoke-Git @('pull', '--ff-only', $PushBase[0], $BaseBranch) -AllowFailure

    Write-Step "merge --no-ff $current"
    Invoke-Git @('merge', '--no-ff', $current)

    Push-Branch -Branch $BaseBranch -Remotes $PushBase

    Write-Info "Merged. Release steps are manual: see README (Release section)."
}

<# 退避した wip の一覧を出す。 #>
function Invoke-Archives {
    Write-Host ''
    Write-Host "Archived work histories under $ArchiveNs :"
    $rows = git for-each-ref --format='%(refname:short)|%(objectname:short)|%(committerdate:short)' $ArchiveNs
    if (-not $rows) {
        Write-Host '  (none)'
        Write-Host ''
        return
    }
    foreach ($row in $rows) {
        $cols = $row.Split('|')
        Write-Host ("  {0,-44} {1}  {2}" -f $cols[0], $cols[1], $cols[2])
    }
    Write-Host ''
    Write-Host 'Read one with: .\scripts\git-flow.ps1 show <topic>'
    Write-Host ''
}

<# 退避した wip のコミット履歴を表示する。 #>
function Invoke-Show {
    param([string]$Topic)
    if (-not $Topic) {
        Write-Fail 'Usage: git-flow.ps1 show <topic>'
        exit 1
    }
    $ref = "$ArchiveNs/$Topic"
    $exists = git rev-parse --verify --quiet $ref
    if (-not $exists) {
        Write-Fail "Archive ref not found: $ref"
        Write-Host 'List them with: .\scripts\git-flow.ps1 archives'
        exit 1
    }
    git log --oneline --graph $ref
}

<# 使い方を出す。 #>
function Invoke-Help {
    Write-Host @"

git-flow.ps1 - helper for the branch workflow in GIT_WORKFLOW.md

Usage:
  .\scripts\git-flow.ps1 <command> [args] [-Yes]

Commands:
  status                  Show current branch, work branch, archives, unpushed commits
  start <type> <topic>    Create <type>/<topic> from $BaseBranch, push it, then create '$WipBranch'
  wip                     Recreate '$WipBranch' from the current type branch
  squash [<topic>]        Squash '$WipBranch' into the type branch, archive it, recreate '$WipBranch'
  integrate               Merge the current type branch into $BaseBranch (--no-ff) and push
  archives                List archived work histories under $ArchiveNs
  show <topic>            Show the commit log of an archived work history
  help                    Show this help

Types: $($TypeNames -join ', ')
Push targets: type -> $($PushType -join ', ') / $BaseBranch -> $($PushBase -join ', ')

Notes:
  - '$WipBranch' is never pushed. Archives under $ArchiveNs are never pushed either.
  - Release (version bump, tag, tag push) is intentionally NOT automated. See README.
  - This script never runs push --force / reset --hard / clean -fd / push --all / push --mirror.

"@
}

# ── 実行 ──────────────────────────────────────────────────────────

switch ($Command) {
    'status' { Invoke-Status }
    'start' { Invoke-Start -Type $Arg1 -Topic $Arg2 }
    'wip' { Invoke-Wip }
    'squash' { Invoke-Squash -Topic $Arg1 }
    'integrate' { Invoke-Integrate }
    'archives' { Invoke-Archives }
    'show' { Invoke-Show -Topic $Arg1 }
    'help' { Invoke-Help }
}
