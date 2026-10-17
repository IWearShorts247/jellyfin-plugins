<#
.SYNOPSIS
Exports every user's watch state (played, play count, resume position, favorite)
for all movies/series/seasons/episodes, keyed by file path.

Run BEFORE restructuring libraries. Read-only — makes no changes to the server.

.EXAMPLE
.\export-watchstate.ps1 -ServerUrl http://192.168.1.50:8096 -ApiKeyFile .\apikey.txt -OutFile .\watchstate.json

Create the API key in Jellyfin: Dashboard -> API Keys -> +. Put it in apikey.txt
(just the key, one line).
#>
param(
    [Parameter(Mandatory = $true)][string]$ServerUrl,
    [Parameter(Mandatory = $true)][string]$ApiKeyFile,
    [string]$OutFile = '.\watchstate.json'
)
$ErrorActionPreference = 'Stop'
$apiKey = (Get-Content $ApiKeyFile -TotalCount 1).Trim()
$headers = @{ 'Authorization' = "MediaBrowser Token=`"$apiKey`"" }
$ServerUrl = $ServerUrl.TrimEnd('/')

$users = Invoke-RestMethod -Uri "$ServerUrl/Users" -Headers $headers
Write-Host "Found $($users.Count) users"

$records = [System.Collections.Generic.List[object]]::new()
foreach ($user in $users) {
    $startIndex = 0
    $pageSize = 500
    $userTotal = 0
    while ($true) {
        $uri = "$ServerUrl/Items?userId=$($user.Id)&Recursive=true" +
            "&IncludeItemTypes=Movie,Series,Season,Episode,Video" +
            "&Fields=Path&EnableUserData=true&EnableImages=false" +
            "&StartIndex=$startIndex&Limit=$pageSize"
        $page = Invoke-RestMethod -Uri $uri -Headers $headers
        foreach ($item in $page.Items) {
            $ud = $item.UserData
            if ($null -eq $ud -or [string]::IsNullOrEmpty($item.Path)) { continue }
            # Only export state worth carrying over.
            if (-not ($ud.Played -or $ud.IsFavorite -or $ud.PlaybackPositionTicks -gt 0 -or $ud.PlayCount -gt 0)) { continue }
            $records.Add([pscustomobject]@{
                UserId                = $user.Id
                UserName              = $user.Name
                Path                  = $item.Path
                ItemName              = $item.Name
                Played                = [bool]$ud.Played
                PlayCount             = [int]$ud.PlayCount
                PlaybackPositionTicks = [long]$ud.PlaybackPositionTicks
                IsFavorite            = [bool]$ud.IsFavorite
                LastPlayedDate        = $ud.LastPlayedDate
            })
            $userTotal++
        }
        $startIndex += $pageSize
        if ($startIndex -ge $page.TotalRecordCount) { break }
    }
    Write-Host ("  {0}: {1} items with watch state" -f $user.Name, $userTotal)
}

$records | ConvertTo-Json -Depth 5 | Set-Content $OutFile -Encoding UTF8
Write-Host "`nExported $($records.Count) records to $OutFile"
