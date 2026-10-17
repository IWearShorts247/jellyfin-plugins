<#
.SYNOPSIS
Re-applies watch state exported by export-watchstate.ps1 to the current
library items, matching by file path. Run AFTER the new combined libraries
have finished scanning. Use -DryRun first to see what would change.

.EXAMPLE
.\import-watchstate.ps1 -ServerUrl http://192.168.1.50:8096 -ApiKeyFile .\apikey.txt -InFile .\watchstate.json -DryRun
#>
param(
    [Parameter(Mandatory = $true)][string]$ServerUrl,
    [Parameter(Mandatory = $true)][string]$ApiKeyFile,
    [string]$InFile = '.\watchstate.json',
    [switch]$DryRun
)
$ErrorActionPreference = 'Stop'
$apiKey = (Get-Content $ApiKeyFile -TotalCount 1).Trim()
$headers = @{ 'Authorization' = "MediaBrowser Token=`"$apiKey`""; 'Content-Type' = 'application/json' }
$ServerUrl = $ServerUrl.TrimEnd('/')

$records = Get-Content $InFile -Raw | ConvertFrom-Json
Write-Host "Loaded $($records.Count) records from $InFile"

# Build path -> current itemId map across all libraries.
Write-Host 'Mapping current library paths to item ids...'
$pathToId = @{}
$startIndex = 0
$pageSize = 500
while ($true) {
    $uri = "$ServerUrl/Items?Recursive=true" +
        "&IncludeItemTypes=Movie,Series,Season,Episode,Video" +
        "&Fields=Path&EnableImages=false&EnableUserData=false" +
        "&StartIndex=$startIndex&Limit=$pageSize"
    $page = Invoke-RestMethod -Uri $uri -Headers $headers
    foreach ($item in $page.Items) {
        if (-not [string]::IsNullOrEmpty($item.Path)) {
            # Same path can appear in old + new libraries while both exist;
            # keep a list and apply to every id so the new copies get state.
            if (-not $pathToId.ContainsKey($item.Path)) { $pathToId[$item.Path] = @() }
            $pathToId[$item.Path] += $item.Id
        }
    }
    $startIndex += $pageSize
    if ($startIndex -ge $page.TotalRecordCount) { break }
}
Write-Host "  $($pathToId.Count) distinct paths on server"

$applied = 0
$missing = 0
foreach ($rec in $records) {
    if (-not $pathToId.ContainsKey($rec.Path)) {
        Write-Warning "No current item for path: $($rec.Path)"
        $missing++
        continue
    }
    foreach ($itemId in $pathToId[$rec.Path]) {
        $body = @{
            Played                = $rec.Played
            PlayCount             = $rec.PlayCount
            PlaybackPositionTicks = $rec.PlaybackPositionTicks
            IsFavorite            = $rec.IsFavorite
        }
        if ($rec.LastPlayedDate) { $body.LastPlayedDate = $rec.LastPlayedDate }
        if ($DryRun) {
            Write-Host ("DRYRUN {0}: {1} -> played={2} pos={3} fav={4}" -f $rec.UserName, $rec.ItemName, $rec.Played, $rec.PlaybackPositionTicks, $rec.IsFavorite)
        }
        else {
            Invoke-RestMethod -Method Post `
                -Uri "$ServerUrl/UserItems/$itemId/UserData?userId=$($rec.UserId)" `
                -Headers $headers -Body ($body | ConvertTo-Json) | Out-Null
        }
        $applied++
    }
}

Write-Host "`n$(if ($DryRun) { 'Would apply' } else { 'Applied' }) $applied updates; $missing exported paths not found on server."
if ($missing -gt 0) { Write-Host 'Missing paths usually mean the new library has not finished scanning yet.' }
