# @feature: test terminology routing | keywords: ビュー
param(
  [int]$Port = 5210,
  [int]$Limit = 12
)

chcp 65001 > $null
$env:PYTHONUTF8='1'

$SCRIPT_DIR = $PSScriptRoot
$PY = Join-Path $SCRIPT_DIR 'send_revit_command_durable.py'

function Invoke-RevitMcp([string]$Command, [hashtable]$Params){
  $json = ($Params | ConvertTo-Json -Depth 12 -Compress)
  $out = python $PY --port $Port --command $Command --params $json
  try { return $out | ConvertFrom-Json } catch {
    throw "Failed to parse JSON. Raw output:`n$out"
  }
}

function Get-SearchItems($obj){
  $r = $obj
  if($null -ne $obj.result){ $r = $obj.result }
  if($null -ne $r.data -and $null -ne $r.data.items){ return @($r.data.items) }
  if($null -ne $r.items){ return @($r.items) }
  return @()
}

function IndexOfCommand($items, [string]$name){
  for($i=0; $i -lt $items.Count; $i++){
    if($items[$i].name -eq $name){ return $i }
  }
  return -1
}

$cases = @(
  @{ q = "断面";   expectBefore = @{ a="create_section"; b="create_view_plan" } },
  @{ q = "平断面"; expectBefore = @{ a="create_view_plan"; b="create_section" } },
  @{ q = "立面";   expectContains = @("create_elevation_view") },
  @{ q = "RCP";    expectContains = @("create_view_plan") }
)

$fail = $false
foreach($c in $cases){
  Write-Host ("[search] {0}" -f $c.q) -ForegroundColor Cyan
  $obj = Invoke-RevitMcp -Command "help.search_commands" -Params @{ query = $c.q; limit = $Limit }
  $items = Get-SearchItems $obj
  if(-not $items -or $items.Count -eq 0){
    Write-Host "  FAIL: no items returned" -ForegroundColor Red
    $fail = $true
    continue
  }

  if($c.expectBefore){
    $a = $c.expectBefore.a
    $b = $c.expectBefore.b
    $ia = IndexOfCommand $items $a
    $ib = IndexOfCommand $items $b
    if($ia -lt 0 -or $ib -lt 0){
      Write-Host ("  FAIL: missing a={0} (idx={1}) or b={2} (idx={3})" -f $a,$ia,$b,$ib) -ForegroundColor Red
      $fail = $true
      continue
    }
    if($ia -ge $ib){
      Write-Host ("  FAIL: expected {0} before {1}, but idx {2} >= {3}" -f $a,$b,$ia,$ib) -ForegroundColor Red
      $fail = $true
      continue
    }
    Write-Host ("  OK: {0} (idx={1}) before {2} (idx={3})" -f $a,$ia,$b,$ib) -ForegroundColor Green
  }

  if($c.expectContains){
    foreach($name in $c.expectContains){
      $idx = IndexOfCommand $items $name
      if($idx -lt 0){
        Write-Host ("  FAIL: missing {0}" -f $name) -ForegroundColor Red
        $fail = $true
      } else {
        Write-Host ("  OK: contains {0} (idx={1})" -f $name,$idx) -ForegroundColor Green
      }
    }
  }
}

if($fail){
  Write-Host "One or more checks failed." -ForegroundColor Red
  exit 1
}

Write-Host "All checks passed." -ForegroundColor Green

