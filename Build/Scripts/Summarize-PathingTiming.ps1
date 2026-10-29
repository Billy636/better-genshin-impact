param(
    [Parameter(Mandatory = $true)][string]$LogDirectory,
    [ValidatePattern('^\d{8}$')][string]$Day = (Get-Date).ToString('yyyyMMdd'),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$phaseNames = @('Teleport', 'Move', 'Fight', 'Pickup', 'Other')
$records = @()
$invalidLines = 0
foreach ($file in Get-ChildItem -LiteralPath $LogDirectory -Filter "timing-$Day-*.jsonl" -File) {
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $records += $line | ConvertFrom-Json } catch { $invalidLines++ }
    }
}

# 只将完整路线合计为正常基线；取消和未完成路线保留独立数量，不能伪装成成功运行。
$completed = @($records | Where-Object { $_.outcome -eq 'completed' })
$groups = @($completed | Group-Object group | ForEach-Object {
    $routes = @($_.Group)
    $phases = [ordered]@{}
    foreach ($phase in $phaseNames) {
        $sum = ($routes | ForEach-Object { $_.phasesMs.$phase } | Measure-Object -Sum).Sum
        $phases[$phase] = [math]::Round($sum / 1000, 3)
    }
    [pscustomobject]@{
        Group = $_.Name
        CompletedRoutes = $routes.Count
        TotalSeconds = [math]::Round(($routes | Measure-Object totalMs -Sum).Sum / 1000, 3)
        PhaseSeconds = $phases
        EndCheckSeconds = [math]::Round(($routes | ForEach-Object { $_.detailsIncludedInPhases.fight_end_check.totalMs } | Measure-Object -Sum).Sum / 1000, 3)
        EmptyPickupSweeps = ($routes | ForEach-Object { $_.counts.pickup_empty_sweeps } | Measure-Object -Sum).Sum
        Retries = ($routes | ForEach-Object { $_.counts.route_retries + $_.counts.route_retries_no_count } | Measure-Object -Sum).Sum
    }
})

$report = [ordered]@{
    Day = $Day
    RecordedRoutes = $records.Count
    CompletedRoutes = $completed.Count
    OtherOutcomes = @($records | Where-Object { $_.outcome -ne 'completed' } | Group-Object outcome | Select-Object Name, Count)
    InvalidOrIncompleteLines = $invalidLines
    Groups = $groups
    Notes = @(
        'PhaseSeconds 为互斥墙钟时间，包含手动暂停和游戏等待；中断路线已从正常基线分离。',
        'EndCheckSeconds 包含在 Fight 内，不能与主阶段相加；不代表怪物死亡后的纯空等时间。',
        '模板或原版自动拾取可与跑图/战斗并行；Pickup 仅表示明确的战后或点位拾取阶段。',
        'Other 包含初始化、换队、低血恢复及动作结束等待，不强行分入四阶段。'
    )
}
$json = $report | ConvertTo-Json -Depth 8
if ($OutputPath) { [IO.File]::WriteAllText($OutputPath, $json, (New-Object Text.UTF8Encoding $false)) }
$json
