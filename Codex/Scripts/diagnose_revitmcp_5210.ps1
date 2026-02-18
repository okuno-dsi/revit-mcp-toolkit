param(
  [string]$ServerPath = '',
  [string]$TracePath = '',
  [switch]$TryLaunchServer,
  [switch]$OpenOutputFolder,
  [int]$TimeoutSec = 8
)

$ErrorActionPreference = 'Stop'
$script:DiagTracePath = $TracePath

# Fixed operation mode for non-technical users:
# - Port: 5210
# - Mode: Full
# - Output: same folder as this script
$Port = 5210
$Mode = 'Full'
$OutputDir = $PSScriptRoot

function Write-DiagStep([string]$Message) {
  $line = '[Step] ' + $Message
  Write-Host $line -ForegroundColor DarkGray
  if (-not [string]::IsNullOrWhiteSpace($script:DiagTracePath)) {
    try { Add-Content -LiteralPath $script:DiagTracePath -Value $line -Encoding UTF8 } catch {}
  }
}

function Get-IsoNow {
  return (Get-Date).ToUniversalTime().ToString('o')
}

function Mask-Path([string]$PathText) {
  if ([string]::IsNullOrWhiteSpace($PathText)) { return $PathText }
  $masked = $PathText
  if ($env:USERPROFILE) {
    $masked = $masked -replace [regex]::Escape($env:USERPROFILE), 'C:\Users\<user>'
  }
  return $masked
}

function Read-TextSafe([string]$PathText) {
  try {
    if (-not (Test-Path -LiteralPath $PathText)) { return $null }
    return Get-Content -LiteralPath $PathText -Raw -ErrorAction Stop
  } catch {
    return $null
  }
}

function Get-JsonStringValues([object]$Obj) {
  $vals = New-Object System.Collections.Generic.List[string]
  function _walk([object]$node, [System.Collections.Generic.List[string]]$sink) {
    if ($null -eq $node) { return }
    if ($node -is [string]) {
      if (-not [string]::IsNullOrWhiteSpace($node)) { $sink.Add($node) }
      return
    }
    if ($node -is [System.Collections.IDictionary]) {
      foreach ($k in $node.Keys) { _walk $node[$k] $sink }
      return
    }
    if (($node -is [System.Collections.IEnumerable]) -and -not ($node -is [string])) {
      foreach ($item in $node) { _walk $item $sink }
      return
    }
    foreach ($p in $node.PSObject.Properties) { _walk $p.Value $sink }
  }
  _walk $Obj $vals
  return $vals.ToArray()
}

function Resolve-ServerPath([string]$ExplicitPath) {
  $result = [ordered]@{
    selected = $null
    selectedExists = $false
    selectedBy = $null
    candidates = @()
    notes = @()
    sources = @()
  }

  $candidates = New-Object System.Collections.Generic.List[string]
  if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
    $candidates.Add($ExplicitPath)
    $result.sources += 'param.ServerPath'
  }

  if ($env:REVIT_MCP_SERVER_EXE) {
    $candidates.Add($env:REVIT_MCP_SERVER_EXE)
    $result.sources += 'env.REVIT_MCP_SERVER_EXE'
  }

  $addinRoots = @(
    (Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024\RevitMCPAddin'),
    (Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2024\RevitMCPAddin')
  )
  foreach ($r in $addinRoots) {
    $candidates.Add((Join-Path $r 'server\RevitMCPServer.exe'))
  }
  $result.sources += 'installed.addinRoots'

  $jsonHints = @(
    (Join-Path $env:USERPROFILE 'Documents\Revit_MCP\Settings\paths.json'),
    (Join-Path $env:USERPROFILE 'Documents\Revit_MCP\paths.json'),
    (Join-Path $env:LOCALAPPDATA 'RevitMCP\paths.json'),
    (Join-Path $env:APPDATA 'RevitMCP\paths.json')
  )
  foreach ($jp in $jsonHints) {
    if (-not (Test-Path -LiteralPath $jp)) { continue }
    try {
      $obj = (Get-Content -LiteralPath $jp -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop)
      $strings = Get-JsonStringValues $obj
      foreach ($s in $strings) {
        if ($s -match 'RevitMCPServer\.exe$') {
          $candidates.Add($s)
        } elseif ($s -match 'RevitMCPAddin\\server$') {
          $candidates.Add((Join-Path $s 'RevitMCPServer.exe'))
        }
      }
      $result.sources += ("pathsJson:" + (Mask-Path $jp))
    } catch {
      $result.notes += ("paths.json parse failed: " + (Mask-Path $jp))
    }
  }

  $dedup = New-Object System.Collections.Generic.List[string]
  $seen = @{}
  foreach ($c in $candidates) {
    if ([string]::IsNullOrWhiteSpace($c)) { continue }
    $n = $c.Trim()
    if ($seen.ContainsKey($n)) { continue }
    $seen[$n] = $true
    $dedup.Add($n)
  }

  foreach ($c in $dedup) {
    $result.candidates += [ordered]@{
      path = $c
      pathMasked = (Mask-Path $c)
      exists = (Test-Path -LiteralPath $c)
    }
  }

  foreach ($item in $result.candidates) {
    if ($item.exists) {
      $result.selected = $item.path
      $result.selectedExists = $true
      $result.selectedBy = 'firstExistingCandidate'
      break
    }
  }

  if (-not $result.selected -and $result.candidates.Count -gt 0) {
    $result.selected = $result.candidates[0].path
    $result.selectedExists = $false
    $result.selectedBy = 'firstCandidateNotFound'
  }

  return $result
}

function Get-FileIntegrity([string]$ExePath) {
  $check = [ordered]@{
    ok = $false
    exists = $false
    path = $ExePath
    pathMasked = (Mask-Path $ExePath)
    sizeBytes = 0
    sizeMB = 0.0
    lastWriteTimeUtc = $null
    isReadOnly = $false
    canRead = $false
    runtimeConfigExists = $false
    appSettingsFiles = @()
    zoneIdentifier = $null
    errors = @()
  }

  if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $check.errors += 'Server executable path is empty.'
    return $check
  }

  if (-not (Test-Path -LiteralPath $ExePath)) {
    $check.errors += 'Server executable not found.'
    return $check
  }

  $check.exists = $true

  try {
    $fi = Get-Item -LiteralPath $ExePath -ErrorAction Stop
    $check.sizeBytes = [int64]$fi.Length
    $check.sizeMB = [math]::Round($fi.Length / 1MB, 2)
    $check.lastWriteTimeUtc = $fi.LastWriteTimeUtc.ToString('o')
    $check.isReadOnly = ($fi.Attributes -band [System.IO.FileAttributes]::ReadOnly) -ne 0
  } catch {
    $check.errors += ('Get-Item failed: ' + $_.Exception.Message)
  }

  try {
    $fs = [System.IO.File]::Open($ExePath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
    $fs.Close()
    $check.canRead = $true
  } catch {
    $check.canRead = $false
    $check.errors += ('Read test failed: ' + $_.Exception.Message)
  }

  try {
    $stream = Get-Content -LiteralPath $ExePath -Stream Zone.Identifier -ErrorAction Stop
    $check.zoneIdentifier = ($stream -join "`n")
  } catch {
  }

  try {
    $dir = Split-Path -Parent $ExePath
    $stem = [System.IO.Path]::GetFileNameWithoutExtension($ExePath)
    $runtimeCfg = Join-Path $dir ($stem + '.runtimeconfig.json')
    $check.runtimeConfigExists = Test-Path -LiteralPath $runtimeCfg
    $check.appSettingsFiles = @(Get-ChildItem -LiteralPath $dir -Filter 'appsettings*.json' -File -ErrorAction SilentlyContinue | ForEach-Object { Mask-Path $_.FullName })
  } catch {
    $check.errors += ('Sidecar check failed: ' + $_.Exception.Message)
  }

  $check.ok = $check.exists -and ($check.sizeBytes -gt 0) -and $check.canRead
  return $check
}

function Get-ProcessSnapshot {
  $rows = @(Get-Process -Name 'RevitMCPServer' -ErrorAction SilentlyContinue)
  $items = @()
  foreach ($p in $rows) {
    $items += [ordered]@{
      pid = $p.Id
      name = $p.ProcessName
      startTime = $(try { $p.StartTime.ToUniversalTime().ToString('o') } catch { $null })
      path = $(try { $p.Path } catch { $null })
      pathMasked = $(try { Mask-Path $p.Path } catch { $null })
    }
  }
  return [ordered]@{
    running = ($items.Count -gt 0)
    count = $items.Count
    items = $items
  }
}

function Get-PortListeners([int]$TargetPort) {
  $items = @()

  try {
    if (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue) {
      $rows = @(Get-NetTCPConnection -State Listen -LocalPort $TargetPort -ErrorAction Stop)
      foreach ($r in $rows) {
        $items += [ordered]@{
          localAddress = $r.LocalAddress
          localPort = [int]$r.LocalPort
          pid = [int]$r.OwningProcess
          source = 'Get-NetTCPConnection'
        }
      }
    }
  } catch {
  }

  if ($items.Count -eq 0) {
    try {
      $lines = netstat -ano -p tcp | Select-String -Pattern 'LISTENING' | ForEach-Object { $_.Line }
      foreach ($line in $lines) {
        $parts = ($line -replace '\s+', ' ').Trim().Split(' ')
        if ($parts.Count -lt 5) { continue }
        $local = $parts[1]
        $state = $parts[3]
        $pidText = $parts[4]
        if ($state -ne 'LISTENING') { continue }
        if ($local -notmatch ':(\d+)$') { continue }
        $localPort = [int]$Matches[1]
        if ($localPort -ne $TargetPort) { continue }
        $addr = $local.Substring(0, $local.LastIndexOf(':'))
        $pid = 0
        [void][int]::TryParse($pidText, [ref]$pid)
        $items += [ordered]@{
          localAddress = $addr
          localPort = $localPort
          pid = $pid
          source = 'netstat'
        }
      }
    } catch {
    }
  }

  $seen = @{}
  $uniq = @()
  foreach ($i in $items) {
    $k = '{0}|{1}|{2}' -f $i.pid, $i.localAddress, $i.localPort
    if ($seen.ContainsKey($k)) { continue }
    $seen[$k] = $true
    $uniq += $i
  }

  return $uniq
}

function Test-TcpPort([string]$TargetHost, [int]$TargetPort, [int]$TimeoutSeconds) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  $client = New-Object System.Net.Sockets.TcpClient
  try {
    $ar = $client.BeginConnect($TargetHost, $TargetPort, $null, $null)
    $ok = $ar.AsyncWaitHandle.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
    if (-not $ok) {
      return [ordered]@{
        ok = $false
        host = $TargetHost
        port = $TargetPort
        elapsedMs = [int]$sw.ElapsedMilliseconds
        error = 'timeout'
      }
    }
    $client.EndConnect($ar)
    return [ordered]@{
      ok = $true
      host = $TargetHost
      port = $TargetPort
      elapsedMs = [int]$sw.ElapsedMilliseconds
      error = $null
    }
  } catch {
    return [ordered]@{
      ok = $false
      host = $TargetHost
      port = $TargetPort
      elapsedMs = [int]$sw.ElapsedMilliseconds
      error = $_.Exception.Message
    }
  } finally {
    try { $client.Close() } catch {}
  }
}

function Invoke-HttpProbe([string]$Url, [string]$Method, [string]$Body, [int]$TimeoutSeconds) {
  $sw = [System.Diagnostics.Stopwatch]::StartNew()
  try {
    $req = [System.Net.HttpWebRequest]::Create($Url)
    $req.Method = $Method
    $req.Timeout = $TimeoutSeconds * 1000
    $req.ReadWriteTimeout = $TimeoutSeconds * 1000
    $req.UserAgent = 'RevitMCPDiag/1.0'
    $req.ContentType = 'application/json'
    $req.Proxy = $null
    $req.KeepAlive = $false
    $req.ServicePoint.Expect100Continue = $false

    if (-not [string]::IsNullOrEmpty($Body)) {
      $bytes = [System.Text.Encoding]::UTF8.GetBytes($Body)
      $req.ContentLength = $bytes.Length
      $stream = $req.GetRequestStream()
      $stream.Write($bytes, 0, $bytes.Length)
      $stream.Close()
    }

    $resp = $req.GetResponse()
    $statusCode = [int]$resp.StatusCode
    $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
    $text = $reader.ReadToEnd()
    $reader.Close()
    $resp.Close()

    $snippet = $text
    if ($snippet.Length -gt 500) { $snippet = $snippet.Substring(0, 500) }

    return [ordered]@{
      ok = $true
      url = $Url
      method = $Method
      statusCode = $statusCode
      elapsedMs = [int]$sw.ElapsedMilliseconds
      error = $null
      body = $text
      bodySnippet = $snippet
    }
  } catch [System.Net.WebException] {
    $statusCode = $null
    $body = ''
    $snippet = ''
    try {
      if ($_.Exception.Response) {
        $statusCode = [int]$_.Exception.Response.StatusCode
        $reader = New-Object System.IO.StreamReader($_.Exception.Response.GetResponseStream())
        $body = $reader.ReadToEnd()
        $reader.Close()
        $_.Exception.Response.Close()
      }
    } catch {
    }
    $snippet = $body
    if ($snippet.Length -gt 500) { $snippet = $snippet.Substring(0, 500) }

    return [ordered]@{
      ok = $false
      url = $Url
      method = $Method
      statusCode = $statusCode
      elapsedMs = [int]$sw.ElapsedMilliseconds
      error = $_.Exception.Message
      body = $body
      bodySnippet = $snippet
    }
  } catch {
    return [ordered]@{
      ok = $false
      url = $Url
      method = $Method
      statusCode = $null
      elapsedMs = [int]$sw.ElapsedMilliseconds
      error = $_.Exception.Message
      body = ''
      bodySnippet = ''
    }
  }
}

function Try-ParseJson([string]$Text) {
  if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
  try { return ($Text | ConvertFrom-Json -ErrorAction Stop) } catch { return $null }
}

function Get-RpcResponseSummary([object]$Obj) {
  if ($null -eq $Obj) { return $null }
  $summary = [ordered]@{
    hasResult = $false
    hasError = $false
    queued = $false
    jobId = $null
    code = $null
    msg = $null
  }
  try {
    if ($Obj.PSObject.Properties.Name -contains 'result') {
      $summary.hasResult = $true
      $r = $Obj.result
      if ($null -ne $r) {
        if ($r.PSObject.Properties.Name -contains 'queued') { $summary.queued = [bool]$r.queued }
        if ($r.PSObject.Properties.Name -contains 'jobId') { $summary.jobId = [string]$r.jobId }
        if ($r.PSObject.Properties.Name -contains 'code') { $summary.code = [string]$r.code }
        if ($r.PSObject.Properties.Name -contains 'msg') { $summary.msg = [string]$r.msg }
      }
    }
    if ($Obj.PSObject.Properties.Name -contains 'error') {
      $summary.hasError = $true
      $e = $Obj.error
      if ($null -ne $e) {
        if ($e.PSObject.Properties.Name -contains 'code') { $summary.code = [string]$e.code }
        if ($e.PSObject.Properties.Name -contains 'message') { $summary.msg = [string]$e.message }
      }
    }
  } catch {
  }
  return $summary
}

function Read-LogTail([string]$PathText, [int]$MaxLines = 120) {
  $ret = [ordered]@{
    exists = Test-Path -LiteralPath $PathText
    path = $PathText
    pathMasked = (Mask-Path $PathText)
    lines = @()
    errors = @()
  }
  if (-not $ret.exists) { return $ret }
  try {
    $rawLines = @(Get-Content -LiteralPath $PathText -Tail $MaxLines -ErrorAction Stop)
    # Normalize to plain strings to avoid serializing PowerShell ETS metadata.
    $ret.lines = @($rawLines | ForEach-Object { [string]$_ })
  } catch {
    $ret.errors += $_.Exception.Message
  }
  return $ret
}

function Get-FirewallSnapshot([int]$TargetPort, [string]$ServerExe, [bool]$DoFull) {
  $ret = [ordered]@{
    status = 'unknown'
    cmdletsAvailable = $false
    rulesByName = @()
    rulesByApp = @()
    rulesByPort = @()
    suspectedBlocked = $false
    errors = @()
  }

  if (-not (Get-Command Get-NetFirewallRule -ErrorAction SilentlyContinue)) {
    $ret.errors += 'Get-NetFirewallRule cmdlet not available.'
    return $ret
  }

  $ret.cmdletsAvailable = $true

  if (-not $DoFull) {
    $ret.status = 'skipped'
    return $ret
  }

  try {
    $nameRules = @(Get-NetFirewallRule -ErrorAction Stop | Where-Object { $_.DisplayName -match 'RevitMCP|Revit MCP|5210' } | Select-Object -First 50)
    foreach ($r in $nameRules) {
      $ret.rulesByName += [ordered]@{
        name = $r.Name
        displayName = $r.DisplayName
        direction = $r.Direction
        enabled = $r.Enabled
        action = $r.Action
      }
    }
  } catch {
    $ret.errors += ('rulesByName failed: ' + $_.Exception.Message)
  }

  if ($DoFull) {
    try {
      $allIn = @(Get-NetFirewallRule -Direction Inbound -Enabled True -ErrorAction Stop)
      foreach ($r in $allIn) {
        try {
          $pf = @(Get-NetFirewallPortFilter -AssociatedNetFirewallRule $r -ErrorAction Stop)
          foreach ($p in $pf) {
            if (($p.Protocol -eq 'TCP') -and ($p.LocalPort -eq "$TargetPort")) {
              $ret.rulesByPort += [ordered]@{
                name = $r.Name
                displayName = $r.DisplayName
                action = $r.Action
                enabled = $r.Enabled
                direction = $r.Direction
              }
            }
          }
        } catch {
        }
      }
    } catch {
      $ret.errors += ('rulesByPort failed: ' + $_.Exception.Message)
    }

    if (-not [string]::IsNullOrWhiteSpace($ServerExe)) {
      try {
        $allIn = @(Get-NetFirewallRule -Direction Inbound -Enabled True -ErrorAction Stop)
        foreach ($r in $allIn) {
          try {
            $af = @(Get-NetFirewallApplicationFilter -AssociatedNetFirewallRule $r -ErrorAction Stop)
            foreach ($a in $af) {
              if (-not [string]::IsNullOrWhiteSpace($a.Program) -and ($a.Program -ieq $ServerExe)) {
                $ret.rulesByApp += [ordered]@{
                  name = $r.Name
                  displayName = $r.DisplayName
                  action = $r.Action
                  enabled = $r.Enabled
                  direction = $r.Direction
                  program = (Mask-Path $a.Program)
                }
              }
            }
          } catch {
          }
        }
      } catch {
        $ret.errors += ('rulesByApp failed: ' + $_.Exception.Message)
      }
    }
  }

  $allowByPort = @($ret.rulesByPort | Where-Object { $_.action -eq 'Allow' }).Count
  $allowByApp = @($ret.rulesByApp | Where-Object { $_.action -eq 'Allow' }).Count
  if ($DoFull -and ($allowByPort -eq 0) -and ($allowByApp -eq 0)) {
    $ret.suspectedBlocked = $true
  }

  if ($ret.errors.Count -eq 0) { $ret.status = 'ok' } else { $ret.status = 'warn' }
  return $ret
}

function Get-SecurityEvents([bool]$Enabled) {
  $ret = [ordered]@{
    status = 'skipped'
    windowDays = 3
    channels = @()
    appControlBlockCount = 0
    errors = @()
  }
  if (-not $Enabled) { return $ret }

  $ret.status = 'ok'
  $startTime = (Get-Date).AddDays(-3)
  $logNames = @(
    'Microsoft-Windows-AppLocker/EXE and DLL',
    'Microsoft-Windows-CodeIntegrity/Operational',
    'Microsoft-Windows-Windows Defender/Operational'
  )
  $regex = 'RevitMCPServer|5210|block|blocked|deny|denied|AppLocker|Code Integrity|WDAC'

  foreach ($ln in $logNames) {
    $channelItem = [ordered]@{
      logName = $ln
      status = 'ok'
      matches = @()
      error = $null
    }
    try {
      $events = @(Get-WinEvent -FilterHashtable @{ LogName = $ln; StartTime = $startTime } -MaxEvents 250 -ErrorAction Stop)
      foreach ($ev in $events) {
        $msg = ''
        try { $msg = [string]$ev.Message } catch {}
        if ($msg -match $regex) {
          if ($channelItem.matches.Count -ge 40) { break }
          $short = $msg
          if ($short.Length -gt 280) { $short = $short.Substring(0, 280) }
          $channelItem.matches += [ordered]@{
            timeUtc = $ev.TimeCreated.ToUniversalTime().ToString('o')
            id = $ev.Id
            level = $ev.LevelDisplayName
            provider = $ev.ProviderName
            message = $short
          }
        }
      }
      $ret.appControlBlockCount += @($channelItem.matches).Count
    } catch {
      $channelItem.status = 'error'
      $channelItem.error = $_.Exception.Message
      $ret.errors += ("{0}: {1}" -f $ln, $_.Exception.Message)
      $ret.status = 'warn'
    }
    $ret.channels += $channelItem
  }

  return $ret
}

function Classify-Result([hashtable]$Report) {
  $cls = [ordered]@{
    code = 'UNKNOWN'
    confidence = 0.50
    reasons = @()
    nextActions = @()
  }

  $fileOk = [bool]$Report.checks.fileIntegrity.ok
  $procRunning = [bool]$Report.checks.process.running
  $listen = [bool]$Report.checks.port.listen
  $ownerIsServer = [bool]$Report.checks.port.ownerIsServer
  $tcpOk = [bool]$Report.checks.network.tcp127.ok
  $rootOk = [bool]$Report.checks.http.root.ok
  $rpcReach = [bool]$Report.checks.http.rpc.anyEndpointReachable
  $fwBlocked = [bool]$Report.checks.security.firewall.suspectedBlocked
  $appBlocked = ([int]$Report.checks.security.events.appControlBlockCount -gt 0)

  if (-not $fileOk) {
    $cls.code = 'SERVER_MISSING'
    $cls.confidence = 0.95
    $cls.reasons += 'Server executable missing or unreadable.'
  } elseif ($appBlocked) {
    $cls.code = 'APP_CONTROL_BLOCKED'
    $cls.confidence = 0.88
    $cls.reasons += 'Security event logs contain block-like events.'
  } elseif ($listen -and -not $ownerIsServer) {
    $cls.code = 'PORT_CONFLICT'
    $cls.confidence = 0.90
    $cls.reasons += 'Port is listening but owned by another process.'
  } elseif ((-not $procRunning) -and (-not $listen) -and (-not $tcpOk)) {
    $cls.code = 'SERVER_START_FAILED'
    $cls.confidence = 0.86
    $cls.reasons += 'No server process and no listener on target port.'
  } elseif ($procRunning -and (-not $listen)) {
    $cls.code = 'SERVER_START_FAILED'
    $cls.confidence = 0.78
    $cls.reasons += 'Server process exists but listener not observed.'
  } elseif ($listen -and $tcpOk -and (-not $rpcReach)) {
    $cls.code = 'RPC_ENDPOINT_INVALID'
    $cls.confidence = 0.74
    $cls.reasons += 'TCP and HTTP reachable, but RPC endpoints not healthy.'
  } elseif ($listen -and (-not $tcpOk)) {
    $cls.code = 'LOOPBACK_BLOCKED'
    $cls.confidence = 0.70
    $cls.reasons += 'Listener exists but local TCP connection failed.'
  } elseif ($fwBlocked) {
    $cls.code = 'FIREWALL_BLOCKED'
    $cls.confidence = 0.72
    $cls.reasons += 'No matching firewall allow rule found (Full mode heuristic).'
  } elseif ($rpcReach) {
    $cls.code = 'OK'
    $cls.confidence = 0.95
    $cls.reasons += 'RPC endpoint reachable.'
  } elseif ($rootOk) {
    $cls.code = 'PARTIAL_OK'
    $cls.confidence = 0.65
    $cls.reasons += 'HTTP root reachable but RPC not confirmed.'
  } else {
    $cls.code = 'UNKNOWN'
    $cls.confidence = 0.50
    $cls.reasons += 'Unable to determine a single root cause.'
  }

  switch ($cls.code) {
    'OK' {
      $cls.nextActions += 'No immediate action required.'
      $cls.nextActions += 'If users still fail, compare this JSON against failing PCs.'
    }
    'SERVER_MISSING' {
      $cls.nextActions += 'Reinstall RevitMCP add-in payload (server folder).'
      $cls.nextActions += 'Verify RevitMCPServer.exe and sidecar files exist in addin server directory.'
    }
    'SERVER_START_FAILED' {
      $cls.nextActions += 'Run this tool with -TryLaunchServer and inspect launchErrors.'
      $cls.nextActions += 'Check addin log tail for startup exceptions.'
    }
    'PORT_CONFLICT' {
      $cls.nextActions += 'Stop conflicting process on target port or change RevitMCP port.'
      $cls.nextActions += 'Re-run diagnostics and confirm owner process is RevitMCPServer.'
    }
    'LOOPBACK_BLOCKED' {
      $cls.nextActions += 'Check endpoint security policy affecting localhost/loopback traffic.'
      $cls.nextActions += 'Try temporary exception for RevitMCPServer.exe.'
    }
    'FIREWALL_BLOCKED' {
      $cls.nextActions += 'Add inbound allow rule for RevitMCPServer.exe and TCP ' + $Port + '.'
      $cls.nextActions += 'Re-run with -Mode Full to confirm firewall rule visibility.'
    }
    'APP_CONTROL_BLOCKED' {
      $cls.nextActions += 'Review AppLocker/WDAC policy for server executable path and signature.'
      $cls.nextActions += 'Use security event IDs in this report for SOC/admin escalation.'
    }
    'RPC_ENDPOINT_INVALID' {
      $cls.nextActions += 'Verify server build compatibility and endpoint route (/rpc or /jsonrpc).'
      $cls.nextActions += 'Check server logs for route/serialization exceptions.'
    }
    default {
      $cls.nextActions += 'Run with -Mode Full -IncludeEvents and compare with a working PC report.'
      $cls.nextActions += 'Collect addin/server logs and escalate with JSON report.'
    }
  }

  return $cls
}

function Build-Markdown([hashtable]$Report) {
  $lines = New-Object System.Collections.Generic.List[string]
  $cls = $Report.classification
  $lines.Add('# RevitMCP Connectivity Diagnostics Report')
  $lines.Add('')
  $lines.Add('- Timestamp (UTC): ' + $Report.timestamp)
  $lines.Add('- Port: ' + $Report.inputs.port)
  $lines.Add('- Mode: ' + $Report.inputs.mode)
  $lines.Add('- Host: ' + $Report.host.computerName + ' / ' + $Report.host.osVersion)
  $lines.Add('')
  $lines.Add('## Summary')
  $lines.Add('')
  $lines.Add('| Item | Value |')
  $lines.Add('|---|---|')
  $lines.Add('| Classification | ' + $cls.code + ' |')
  $lines.Add('| Confidence | ' + $cls.confidence + ' |')
  $lines.Add('| Server File OK | ' + $Report.checks.fileIntegrity.ok + ' |')
  $lines.Add('| Process Running | ' + $Report.checks.process.running + ' |')
  $lines.Add('| Port Listening | ' + $Report.checks.port.listen + ' |')
  $lines.Add('| TCP 127.0.0.1 | ' + $Report.checks.network.tcp127.ok + ' |')
  $lines.Add('| HTTP Root | ' + $Report.checks.http.root.ok + ' |')
  $lines.Add('| RPC Reachable | ' + $Report.checks.http.rpc.anyEndpointReachable + ' |')
  $lines.Add('')
  $lines.Add('## Reasons')
  $lines.Add('')
  foreach ($r in $cls.reasons) { $lines.Add('- ' + $r) }
  $lines.Add('')
  $lines.Add('## Recommended Next Actions')
  $lines.Add('')
  foreach ($a in $cls.nextActions) { $lines.Add('- ' + $a) }
  $lines.Add('')
  $lines.Add('## Key Paths')
  $lines.Add('')
  $lines.Add('- ServerExe: ' + $Report.paths.serverExeMasked)
  $lines.Add('- AddinLog: ' + $Report.paths.addinLogMasked)
  $lines.Add('- OutputDir: ' + $Report.paths.outputDirMasked)
  $lines.Add('')
  $lines.Add('## Launch Attempt')
  $lines.Add('')
  $lines.Add('- Tried: ' + $Report.checks.launchAttempt.tried)
  if ($Report.checks.launchAttempt.tried) {
    $lines.Add('- Started: ' + $Report.checks.launchAttempt.started)
    $lines.Add('- StartPid: ' + $Report.checks.launchAttempt.startPid)
    $lines.Add('- StartError: ' + $Report.checks.launchAttempt.startError)
  }
  $lines.Add('')
  $lines.Add('## Notes')
  $lines.Add('')
  $lines.Add('- This report is diagnostic-only; no firewall/policy changes are performed.')
  $lines.Add('- For enterprise policy issues, compare this report with a known-good PC report.')
  return ($lines -join "`r`n")
}

function Convert-ReportToJson([object]$Obj) {
  try {
    Add-Type -AssemblyName System.Web.Extensions -ErrorAction Stop
    $js = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $js.MaxJsonLength = 67108864
    $js.RecursionLimit = 100
    return $js.Serialize($Obj)
  } catch {
    return ($Obj | ConvertTo-Json -Depth 8)
  }
}

$startUtc = Get-Date
$doFull = ($Mode -eq 'Full')
$doEvents = $true

Write-DiagStep ('Initialize mode={0} port={1}' -f $Mode, $Port)
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Write-DiagStep 'Resolve server path'
$resolved = Resolve-ServerPath -ExplicitPath $ServerPath
$serverExe = $resolved.selected

$addinLogPath = Join-Path $env:LOCALAPPDATA ('RevitMCP\logs\addin_{0}.log' -f $Port)
$serverStatePath = Join-Path $env:LOCALAPPDATA 'RevitMCP\server_state.json'

$report = [ordered]@{
  timestamp = Get-IsoNow
  tool = [ordered]@{
    name = 'diagnose_revitmcp_5210.ps1'
    version = '1.0.0'
  }
  host = [ordered]@{
    computerName = $env:COMPUTERNAME
    userName = $env:USERNAME
    osVersion = $(try { (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion' -ErrorAction Stop).ProductName } catch { [System.Environment]::OSVersion.VersionString })
    psVersion = $PSVersionTable.PSVersion.ToString()
  }
  inputs = [ordered]@{
    port = $Port
    mode = $Mode
    includeEvents = [bool]$doEvents
    tryLaunchServer = [bool]$TryLaunchServer
    timeoutSec = $TimeoutSec
    serverPathParam = $ServerPath
  }
  paths = [ordered]@{
    outputDir = $OutputDir
    outputDirMasked = (Mask-Path $OutputDir)
    serverExe = $serverExe
    serverExeMasked = (Mask-Path $serverExe)
    serverCandidates = $resolved.candidates
    addinLog = $addinLogPath
    addinLogMasked = (Mask-Path $addinLogPath)
    serverState = $serverStatePath
    serverStateMasked = (Mask-Path $serverStatePath)
    notes = $resolved.notes
    sources = $resolved.sources
  }
  checks = [ordered]@{
    fileIntegrity = [ordered]@{}
    process = [ordered]@{}
    launchAttempt = [ordered]@{}
    port = [ordered]@{}
    network = [ordered]@{}
    http = [ordered]@{}
    security = [ordered]@{}
    logs = [ordered]@{}
  }
  classification = [ordered]@{}
  nextActions = @()
  durationMs = 0
}

Write-DiagStep 'Check file/process state'
$report.checks.fileIntegrity = Get-FileIntegrity -ExePath $serverExe
$report.checks.process = Get-ProcessSnapshot
$report.checks.launchAttempt = [ordered]@{
  tried = [bool]$TryLaunchServer
  started = $false
  startPid = $null
  startError = $null
}

if ($TryLaunchServer -and (-not $report.checks.process.running) -and $report.checks.fileIntegrity.exists) {
  Write-DiagStep 'Try launch server'
  try {
    $p = $null
    try {
      $p = Start-Process -FilePath $serverExe -ArgumentList @('--port', "$Port") -PassThru -WindowStyle Hidden -ErrorAction Stop
    } catch {
      $p = Start-Process -FilePath $serverExe -PassThru -WindowStyle Hidden -ErrorAction Stop
    }
    Start-Sleep -Milliseconds 1800
    $report.checks.launchAttempt.started = $true
    $report.checks.launchAttempt.startPid = $p.Id
  } catch {
    $report.checks.launchAttempt.started = $false
    $report.checks.launchAttempt.startError = $_.Exception.Message
  }
  $report.checks.process = Get-ProcessSnapshot
}

Write-DiagStep 'Check port listeners'
$listeners = Get-PortListeners -TargetPort $Port
$listenerEnriched = @()
foreach ($l in $listeners) {
  $pinfo = $null
  try {
    $proc = Get-Process -Id $l.pid -ErrorAction Stop
    $pinfo = [ordered]@{
      pid = $proc.Id
      processName = $proc.ProcessName
      processPath = $(try { $proc.Path } catch { $null })
      processPathMasked = $(try { Mask-Path $proc.Path } catch { $null })
    }
  } catch {
    $pinfo = [ordered]@{
      pid = $l.pid
      processName = $null
      processPath = $null
      processPathMasked = $null
    }
  }
  $listenerEnriched += [ordered]@{
    localAddress = $l.localAddress
    localPort = $l.localPort
    pid = $l.pid
    source = $l.source
    process = $pinfo
  }
}

$ownerIsServer = $false
if ($listenerEnriched.Count -gt 0) {
  foreach ($li in $listenerEnriched) {
    if ($li.process.processName -eq 'RevitMCPServer') { $ownerIsServer = $true; break }
  }
}

$report.checks.port = [ordered]@{
  listen = ($listenerEnriched.Count -gt 0)
  ownerIsServer = $ownerIsServer
  listeners = $listenerEnriched
}

Write-DiagStep 'Run network/http/rpc probes'
$report.checks.network = [ordered]@{
  tcp127 = (Test-TcpPort -TargetHost '127.0.0.1' -TargetPort $Port -TimeoutSeconds $TimeoutSec)
  tcplocalhost = (Test-TcpPort -TargetHost 'localhost' -TargetPort $Port -TimeoutSeconds $TimeoutSec)
}

$baseUrl = 'http://127.0.0.1:{0}' -f $Port
$rootProbe = Invoke-HttpProbe -Url ($baseUrl + '/') -Method 'GET' -Body $null -TimeoutSeconds $TimeoutSec

$rpcBody1 = '{"jsonrpc":"2.0","id":"diag-1","method":"ping_server","params":{}}'
$rpcBody2 = '{"jsonrpc":"2.0","id":"diag-2","method":"help.ping_server","params":{}}'
$rpcEndpoints = @('/rpc', '/jsonrpc')
$rpcRows = @()
foreach ($ep in $rpcEndpoints) {
  $url = $baseUrl + $ep
  $r1 = Invoke-HttpProbe -Url $url -Method 'POST' -Body $rpcBody1 -TimeoutSeconds $TimeoutSec
  $r2 = Invoke-HttpProbe -Url $url -Method 'POST' -Body $rpcBody2 -TimeoutSeconds $TimeoutSec
  $p1 = Try-ParseJson $r1.body
  $p2 = Try-ParseJson $r2.body
  $reachable = $false
  if ($r1.statusCode -and $r1.statusCode -ge 200 -and $r1.statusCode -lt 500) { $reachable = $true }
  if ($r2.statusCode -and $r2.statusCode -ge 200 -and $r2.statusCode -lt 500) { $reachable = $true }
  $rpcRows += [ordered]@{
    endpoint = $ep
    url = $url
    probePingServer = [ordered]@{
      statusCode = $r1.statusCode
      ok = $r1.ok
      error = $r1.error
      parsedSummary = (Get-RpcResponseSummary $p1)
    }
    probeHelpPingServer = [ordered]@{
      statusCode = $r2.statusCode
      ok = $r2.ok
      error = $r2.error
      parsedSummary = (Get-RpcResponseSummary $p2)
    }
    reachable = $reachable
  }
}

$report.checks.http = [ordered]@{
  root = [ordered]@{
    ok = $rootProbe.ok
    statusCode = $rootProbe.statusCode
    elapsedMs = $rootProbe.elapsedMs
    error = $rootProbe.error
    bodySnippet = $rootProbe.bodySnippet
  }
  rpc = [ordered]@{
    anyEndpointReachable = (@($rpcRows | Where-Object { $_.reachable }).Count -gt 0)
    endpoints = $rpcRows
  }
}

Write-DiagStep 'Collect security and log hints'
$report.checks.security = [ordered]@{
  firewall = (Get-FirewallSnapshot -TargetPort $Port -ServerExe $serverExe -DoFull:$doFull)
  events = (Get-SecurityEvents -Enabled:$doEvents)
}

$report.checks.logs = [ordered]@{
  addinLogTail = (Read-LogTail -PathText $addinLogPath -MaxLines 120)
  serverState = [ordered]@{
    exists = (Test-Path -LiteralPath $serverStatePath)
    content = $(try { if (Test-Path -LiteralPath $serverStatePath) { [string](Get-Content -LiteralPath $serverStatePath -Raw) } else { $null } } catch { $null })
  }
}

Write-DiagStep 'Classify and write outputs'
$classification = Classify-Result -Report $report
$report.classification = $classification
$report.nextActions = $classification.nextActions
$report.durationMs = [int]((Get-Date) - $startUtc).TotalMilliseconds

$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$jsonPath = Join-Path $OutputDir ('diagnostics_' + $stamp + '.json')
$mdPath = Join-Path $OutputDir ('diagnostics_' + $stamp + '.md')

Write-DiagStep 'Serialize JSON'
$jsonText = Convert-ReportToJson -Obj $report
Write-DiagStep 'Write JSON file'
Set-Content -LiteralPath $jsonPath -Value $jsonText -Encoding UTF8

Write-DiagStep 'Build markdown'
$mdText = Build-Markdown -Report $report
Write-DiagStep 'Write markdown file'
Set-Content -LiteralPath $mdPath -Value $mdText -Encoding UTF8

Write-DiagStep 'Print final summary'
Write-Host ("[Diagnostics] code={0} confidence={1}" -f $classification.code, $classification.confidence) -ForegroundColor Cyan
Write-Host ("[Diagnostics] JSON: {0}" -f (Mask-Path $jsonPath)) -ForegroundColor Green
Write-Host ("[Diagnostics] MD:   {0}" -f (Mask-Path $mdPath)) -ForegroundColor Green
Write-Host ("[Diagnostics] durationMs={0}" -f $report.durationMs) -ForegroundColor DarkGray

if ($OpenOutputFolder) {
  try {
    Start-Process explorer.exe -ArgumentList ('/select,"{0}"' -f $jsonPath) | Out-Null
  } catch {
  }
}

if ($classification.code -eq 'OK') { exit 0 }
if ($classification.code -eq 'PARTIAL_OK') { exit 1 }
exit 2
