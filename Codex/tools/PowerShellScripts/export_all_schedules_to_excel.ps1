# @feature: export all schedules to excel | keywords: 柱, ビュー, 集計表, Excel, 床
param(
  [int]$Port = 5210,
  [string]$OutDir,
  [switch]$CsvAlso
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
      "Manuals/Scripts/send_revit_command_durable.py",
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
  if (-not (Test-Path $Path)) { New-Item -ItemType Directory -Path $Path -Force | Out-Null }
}

function Sanitize-FileName {
  param([string]$Name)
  if (-not $Name) { return 'Untitled' }
  return ($Name -replace '[\\/:*?"<>|]', '_').Trim()
}

function Get-ExcelColumnName { param([int]$Index) $name=''; $i=$Index; while($i -gt 0){ $i--; $name=[char](65+($i%26)) + $name; $i=[math]::Floor($i/26) } return $name }

function New-WorksheetXmlFromRows {
  param([object[]]$Rows)
  $headers = New-Object System.Collections.Generic.List[string]
  $seen = New-Object 'System.Collections.Generic.HashSet[string]'
  foreach ($row in $Rows) {
    foreach ($p in $row.PSObject.Properties) {
      $n = [string]$p.Name
      if (-not $seen.Contains($n)) { $seen.Add($n) | Out-Null; [void]$headers.Add($n) }
    }
  }
  $esc = [System.Security.SecurityElement]::Escape
  $sb = New-Object System.Text.StringBuilder
  [void]$sb.AppendLine('<?xml version="1.0" encoding="UTF-8" standalone="yes"?>')
  [void]$sb.AppendLine('<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">')
  $rowCount = ($Rows.Count + 1); $colCount = $headers.Count; $lastCol = Get-ExcelColumnName -Index $colCount
  [void]$sb.AppendLine(('  <dimension ref="A1:{0}{1}"/>' -f $lastCol, $rowCount))
  [void]$sb.AppendLine('  <sheetData>')
  # Header row
  [void]$sb.Append('    <row r="1">')
  for ($c=1; $c -le $colCount; $c++) {
    $addr = (Get-ExcelColumnName -Index $c) + '1'
    $val = $esc.Invoke([string]$headers[$c-1])
    [void]$sb.Append(('<c r="{0}" t="inlineStr"><is><t>{1}</t></is></c>' -f $addr, $val))
  }
  [void]$sb.AppendLine('</row>')
  # Data rows
  $rIndex = 2
  foreach ($row in $Rows) {
    [void]$sb.Append(('    <row r="{0}">' -f $rIndex))
    for ($c=1; $c -le $colCount; $c++) {
      $h = $headers[$c-1]; $v = $null; if ($row.PSObject.Properties[$h]) { $v = $row.$h }
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
  return @{ headers = $headers; xml = $sb.ToString() }
}

function Try-WriteExcelOneSheet {
  param([object[]]$Rows, [string]$OutPath)
  try { $excel = New-Object -ComObject Excel.Application } catch { return $false }
  try {
    $excel.Visible = $false
    $wb = $excel.Workbooks.Add()
    $ws = $wb.Worksheets.Item(1); $ws.Name = 'Schedule'
    # Headers from union of properties
    $headers = New-Object System.Collections.Generic.List[string]
    $seen = New-Object 'System.Collections.Generic.HashSet[string]'
    foreach ($row in $Rows) { foreach ($p in $row.PSObject.Properties) { $n=[string]$p.Name; if(-not $seen.Contains($n)){ $seen.Add($n)|Out-Null; [void]$headers.Add($n) } } }
    for ($c=0; $c -lt $headers.Count; $c++){ $ws.Cells.Item(1,$c+1).Value2 = $headers[$c] }
    $r=2
    foreach ($row in $Rows) {
      for ($c=0; $c -lt $headers.Count; $c++) {
        $h = $headers[$c]
        $v = $null; if ($row.PSObject.Properties[$h]) { $v = $row.$h }
        $ws.Cells.Item($r, $c+1).Value2 = if($null -ne $v){ [string]$v } else { $null }
      }
      $r++
    }
    $ws.UsedRange.EntireColumn.AutoFit() | Out-Null
    Ensure-Directory -Path (Split-Path -Parent $OutPath)
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

function Write-Xlsx1SheetZip {
  param([object[]]$Rows, [string]$OutPath)
  Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null
  $sheet = New-WorksheetXmlFromRows -Rows $Rows
  $sheetXml = $sheet.xml
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
    <sheet name="Schedule" sheetId="1" r:id="rId1"/>
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
  <dc:title>Schedule Export</dc:title>
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
      <vt:lpstr>Schedule</vt:lpstr>
    </vt:vector>
  </TitlesOfParts>
  <Company></Company>
  <LinksUpToDate>false</LinksUpToDate>
  <SharedDoc>false</SharedDoc>
  <HyperlinksChanged>false</HyperlinksChanged>
  <AppVersion>16.0300</AppVersion>
  </Properties>
'@
  Ensure-Directory -Path (Split-Path -Parent $OutPath)
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

# Main entry (guarded to avoid executing when dot-sourced)
if ($MyInvocation.InvocationName -ne '.') {
  # Determine output directory
  $proj = Get-ProjectNameSafe -Port $Port
  if (-not $OutDir) { $OutDir = Join-Path 'Work' ("{0}_{1}" -f $proj, $Port) }
  Ensure-Directory -Path $OutDir

  # Fetch all schedules
  Write-Host "[1/2] Fetching schedules..." -ForegroundColor Cyan
  $sched = Invoke-RevitCommandJson -Method 'get_schedules' -Params @{} -Port $Port
  if (-not $sched.ok -or -not $sched.schedules) { throw "No schedules returned." }
  $list = @($sched.schedules)
  Write-Host ("Found {0} schedules." -f $list.Count)

  # Export each schedule
  Write-Host "[2/2] Exporting schedules to Excel..." -ForegroundColor Cyan
  foreach ($s in $list) {
    $title = [string]$s.title
    $sid = [int]$s.scheduleViewId
    $safeName = Sanitize-FileName ("集計表_" + $title)
    $xlsx = Join-Path $OutDir ("{0}.xlsx" -f $safeName)
    Write-Host ("- Exporting: {0} (id={1})" -f $title, $sid)
    $data = Invoke-RevitCommandJson -Method 'get_schedule_data' -Params @{ scheduleViewId = $sid } -Port $Port
    $rows = @()
    if ($data -and $data.ok -and $data.rows) { $rows = @($data.rows) }
    if (-not $rows -or $rows.Count -eq 0) {
      Write-Warning ("Schedule '{0}' returned no rows; creating empty sheet." -f $title)
      $rows = @([pscustomobject]@{ Note = 'No rows' })
    }
    if ($CsvAlso) {
      $csv = Join-Path $OutDir ("{0}.csv" -f $safeName)
      $rows | Export-Csv -NoTypeInformation -Encoding UTF8 -LiteralPath $csv
    }
    $ok = Try-WriteExcelOneSheet -Rows $rows -OutPath $xlsx
    if (-not $ok) {
      Write-Warning "Excel COM unavailable; using pure .xlsx writer."
      $ok = Write-Xlsx1SheetZip -Rows $rows -OutPath $xlsx
    }
    if ($ok) { Write-Host ("  -> Saved: {0}" -f $xlsx) } else { Write-Warning ("Failed to write: {0}" -f $xlsx) }
  }
}
