param(
  [int]$Port = 5210,
  [string]$OutPath,
  [switch]$CsvFallbackOnly,
  [switch]$ReuseCsv
)

$ErrorActionPreference = 'Stop'

function Invoke-RevitCommandJson {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [hashtable]$Params = @{},
    [int]$Port = 5210
  )
  $paramsJson = if ($Params) { $Params | ConvertTo-Json -Compress } else { '{}' }
  $tmp = New-TemporaryFile
  try {
    $argsList = @(
      "Scripts/Reference/send_revit_command_durable.py",
      "--port", $Port,
      "--command", $Method,
      "--params", $paramsJson,
      "--output-file", $tmp.FullName
    )
    python @argsList | Out-Null
    $j = Get-Content -Raw -LiteralPath $tmp.FullName | ConvertFrom-Json
    return $j.result.result
  } finally {
    Remove-Item -ErrorAction SilentlyContinue $tmp.FullName
  }
}

function Resolve-LogsDir([int]$p){
  $work = Resolve-Path (Join-Path $PSScriptRoot '..\\..\\Work')
  $cands = Get-ChildItem -LiteralPath $work -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*_$p" }
  $chosen = $null
  if($cands){ $chosen = ($cands | Where-Object { $_.Name -notlike 'Project_*' } | Select-Object -First 1); if(-not $chosen){ $chosen = $cands | Select-Object -First 1 } }
  if(-not $chosen){ $chosen = New-Item -ItemType Directory -Path (Join-Path $work ("Project_{0}" -f $p)) }
  $logs = Join-Path $chosen.FullName 'Logs'
  if(-not (Test-Path $logs)){ New-Item -ItemType Directory -Path $logs | Out-Null }
  return $logs
}

function Get-ProjectNameSafe {
  param([int]$Port)
  $logs = Resolve-LogsDir -p $Port
  $cands = @(
    (Join-Path $logs ("project_info_{0}.json" -f $Port)),
    (Join-Path $logs 'project_info.json'),
    (Join-Path 'Manuals/Logs' ("project_info_{0}.json" -f $Port)),
    (Join-Path 'Manuals/Logs' 'project_info.json')
  )
  foreach($path in $cands){
    if(Test-Path $path){
      try{
        $j = Get-Content -Raw -LiteralPath $path | ConvertFrom-Json
        $name = $j.result.result.projectName
        if($name){ return ($name -replace '[\\/:*?"<>|]', '_') }
      }catch{}
    }
  }
  return ("Project_{0}" -f $Port)
}

function Ensure-Directory {
  param([string]$Path)
  $dir = Split-Path -Parent $Path
  if (-not $dir) { return }
  if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
}

function Write-CsvSafe {
  param([object[]]$Rows, [string]$Path)
  Ensure-Directory -Path $Path
  $Rows | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath $Path
}

function Try-WriteExcelOneSheet {
  param(
    [object[]]$Rows,
    [string]$OutPath
  )
  try { $excel = New-Object -ComObject Excel.Application } catch { return $false }
  try {
    $excel.Visible = $false
    $wb = $excel.Workbooks.Add()
    $ws = $wb.Worksheets.Item(1)
    $ws.Name = 'TypeParameters'

    $headers = 'category','typeId','typeName','familyName','paramId','paramName','storageType','isReadOnly','dataType','unit','value','display'
    for ($c=0; $c -lt $headers.Count; $c++){ $ws.Cells.Item(1,$c+1).Value2 = $headers[$c] }
    $r=2
    foreach ($row in $Rows) {
      $ws.Cells.Item($r,1).Value2 = $row.category
      $ws.Cells.Item($r,2).Value2 = $row.typeId
      $ws.Cells.Item($r,3).Value2 = $row.typeName
      $ws.Cells.Item($r,4).Value2 = $row.familyName
      $ws.Cells.Item($r,5).Value2 = $row.paramId
      $ws.Cells.Item($r,6).Value2 = $row.paramName
      $ws.Cells.Item($r,7).Value2 = $row.storageType
      $ws.Cells.Item($r,8).Value2 = [string]$row.isReadOnly
      $ws.Cells.Item($r,9).Value2 = $row.dataType
      $ws.Cells.Item($r,10).Value2 = $row.unit
      $ws.Cells.Item($r,11).Value2 = if($null -ne $row.value){ [string]$row.value } else { $null }
      $ws.Cells.Item($r,12).Value2 = $row.display
      $r++
    }
    $ws.UsedRange.EntireColumn.AutoFit() | Out-Null

    Ensure-Directory -Path $OutPath
    $wb.SaveAs($OutPath, 51)
    $wb.Close($true); $excel.Quit()
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($ws)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($wb)
    [void][Runtime.InteropServices.Marshal]::ReleaseComObject($excel)
    return $true
  } catch {
    try { if ($wb) { $wb.Close($false) } } catch {}
    try { if ($excel) { $excel.Quit() } } catch {}
    return $false
  }
}

# Minimal 1-sheet XLSX fallback
function Get-ExcelColumnName { param([int]$Index) $name=''; $i=$Index; while($i -gt 0){ $i--; $name=[char](65+($i%26)) + $name; $i=[math]::Floor($i/26) } return $name }
function New-WorksheetXmlInlineStr {
  param([string[]]$Headers,[object[]]$Rows)
  $esc = [System.Security.SecurityElement]::Escape
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
  [void]$sb.AppendLine('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">')
  $rowCount = ($Rows.Count + 1); $colCount = $Headers.Count; $lastCol = Get-ExcelColumnName -Index $colCount
  [void]$sb.AppendLine(('  <dimension ref="A1:{0}{1}"/>' -f $lastCol, $rowCount))
  [void]$sb.AppendLine('  <sheetData>')
  [void]$sb.Append('    <row r="1">')
  for ($c=1; $c -le $colCount; $c++) {
    $addr = (Get-ExcelColumnName -Index $c) + '1'
    $val = $esc.Invoke([string]$Headers[$c-1])
    [void]$sb.Append(('<c r="{0}" t="inlineStr"><is><t>{1}</t></is></c>' -f $addr, $val))
  }
  [void]$sb.AppendLine('</row>')
  $rIndex = 2
  foreach ($row in $Rows) {
    [void]$sb.Append(('    <row r="{0}">' -f $rIndex))
    for ($c=1; $c -le $colCount; $c++) {
      $h = $Headers[$c-1]; $v = $null; if ($row.PSObject.Properties[$h]) { $v = $row.$h }
      $addr = (Get-ExcelColumnName -Index $c) + $rIndex
      if ($null -ne $v -and $v -ne '') {
        $val = $esc.Invoke([string]$v)
        [void]$sb.Append(('<c r="{0}" t="inlineStr"><is><t>{1}</t></is></c>' -f $addr, $val))
      } else {
        [void]$sb.Append(('<c r="{0}"/>' -f $addr))
      }
    }
    [void]$sb.AppendLine('</row>'); $rIndex++
  }
  [void]$sb.AppendLine('  </sheetData>'); [void]$sb.AppendLine('</worksheet>')
  return $sb.ToString()
}
function Write-Xlsx1SheetZip {
  param([object[]]$Rows,[string]$OutPath)
  Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
  $headers = 'category','typeId','typeName','familyName','paramId','paramName','storageType','isReadOnly','dataType','unit','value','display'
  $sheetXml = New-WorksheetXmlInlineStr -Headers $headers -Rows $Rows
  $contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
  <Default Extension="xml" ContentType="application/xml"/>
  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
  <Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/>
  <Override PartName="/docProps/app.xml" ContentType="application/vnd.openxmlformats-officedocument.extended-properties+xml"/>
</Types>
'@
  $relsRoot = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/>
  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties" Target="docProps/app.xml"/>
</Relationships>
'@
  $workbook = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <sheets>
    <sheet name="TypeParameters" sheetId="1" r:id="rId1"/>
  </sheets>
</workbook>
'@
  $workbookRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
</Relationships>
'@
  $now = [DateTime]::UtcNow.ToString('s') + 'Z'
  $coreProps = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:dcmitype="http://purl.org/dc/dcmitype/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
  <dc:title>Door/Window Types Export</dc:title>
  <dc:creator>Codex</dc:creator>
  <cp:lastModifiedBy>Codex</cp:lastModifiedBy>
  <dcterms:created xsi:type="dcterms:W3CDTF">$now</dcterms:created>
  <dcterms:modified xsi:type="dcterms:W3CDTF">$now</dcterms:modified>
</cp:coreProperties>
"@
  $appProps = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Properties xmlns="http://schemas.openxmlformats.org/officeDocument/2006/extended-properties" xmlns:vt="http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes">
  <Application>Microsoft Excel</Application>
  <DocSecurity>0</DocSecurity>
  <ScaleCrop>false</ScaleCrop>
  <HeadingPairs>
    <vt:vector size="2" baseType="variant">
      <vt:variant><vt:lpstr>Worksheets</vt:lpstr></vt:variant>
      <vt:variant><vt:i4>1</vt:i4></vt:variant>
    </vt:vector>
  </HeadingPairs>
  <TitlesOfParts>
    <vt:vector size="1" baseType="lpstr">
      <vt:lpstr>TypeParameters</vt:lpstr>
    </vt:vector>
  </TitlesOfParts>
  <Company></Company>
  <LinksUpToDate>false</LinksUpToDate>
  <SharedDoc>false</SharedDoc>
  <HyperlinksChanged>false</HyperlinksChanged>
  <AppVersion>16.0300</AppVersion>
  </Properties>
'@
  Ensure-Directory -Path $OutPath
  if (Test-Path $OutPath) { Remove-Item -LiteralPath $OutPath -Force }
  $fs = [System.IO.File]::Open($OutPath, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite)
  try {
    $zip = New-Object System.IO.Compression.ZipArchive($fs, [System.IO.Compression.ZipArchiveMode]::Create, $true)
    function Add-Entry([System.IO.Compression.ZipArchive]$zip,[string]$path,[string]$text){ $entry=$zip.CreateEntry($path); $enc=New-Object System.Text.UTF8Encoding($false); $bytes=$enc.GetBytes($text); $es=$entry.Open(); try { $es.Write($bytes,0,$bytes.Length) } finally { $es.Dispose() } }
    Add-Entry $zip '[Content_Types].xml' $contentTypes
    Add-Entry $zip '_rels/.rels' $relsRoot
    Add-Entry $zip 'docProps/core.xml' $coreProps
    Add-Entry $zip 'docProps/app.xml' $appProps
    Add-Entry $zip 'xl/workbook.xml' $workbook
    Add-Entry $zip 'xl/_rels/workbook.xml.rels' $workbookRels
    Add-Entry $zip 'xl/worksheets/sheet1.xml' $sheetXml
  } finally { if ($zip) { $zip.Dispose() }; $fs.Close(); $fs.Dispose() }
  return $true
}

# Output paths
$ts = (Get-Date).ToString('yyyyMMdd_HHmmss')
$projName = Get-ProjectNameSafe -Port $Port
$baseDir = Join-Path 'Work' ("{0}_{1}" -f $projName, $Port)
if (-not (Test-Path $baseDir)) { New-Item -ItemType Directory -Path $baseDir -Force | Out-Null }
if (-not $OutPath) { $OutPath = Join-Path $baseDir ("DoorWindowTypes_{0}.xlsx" -f $ts) }
$csvParams = [System.IO.Path]::ChangeExtension($OutPath, $null) + "_TypeParameters.csv"

# Build rows
$rows = New-Object System.Collections.Generic.List[object]
if ($ReuseCsv) {
  Write-Host "[1/3] Loading from latest CSV in folder..." -ForegroundColor Cyan
  $latestParams = Get-ChildItem -Path $baseDir -Filter 'DoorWindowTypes_*_TypeParameters.csv' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
  if (-not $latestParams) { throw "No existing DoorWindowTypes CSV found in $baseDir. Omit -ReuseCsv to fetch from Revit." }
  (Import-Csv -LiteralPath $latestParams.FullName) | ForEach-Object { $rows.Add($_) } | Out-Null
} else {
  Write-Host "[1/3] Fetching door/window types..." -ForegroundColor Cyan
  $d = Invoke-RevitCommandJson -Method 'get_door_types' -Params @{} -Port $Port
  $w = Invoke-RevitCommandJson -Method 'get_window_types' -Params @{} -Port $Port
  $doorTypes = @(); if ($d.types) { $doorTypes = @($d.types) } elseif ($d.doorTypes) { $doorTypes = @($d.doorTypes) }
  $windowTypes = @(); if ($w.types) { $windowTypes = @($w.types) } elseif ($w.windowTypes) { $windowTypes = @($w.windowTypes) }
  Write-Host ("Door types: {0}, Window types: {1}" -f $doorTypes.Count, $windowTypes.Count)

  Write-Host "[2/3] Fetching parameters..." -ForegroundColor Cyan
  foreach ($t in $doorTypes) {
    $tid = [int]$t.typeId; $tname=[string]$t.typeName; $fname=[string]$t.familyName
    $pr = Invoke-RevitCommandJson -Method 'get_door_type_parameters' -Params @{ typeId = $tid } -Port $Port
    if ($pr -and $pr.ok -and $pr.parameters) {
      foreach ($p in $pr.parameters) {
        $rows.Add([pscustomobject]@{
          category   = 'Door'
          typeId     = $tid
          typeName   = $tname
          familyName = $fname
          paramId    = $p.id
          paramName  = $p.name
          storageType= $p.storageType
          isReadOnly = $p.isReadOnly
          dataType   = $p.dataType
          unit       = $p.unit
          value      = $p.value
          display    = $p.display
        })
      }
    }
  }
  foreach ($t in $windowTypes) {
    $tid = [int]$t.typeId; $tname=[string]$t.typeName; $fname=[string]$t.familyName
    $pr = Invoke-RevitCommandJson -Method 'get_window_type_parameters' -Params @{ typeId = $tid } -Port $Port
    if ($pr -and $pr.ok -and $pr.parameters) {
      foreach ($p in $pr.parameters) {
        $rows.Add([pscustomobject]@{
          category   = 'Window'
          typeId     = $tid
          typeName   = $tname
          familyName = $fname
          paramId    = $p.id
          paramName  = $p.name
          storageType= $p.storageType
          isReadOnly = $p.isReadOnly
          dataType   = $p.dataType
          unit       = $p.unit
          value      = $p.value
          display    = $p.display
        })
      }
    }
  }
}

if (-not $ReuseCsv) {
  Write-Host "[2/3] Writing CSV export..." -ForegroundColor Cyan
  Write-CsvSafe -Rows $rows -Path $csvParams
} else {
  Write-Host "[2/3] Reusing CSV input; skip writing." -ForegroundColor Cyan
}

if (-not $CsvFallbackOnly) {
  Write-Host ("[3/3] Building Excel workbook (1 sheet) → {0}" -f $OutPath) -ForegroundColor Cyan
  $ok = Try-WriteExcelOneSheet -Rows $rows -OutPath $OutPath
  if (-not $ok) {
    Write-Warning "Excel COM automation unavailable. Falling back to pure .xlsx writer."
    $ok = Write-Xlsx1SheetZip -Rows $rows -OutPath $OutPath
  }
  if ($ok) { Write-Host ("[Done] Excel saved: {0}" -f $OutPath) }
  else { Write-Warning "Could not create .xlsx. Kept CSV only."; Write-Host ("CSV: {0}" -f $csvParams) }
} else {
  Write-Host "[Done] CSV only as requested."; Write-Host ("CSV: {0}" -f $csvParams)
}



