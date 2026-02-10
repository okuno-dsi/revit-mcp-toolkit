param(
  [int]$Port = 5210,
  [string]$Path
)

if(-not $Path){ throw 'Path to data JSON file is required (e.g., Projects/建物1/data1.txt)' }
chcp 65001 > $null
$ErrorActionPreference = 'Stop'
$durable = Join-Path $PSScriptRoot 'send_revit_command_durable.py'

function Call-Durable {
  param([string]$Method, $Params, [int]$WaitSec = 180)
  if(-not $Params){ $Params = @{} }
  $pjson = $Params | ConvertTo-Json -Depth 20 -Compress
  $out = & python $durable --port $Port --command $Method --params $pjson --wait-seconds $WaitSec 2>&1
  try { return $out | ConvertFrom-Json } catch { return @{ ok=$false; raw=$out } }
}

# Read file and extract the JSON array segment
$raw = Get-Content -Raw $Path
$start = $raw.IndexOf('[')
$end = $raw.LastIndexOf(']')
if($start -lt 0 -or $end -lt $start){ throw "JSON array not found in $Path" }
$json = $raw.Substring($start, ($end-$start)+1)
$steps = $json | ConvertFrom-Json

$lastViewId = $null
$is3DViewCache = $null

function Test-IsActiveView3D {
  param()
  if($null -ne $is3DViewCache){ return [bool]$is3DViewCache }
  try {
    $cv = Call-Durable 'get_current_view' @{}
    $vr = $null; if($cv.result -and $cv.result.result){ $vr = $cv.result.result } elseif($cv.result){ $vr = $cv.result } else { $vr = $cv }
    $vid = $null; if($vr -and $vr.viewId){ $vid = [int]$vr.viewId }
    if($vid){
      $info = Call-Durable 'get_view_info' @{ viewId = $vid }
      $ir = $null; if($info.result -and $info.result.result){ $ir = $info.result.result } elseif($info.result){ $ir = $info.result } else { $ir = $info }
      $typ = ($ir.viewType ?? $ir.type ?? '').ToString()
      $is3d = $false
      if($ir.is3D){ $is3d = [bool]$ir.is3D }
      elseif($typ -match '3D'){ $is3d = $true }
      $is3DViewCache = $is3d
      return $is3d
    }
  } catch {}
  $is3DViewCache = $false
  return $false
}
foreach($s in $steps){
  $m = [string]$s.method
  $p = $s.params
  if(-not $p){ $p = @{} }

  switch ($m) {
    'create_view_plan' {
      Write-Host "[send] create_view_plan" -ForegroundColor Cyan
      $r = Call-Durable 'create_view_plan' ($p)
      $res = $null; if($r.result -and $r.result.result){ $res = $r.result.result } elseif($r.result){ $res = $r.result } else { $res = $r }
      if($res -and $res.viewId){ $lastViewId = [int]$res.viewId }
    }
    'activate_view' {
      Write-Host "[send] activate_view" -ForegroundColor Magenta
      $hp = @{}; $p.PSObject.Properties | ForEach-Object { $hp[$_.Name] = $_.Value }
      if($lastViewId -and -not $hp.ContainsKey('viewId')){ $hp['viewId'] = [int]$lastViewId }
      [void](Call-Durable 'activate_view' $hp)
    }
    'create_room' {
      Write-Host "[send] create_room" -ForegroundColor Green
      $name = $null
      if($p.PSObject.Properties.Name -contains 'name'){ $name = [string]$p.name; $p.PSObject.Properties.Remove('name') | Out-Null }
      $r = Call-Durable 'create_room' ($p)
      if($name){
        $res = $null; if($r.result -and $r.result.result){ $res = $r.result.result } elseif($r.result){ $res = $r.result } else { $res = $r }
        $eid = $null; if($res -and $res.elementId){ $eid = [int]$res.elementId }
        if($eid){ [void](Call-Durable 'set_room_param' @{ elementId=$eid; paramName='Name'; value=$name; __smoke_ok=$true }) }
      }
    }
    'view_fit' {
      if(Test-IsActiveView3D){
        Write-Host "[send] view_fit" -ForegroundColor DarkCyan
        [void](Call-Durable 'view_fit' ($p))
      } else { Write-Host "[skip] view_fit (not 3D)" -ForegroundColor DarkYellow }
    }
    'view_orbit' {
      if(Test-IsActiveView3D){
        Write-Host "[send] view_orbit" -ForegroundColor DarkCyan
        [void](Call-Durable 'view_orbit' ($p))
      } else { Write-Host "[skip] view_orbit (not 3D)" -ForegroundColor DarkYellow }
    }
    '__sleep_ms' {
      $ms = 1000; if($p.ms){ $ms = [int]$p.ms } elseif($p.milliseconds){ $ms = [int]$p.milliseconds }
      Start-Sleep -Milliseconds $ms
    }
    default {
      Write-Host "[send] $m" -ForegroundColor Cyan
      $hp = @{}; $p.PSObject.Properties | ForEach-Object { $hp[$_.Name] = $_.Value }
      if($lastViewId -and -not $hp.ContainsKey('viewId')){
        if($m -in @('set_view_template','set_view_parameter','get_view_info','get_visual_overrides_in_view','reset_all_view_overrides')){ $hp['viewId'] = [int]$lastViewId }
      }
      $r = Call-Durable $m ($hp)
      $res = $null; if($r.result -and $r.result.result){ $res = $r.result.result } elseif($r.result){ $res = $r.result } else { $res = $r }
      if($res -and $res.viewId){ $lastViewId = [int]$res.viewId }
    }
  }

  Start-Sleep -Milliseconds 150
}

Write-Host "Done." -ForegroundColor Green

