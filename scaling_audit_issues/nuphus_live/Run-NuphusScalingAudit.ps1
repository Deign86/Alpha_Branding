param(
  [string]$AppPath = "C:\Users\Deign\Downloads\Alpha_Branding\src\Alpha.Branding\bin\Release\net8.0-windows10.0.19041.0\Alpha.Branding.exe",
  [string]$OutDir = "C:\Users\Deign\Downloads\Alpha_Branding\scaling_audit_issues\nuphus_live"
)
$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

class NuphusClient {
  [System.Diagnostics.Process]$Proc
  [int]$MsgId = 0
  NuphusClient() {
    $pinfo = New-Object System.Diagnostics.ProcessStartInfo
    $pinfo.FileName = "C:\Users\Deign\AppData\Roaming\npm\nuphus-mcp.cmd"
    $pinfo.RedirectStandardInput = $true
    $pinfo.RedirectStandardOutput = $true
    $pinfo.RedirectStandardError = $true
    $pinfo.UseShellExecute = $false
    $pinfo.CreateNoWindow = $true
    $this.Proc = [System.Diagnostics.Process]::Start($pinfo)
  }
  [PSCustomObject] Send([string]$method, [hashtable]$params) {
    $this.MsgId++
    $req = @{ jsonrpc = "2.0"; id = $this.MsgId; method = $method; params = $params } | ConvertTo-Json -Compress -Depth 10
    $this.Proc.StandardInput.WriteLine($req)
    $this.Proc.StandardInput.Flush()
    $raw = $this.Proc.StandardOutput.ReadLine()
    if (-not $raw) { return $null }
    return ($raw | ConvertFrom-Json)
  }
  [PSCustomObject] CallTool([string]$name, [hashtable]$arguments) {
    return $this.Send("tools/call", @{ name = $name; arguments = $arguments })
  }
  [void] Close() {
    try { $this.Proc.StandardInput.Close() } catch {}
    if ($this.Proc -and -not $this.Proc.HasExited) {
      $this.Proc.WaitForExit(2000) | Out-Null
      if (-not $this.Proc.HasExited) { $this.Proc.Kill() }
    }
  }
}

$client = [NuphusClient]::new()
try {
  $init = $client.Send("initialize", @{ protocolVersion = "2024-11-05"; capabilities = @{}; clientInfo = @{ name = "scaling-audit"; version = "1.0" } })
  Write-Host "[INIT] $($init.result.serverInfo.name) v$($init.result.serverInfo.version)"
  $client.Send("notifications/initialized", @{}) | Out-Null

  $size = $client.CallTool("desktop_screen_size", @{})
  Write-Host "[SCREEN] $($size.result.content[0].text)"

  # Launch app
  $app = Start-Process -FilePath $AppPath -PassThru
  Start-Sleep -Seconds 3

  $list = $client.CallTool("desktop_windows_list", @{})
  $winText = $list.result.content[0].text
  Write-Host "[WINDOWS] raw list:"
  Write-Host $winText

  # Find Alpha Branding hwnd: list format hwnd/title/position - parse integer before Alpha Premier
  $hwnd = $null
  foreach ($line in ($winText -split "`n")) {
    if ($line -match "Alpha Premier") {
      Write-Host "  MATCH: $line"
      if ($line -match "^\s*(\d+)") { $hwnd = [int]$matches[1] }
      # also try hwnd=1234 or (1234)
      if (-not $hwnd -and $line -match "(?i)hwnd[:= ]+(\d+)") { $hwnd = [int]$matches[1] }
    }
  }
  if (-not $hwnd) { throw "Could not find Alpha Branding window hwnd. Full list above." }
  Write-Host "[HWND] $hwnd"

  $act = $client.CallTool("desktop_window_activate", @{ hwnd = $hwnd })
  Write-Host "[ACTIVATE] $($act.result.content[0].text)"
  Start-Sleep -Milliseconds 800

  $info = $client.CallTool("desktop_window_info", @{ hwnd = $hwnd })
  Write-Host "[INFO] $($info.result.content[0].text)"

  # Scale simulation matrix: label -> width,height (logical size at that DPI on 1080p)
  $cases = @(
    @{ label = "100pct_1220x840_baseline"; w = 1220; h = 840 },
    @{ label = "125pct_equiv_976x672"; w = 976; h = 672 },
    @{ label = "150pct_equiv_813x560"; w = 813; h = 560 },
    @{ label = "175pct_equiv_697x480"; w = 697; h = 480 },
    @{ label = "200pct_equiv_610x420"; w = 610; h = 420 }
  )
  foreach ($c in $cases) {
    $r = $client.CallTool("desktop_window_resize", @{ hwnd = $hwnd; width = $c.w; height = $c.h })
    Start-Sleep -Milliseconds 900
    $shotPath = Join-Path $OutDir ("live_" + $c.label)
    $s = $client.CallTool("desktop_window_screenshot", @{ hwnd = $hwnd; path = $shotPath })
    Write-Host "[SHOT $($c.label)] $($s.result.content[0].text)"
  }
  # Full desktop shot with app open at baseline restored
  $client.CallTool("desktop_window_resize", @{ hwnd = $hwnd; width = 1220; height = 840 }) | Out-Null
  Start-Sleep -Milliseconds 900
  $full = $client.CallTool("desktop_screenshot", @{ path = (Join-Path $OutDir "live_desktop_with_app_1220x840") })
  Write-Host "[DESKTOP] $($full.result.content[0].text)"
}
finally {
  $client.Close()
  Get-Process "Alpha.Branding" -ErrorAction SilentlyContinue | Stop-Process -Force
}
Write-Host "DONE"
