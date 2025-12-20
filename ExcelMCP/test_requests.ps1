param(
  [string]$BaseUrl = 'http://localhost:5215',
  [string]$ExcelPath = ''
)
$ErrorActionPreference = 'Stop'

Write-Host "[TEST] BaseUrl = $BaseUrl" -ForegroundColor Cyan

# Wait health
$healthy = $false
for($i=0;$i -lt 30;$i++){
  Start-Sleep -Milliseconds 500
  try{ $res = Invoke-RestMethod -Method GET -Uri "$BaseUrl/health" -TimeoutSec 2; if($res.ok){ $healthy=$true; break } }catch{}
}
if(-not $healthy){ throw "Server is not healthy at $BaseUrl" }

# Pick a workbook if not supplied
if([string]::IsNullOrWhiteSpace($ExcelPath)){
  $src = Get-ChildItem -Recurse -Filter *.xlsx | Where-Object { $_.FullName -notlike '*ExcelMCP\\tmp*' } | Select-Object -First 1 -ExpandProperty FullName
  if(-not $src){ throw 'No .xlsx found in repo. Provide -ExcelPath.' }
  $tmp = Join-Path $PSScriptRoot 'tmp'
  New-Item -ItemType Directory -Force -Path $tmp | Out-Null
  $ExcelPath = Join-Path $tmp ([IO.Path]::GetFileName($src))
  if($src -ne $ExcelPath){ Copy-Item $src $ExcelPath -Force }
}

Write-Host "[TEST] Using Excel: $ExcelPath" -ForegroundColor Cyan

$sheetInfo = Invoke-RestMethod -Method Post -Uri "$BaseUrl/sheet_info" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath } | ConvertTo-Json)
Write-Host "[OK] sheet_info: $($sheetInfo.sheets.Count) sheets" -ForegroundColor Green

$null = Invoke-RestMethod -Method Post -Uri "$BaseUrl/write_cells" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; startCell='B2'; values=@(@(1,2,3),@('a','b','c')) } | ConvertTo-Json -Depth 10)
Write-Host "[OK] write_cells" -ForegroundColor Green

$appendRes = Invoke-RestMethod -Method Post -Uri "$BaseUrl/append_rows" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; startColumn='A'; rows=@(@('X','Y','Z'),@(10,20,30)) } | ConvertTo-Json -Depth 10)
Write-Host "[OK] append_rows: $($appendRes.appendedRows) rows" -ForegroundColor Green

$formula = Invoke-RestMethod -Method Post -Uri "$BaseUrl/set_formula" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; target='E2:E3'; formulaA1='=SUM(B2:D2)' } | ConvertTo-Json)
Write-Host "[OK] set_formula: $($formula.cells) cells" -ForegroundColor Green

$format = Invoke-RestMethod -Method Post -Uri "$BaseUrl/format_sheet" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; autoFitColumns=$true; columnWidths=@{ A=18; C=120 }; widthUnit='pixels' } | ConvertTo-Json)
Write-Host "[OK] format_sheet" -ForegroundColor Green

$csvPath = Join-Path (Split-Path $ExcelPath) (([IO.Path]::GetFileNameWithoutExtension($ExcelPath)) + '.csv')
$csv = Invoke-RestMethod -Method Post -Uri "$BaseUrl/to_csv" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; outputCsvPath=$csvPath; delimiter=','; useFormattedText=$false; encodingName='utf-8' } | ConvertTo-Json)
Write-Host "[OK] to_csv: rows=$($csv.rows) -> $csvPath" -ForegroundColor Green

$jsonPath = Join-Path (Split-Path $ExcelPath) (([IO.Path]::GetFileNameWithoutExtension($ExcelPath)) + '.json')
$json = Invoke-RestMethod -Method Post -Uri "$BaseUrl/to_json" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; outputJsonPath=$jsonPath; mode='records'; useFormattedText=$false; indented=$true; emptyAsNull=$true; skipBlankRows=$true } | ConvertTo-Json)
if(-not (Test-Path $jsonPath)){ throw "to_json did not create file: $jsonPath" }
Write-Host "[OK] to_json: rows=$($json.rows) -> $jsonPath" -ForegroundColor Green

$read = Invoke-RestMethod -Method Post -Uri "$BaseUrl/read_cells" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath; sheetName='Sheet1'; returnRaw=$true; includeFormula=$true } | ConvertTo-Json)
Write-Host "[OK] read_cells: $($read.cells.Count) cells" -ForegroundColor Green

$charts = Invoke-RestMethod -Method Post -Uri "$BaseUrl/list_charts" -ContentType 'application/json' -Body (@{ excelPath=$ExcelPath } | ConvertTo-Json)
Write-Host "[OK] list_charts: $($charts.charts.Count) charts" -ForegroundColor Green

Write-Host "[DONE] All tests passed" -ForegroundColor Cyan
