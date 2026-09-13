#Requires -Version 7
<#
.SYNOPSIS
  Per-page UI Automation navigation smoke for Agent-X, in one or both shifts,
  with optional screenshots.

.DESCRIPTION
  Launches the built app, selects every item on the navigation rail through UI
  Automation (SelectionItemPattern on the item's AutomationId, which is its x:Name),
  and for each page proves three things:

    1. the process is still alive (a page that throws during realization kills
       the app or leaves the previous page on screen),
    2. the rail reports the item selected,
    3. the content area now contains an element declared by that page's XAML:
       the live UIA tree's AutomationIds and Names are intersected with the
       x:Name values and Text literals read from Views\<Page>.xaml, so the anchor
       set maintains itself when pages change.

  Then it opens the Ctrl+K palette, checks that a row exists for every rail
  label, types a query and checks the matching row survives, and closes it.

  Then it measures the centering of every width-capped content column, and
  exercises Ctrl+N:

    4. content centering: every page whose named, not-Collapsed ScrollViewer wraps
       a content column capped at 600 or more is visited, and the union of that
       scroller's onscreen descendants is measured against the scroller. The page
       set is read from the XAML's structure rather than from the presence of the
       ContentColumn.Fill binding, because keying the check off the fix means
       deleting the fix also deletes the check.

       Both failure modes are checked, because each hides the other: a Stretch
       column is arranged at its cap but positioned where a content-sized one
       would be centered, which put Weekly Digest and Model Manager 275 and 540px
       right of center, while a plain Center column sits right but shrinks to its
       content. So the left and right gutters must match AND the column must still
       fill the cap.

    5. Ctrl+N: from a page that is not Chat, the accelerator must select Chat on
       the rail and leave a blank conversation on screen (the empty-state title,
       resolved from the resw so this survives localization).

  Screenshots: with -CaptureDir, every page and the open palette are captured
  as PNG. The capture asserts GetForegroundWindow() is the app before copying
  pixels, because CopyFromScreen happily returns a picture of whatever window
  is in front (this has produced a PNG of File Explorer before).

  The shift is set by writing "theme" into %LOCALAPPDATA%\AgentX\settings.json,
  which ThemeService reads at startup; the file is backed up and restored.

.PARAMETER Theme
  Dark, Light, or Both (default Both).

.PARAMETER Exe
  Path to AgentX.App.exe. Defaults to the x64 Debug build.

.PARAMETER CaptureDir
  Optional folder for PNG captures.

.PARAMETER PaletteQuery
  Text typed into the palette for the filter check (default "sync").

.PARAMETER Pages
  Optional list of page tags to visit (default: every rail item). The palette and
  onboarding checks still run.

.PARAMETER Onboarding
  Also open the Onboarding page through Jump To (Ctrl+P), step to its last panel
  by invoking "Get Started" and "Next" by their UIA names, and capture it. This
  is the one page off the rail, and the only place the chrome cap CTA lives.

.EXAMPLE
  pwsh scripts/uia-nav-smoke.ps1 -Theme Both -CaptureDir out\captures
#>
param(
    [ValidateSet('Dark', 'Light', 'Both')]
    [string]$Theme = 'Both',
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\AgentX.App\bin\x64\Debug\net8.0-windows10.0.22621.0\win-x64\AgentX.App.exe'),
    [string]$CaptureDir,
    [string]$PaletteQuery = 'sync',
    [switch]$Onboarding,
    [string[]]$Pages
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Win32 {
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
}
"@

$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$Exe = [System.IO.Path]::GetFullPath($Exe)
if (-not (Test-Path $Exe)) { throw "exe not found: $Exe (build with dotnet build -p:Platform=x64)" }
$settingsPath = Join-Path $env:LOCALAPPDATA 'AgentX\settings.json'
if ($CaptureDir) { New-Item -ItemType Directory -Force -Path $CaptureDir | Out-Null }

# ---------------------------------------------------------------- rail + anchors
$shellXaml = Get-Content (Join-Path $repo 'src\AgentX.App\MainWindow.xaml') -Raw
$shellCs = Get-Content (Join-Path $repo 'src\AgentX.App\MainWindow.xaml.cs') -Raw
$pageMap = @{}
foreach ($m in [regex]::Matches($shellCs, '\["(?<tag>[A-Za-z]+)"\]\s*=\s*typeof\(Views\.(?<page>[A-Za-z]+)\)')) {
    $pageMap[$m.Groups['tag'].Value] = $m.Groups['page'].Value
}
$resw = Get-Content (Join-Path $repo 'src\AgentX.App\Strings\en-US\Resources.resw') -Raw
$reswValues = @{}
foreach ($m in [regex]::Matches($resw, '<data name="(?<key>[^"]+)"[^>]*><value>(?<val>[^<]*)</value>')) {
    $reswValues[$m.Groups['key'].Value] = [System.Net.WebUtility]::HtmlDecode($m.Groups['val'].Value)
}
$rail = @()
foreach ($m in [regex]::Matches($shellXaml, '<NavigationViewItem\s+x:Name="(?<name>Nav[A-Za-z]+)"[^>]*?Tag="(?<tag>[A-Za-z]+)"')) {
    $rail += [pscustomobject]@{ Id = $m.Groups['name'].Value; Tag = $m.Groups['tag'].Value }
}
if ($rail.Count -lt 20) { throw "rail scan found only $($rail.Count) items" }
# pwsh -File hands a comma-separated -Pages value over as one string; split it here.
$Pages = @($Pages | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$visit = if ($Pages.Count) { $rail | Where-Object { $Pages -contains $_.Tag } } else { $rail }
if ($Pages.Count -and -not $visit) { throw "none of the requested pages are on the rail: $($Pages -join ', ')" }

# Pages whose named ScrollViewer wraps a width-capped content column. Derived from the XAML's
# structure, deliberately NOT from the presence of the ContentColumn.Fill binding: keying the
# scan off the fix means deleting the fix also deletes its check, which is how a column drifts
# back off center with every light still green.
$contentColumns = @()
foreach ($tag in $pageMap.Keys) {
    $pageFile = Join-Path $repo "src\AgentX.App\Views\$($pageMap[$tag]).xaml"
    if (-not (Test-Path $pageFile)) { continue }
    $railItem = $rail | Where-Object { $_.Tag -eq $tag } | Select-Object -First 1
    if (-not $railItem) { continue }
    try { $doc = [xml](Get-Content $pageFile -Raw) } catch { continue }

    foreach ($sv in $doc.SelectNodes("//*[local-name()='ScrollViewer']")) {
        $svName = if ($sv.Attributes['x:Name']) { $sv.Attributes['x:Name'].Value } else { $null }
        if (-not $svName) { continue }
        # A scroller that starts Collapsed is a detail panel the page reveals on selection, not
        # the page's content column. PluginManager's DetailPanel is one: capped at 900 and
        # centered, but nothing is on screen to measure until a plugin is picked.
        $svVis = if ($sv.Attributes['Visibility']) { $sv.Attributes['Visibility'].Value } else { '' }
        if ($svVis -eq 'Collapsed') { continue }
        # The scroller's own content, not a property element such as ScrollViewer.Resources.
        $child = $sv.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.LocalName -notlike '*.*' } | Select-Object -First 1
        if (-not $child) { continue }
        $capAttr = $child.Attributes['MaxWidth']
        if (-not $capAttr) { continue }
        $cap = 0.0
        if (-not [double]::TryParse($capAttr.Value, [ref]$cap)) { continue }
        # 600 and up is a page content column; the smaller caps are chat bubbles and side panels,
        # which are meant to sit off center.
        if ($cap -lt 600) { continue }
        $contentColumns += [pscustomobject]@{
            Tag = $tag; Id = $railItem.Id; Scroller = $svName; Cap = $cap
        }
    }
}
if ($contentColumns.Count -lt 3) { throw "content column scan found only $($contentColumns.Count) columns" }

function Get-PageAnchors([string]$tag) {
    $page = $pageMap[$tag]
    if (-not $page) { return @() }
    $xaml = Get-Content (Join-Path $repo "src\AgentX.App\Views\$page.xaml") -Raw
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($m in [regex]::Matches($xaml, 'x:Name="([A-Za-z0-9_]+)"')) { [void]$set.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($xaml, '\bText="([^"{}]{3,})"')) { [void]$set.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($xaml, '\bContent="([^"{}]{3,})"')) { [void]$set.Add($m.Groups[1].Value) }
    foreach ($m in [regex]::Matches($xaml, '\bPlaceholderText="([^"{}]{3,})"')) { [void]$set.Add($m.Groups[1].Value) }
    # Localized elements carry their text in the resw, not the XAML: resolve every x:Uid's
    # Text/Content/Header/PlaceholderText value so a fully localized page still has anchors.
    foreach ($m in [regex]::Matches($xaml, 'x:Uid="([A-Za-z0-9_]+)"')) {
        $uid = $m.Groups[1].Value
        foreach ($prop in @('Text', 'Content', 'Header', 'PlaceholderText')) {
            $v = $reswValues["$uid.$prop"]
            if ($v -and $v.Length -ge 3) { [void]$set.Add($v) }
        }
    }
    return $set
}

# ---------------------------------------------------------------- helpers
function Wait-ForWindow($proc, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        $proc.Refresh()
        if ($proc.HasExited) { throw "app exited during startup with code $($proc.ExitCode)" }
        if ($proc.MainWindowHandle -ne [IntPtr]::Zero) { return $proc.MainWindowHandle }
    }
    throw "no main window within $seconds s"
}

function Set-Foreground([IntPtr]$hwnd) {
    for ($i = 0; $i -lt 10; $i++) {
        # Windows refuses SetForegroundWindow to a process that is not the input owner;
        # a synthetic Alt tap makes this process the last input source, which unlocks it.
        [Win32]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero); [Win32]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
        [Win32]::ShowWindow($hwnd, 3) | Out-Null   # SW_MAXIMIZE
        [Win32]::BringWindowToTop($hwnd) | Out-Null
        [Win32]::SetForegroundWindow($hwnd) | Out-Null
        Start-Sleep -Milliseconds 300
        if ([Win32]::GetForegroundWindow() -eq $hwnd) { return $true }
    }
    return $false
}

function Save-Capture([IntPtr]$hwnd, [string]$path) {
    if (-not (Set-Foreground $hwnd)) { Write-Warning "capture skipped: could not foreground the app for $path"; return $false }
    Start-Sleep -Milliseconds 400
    if ([Win32]::GetForegroundWindow() -ne $hwnd) { Write-Warning "capture skipped: foreground lost for $path"; return $false }
    $r = New-Object Win32+RECT
    [Win32]::GetWindowRect($hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    # Park the pointer on the app's title strip: a pointer resting on the taskbar raises
    # a thumbnail preview that lands in the capture.
    [Win32]::SetCursorPos($r.Left + [int]($w / 2), $r.Top + 12) | Out-Null
    Start-Sleep -Milliseconds 250
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $true
}

function Get-LiveIdentifiers($root) {
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $set = New-Object System.Collections.Generic.HashSet[string]
    foreach ($e in $all) {
        try {
            $id = $e.Current.AutomationId; if ($id) { [void]$set.Add($id) }
            $n = $e.Current.Name; if ($n) { [void]$set.Add($n) }
        } catch { }
    }
    return $set
}

function Invoke-ByName($root, [string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
    foreach ($e in $all) {
        if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Button' -or $e.Current.IsOffscreen) { continue }
        try { ($e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke(); return $true } catch { }
    }
    return $false
}

# Union of the onscreen descendants of one element, as Left/Right in screen pixels.
function Measure-Content($element) {
    $all = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $minLeft = [double]::MaxValue; $maxRight = [double]::MinValue; $counted = 0
    foreach ($e in $all) {
        try {
            # IsOffscreen, not the bounding rect: a collapsed or scrolled-out element reports a
            # real-looking rect, and WinUI's content island parks elements at -31000.
            if ($e.Current.IsOffscreen) { continue }
            $r = $e.Current.BoundingRectangle
            if ($r.Width -le 0 -or $r.Height -le 0) { continue }
            if ($r.Left -lt $minLeft) { $minLeft = $r.Left }
            if ($r.Right -gt $maxRight) { $maxRight = $r.Right }
            $counted++
        } catch { }
    }
    return [pscustomobject]@{ Count = $counted; Left = $minLeft; Right = $maxRight }
}

function Find-ById($root, [string]$id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

# ---------------------------------------------------------------- one shift
function Invoke-Shift([string]$shift) {
    Write-Host "`n=== $shift ===" -ForegroundColor Cyan
    $results = @()
    $backup = $null
    if (Test-Path $settingsPath) {
        $backup = Get-Content $settingsPath -Raw
        $json = $backup | ConvertFrom-Json
        $json.theme = $shift
        ($json | ConvertTo-Json -Depth 20) | Set-Content $settingsPath -Encoding UTF8
    } else {
        Write-Warning "settings.json not found; the app will start in its default shift"
    }

    $proc = $null
    try {
        $proc = Start-Process -FilePath $Exe -PassThru
        $hwnd = Wait-ForWindow $proc 90
        Start-Sleep -Seconds 8
        if (-not (Set-Foreground $hwnd)) { Write-Warning "could not foreground the window; captures may be skipped" }
        $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
        $previous = Get-LiveIdentifiers $root

        foreach ($item in $visit) {
            $proc.Refresh()
            if ($proc.HasExited) { $results += [pscustomobject]@{ Shift=$shift; Page=$item.Tag; Result='FAIL'; Detail="app exited before $($item.Tag)" }; break }
            $nav = Find-ById $root $item.Id
            if (-not $nav) { $results += [pscustomobject]@{ Shift=$shift; Page=$item.Tag; Result='FAIL'; Detail="rail item $($item.Id) not in UIA tree" }; continue }
            try { ($nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() }
            catch { $results += [pscustomobject]@{ Shift=$shift; Page=$item.Tag; Result='FAIL'; Detail="select failed: $($_.Exception.Message)" }; continue }

            $anchors = Get-PageAnchors $item.Tag
            $hit = $null; $selected = $false
            $deadline = (Get-Date).AddSeconds(8)
            do {
                Start-Sleep -Milliseconds 700
                $proc.Refresh()
                if ($proc.HasExited) { break }
                try { $selected = ($nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected } catch { }
                $live = Get-LiveIdentifiers $root
                foreach ($a in $anchors) { if ($live.Contains($a)) { $hit = $a; break } }
            } while (-not ($hit -and $selected) -and (Get-Date) -lt $deadline)

            $proc.Refresh()
            if ($proc.HasExited) { $results += [pscustomobject]@{ Shift=$shift; Page=$item.Tag; Result='FAIL'; Detail="app died navigating to $($item.Tag)" }; break }

            $ok = $selected -and $hit
            $detail = if ($hit) { "anchor '$hit'" } else { "no page anchor found ($($anchors.Count) candidates); selected=$selected" }
            $results += [pscustomobject]@{ Shift=$shift; Page=$item.Tag; Result=($(if ($ok) {'PASS'} else {'FAIL'})); Detail=$detail }
            Write-Host ("  {0,-5} {1,-18} {2}" -f $(if ($ok) {'PASS'} else {'FAIL'}), $item.Tag, $detail)
            if ($CaptureDir) { Save-Capture $hwnd (Join-Path $CaptureDir "$shift-$($item.Tag).png") | Out-Null }
            $previous = $live
        }

        # ---- palette
        $proc.Refresh()
        if (-not $proc.HasExited) {
            if (Set-Foreground $hwnd) {
                [System.Windows.Forms.SendKeys]::SendWait('^k')
                Start-Sleep -Seconds 2
                # The palette is open only if its search well is in the tree and has focus
                # (SearchInput is its x:Name). Without this, the rail's own TextBlocks would
                # satisfy a label search and the check would pass with the palette closed.
                $search = Find-ById $root 'SearchInput'
                $focused = try { [System.Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId } catch { '' }
                $paletteOpen = ($null -ne $search) -and (-not $search.Current.IsOffscreen) -and ($focused -eq 'SearchInput')
                $labels = @()
                foreach ($item in $rail) {
                    $nav = Find-ById $root $item.Id
                    if ($nav) { $labels += $nav.Current.Name }
                }
                # Rows live inside the palette card, to the right of the rail: count Text
                # elements with the label whose left edge is past the pane.
                $missing = @()
                if ($paletteOpen) {
                    foreach ($label in $labels) {
                        $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $label)
                        $found = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
                        $rows = 0
                        foreach ($f in $found) {
                            if ($f.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' -and $f.Current.BoundingRectangle.Left -gt 400) { $rows++ }
                        }
                        if ($rows -lt 1) { $missing += $label }
                    }
                }
                $ok = $paletteOpen -and $missing.Count -eq 0
                $detail = if (-not $paletteOpen) { "palette did not open (search well present=$($null -ne $search), focused='$focused')" } elseif ($ok) { "$($labels.Count) rail labels present as rows" } else { "missing rows: $($missing -join ', ')" }
                $results += [pscustomobject]@{ Shift=$shift; Page='(palette rows)'; Result=($(if ($ok) {'PASS'} else {'FAIL'})); Detail=$detail }
                Write-Host ("  {0,-5} {1,-18} {2}" -f $(if ($ok) {'PASS'} else {'FAIL'}), '(palette rows)', $results[-1].Detail)
                if ($CaptureDir) { Save-Capture $hwnd (Join-Path $CaptureDir "$shift-palette.png") | Out-Null }

                [System.Windows.Forms.SendKeys]::SendWait($PaletteQuery)
                Start-Sleep -Seconds 1
                $expected = $labels | Where-Object { $_ -match [regex]::Escape($PaletteQuery) } | Select-Object -First 1
                $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $expected)
                $found = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
                $stillThere = $false
                foreach ($f in $found) { if ($f.Current.ControlType.ProgrammaticName -eq 'ControlType.Text' -and $f.Current.BoundingRectangle.Left -gt 400) { $stillThere = $true } }
                $results += [pscustomobject]@{ Shift=$shift; Page='(palette filter)'; Result=($(if ($stillThere) {'PASS'} else {'FAIL'})); Detail="query '$PaletteQuery' keeps '$expected'" }
                Write-Host ("  {0,-5} {1,-18} {2}" -f $(if ($stillThere) {'PASS'} else {'FAIL'}), '(palette filter)', $results[-1].Detail)
                if ($CaptureDir) { Save-Capture $hwnd (Join-Path $CaptureDir "$shift-palette-filtered.png") | Out-Null }
                [System.Windows.Forms.SendKeys]::SendWait('{ESC}')
                Start-Sleep -Milliseconds 500
            } else {
                $results += [pscustomobject]@{ Shift=$shift; Page='(palette)'; Result='FAIL'; Detail='could not foreground the window to send Ctrl+K' }
                Write-Host "  FAIL  (palette)          could not foreground the window to send Ctrl+K"
            }
        }

        # ---- content centering
        $proc.Refresh()
        if (-not $proc.HasExited) {
            foreach ($col in $contentColumns) {
                $nav = Find-ById $root $col.Id
                if (-not $nav) {
                    $results += [pscustomobject]@{ Shift=$shift; Page="$($col.Tag) centering"; Result='FAIL'; Detail="rail item $($col.Id) not in UIA tree" }
                    Write-Host ("  {0,-5} {1,-18} {2}" -f 'FAIL', "$($col.Tag) center", "rail item $($col.Id) not in UIA tree")
                    continue
                }
                try { ($nav.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() } catch { }
                Start-Sleep -Milliseconds 2500
                $proc.Refresh()
                if ($proc.HasExited) {
                    $results += [pscustomobject]@{ Shift=$shift; Page="$($col.Tag) centering"; Result='FAIL'; Detail="app died navigating to $($col.Tag)" }
                    Write-Host ("  {0,-5} {1,-18} {2}" -f 'FAIL', "$($col.Tag) center", "app died navigating to $($col.Tag)")
                    break
                }

                $scroller = Find-ById $root $col.Scroller
                if (-not $scroller) {
                    $results += [pscustomobject]@{ Shift=$shift; Page="$($col.Tag) centering"; Result='FAIL'; Detail="scroller $($col.Scroller) not in UIA tree" }
                    Write-Host ("  {0,-5} {1,-18} {2}" -f 'FAIL', "$($col.Tag) center", "scroller $($col.Scroller) not in UIA tree")
                    continue
                }

                $sr = $scroller.Current.BoundingRectangle
                $content = Measure-Content $scroller
                if ($content.Count -lt 1) {
                    $results += [pscustomobject]@{ Shift=$shift; Page="$($col.Tag) centering"; Result='FAIL'; Detail="scroller has no onscreen descendants to measure" }
                    Write-Host ("  {0,-5} {1,-18} {2}" -f 'FAIL', "$($col.Tag) center", "scroller has no onscreen descendants to measure")
                    continue
                }

                $gutterLeft = $content.Left - $sr.Left
                $gutterRight = $sr.Right - $content.Right
                $skew = [math]::Abs($gutterLeft - $gutterRight)
                $width = $content.Right - $content.Left
                # The column fills min(viewport, cap). PaddingPage (32,24,32,24) takes 64 off the
                # measured content, and 16 more is left for sub-pixel and DPI rounding.
                $expected = [math]::Min($sr.Width, $col.Cap) - 80

                $centered = $skew -le 8
                $fills = $width -ge $expected
                $inside = $content.Right -le ($sr.Right + 2)
                $ok = $centered -and $fills -and $inside

                $detail = "gutters $([math]::Round($gutterLeft))/$([math]::Round($gutterRight)), skew $([math]::Round($skew)), width $([math]::Round($width)) of cap $($col.Cap)"
                if (-not $centered) { $detail += "; OFF CENTER" }
                if (-not $fills) { $detail += "; shrank below the cap (expected >= $([math]::Round($expected)))" }
                if (-not $inside) { $detail += "; runs past the scroller's right edge" }

                $results += [pscustomobject]@{ Shift=$shift; Page="$($col.Tag) centering"; Result=($(if ($ok) {'PASS'} else {'FAIL'})); Detail=$detail }
                Write-Host ("  {0,-5} {1,-18} {2}" -f $(if ($ok) {'PASS'} else {'FAIL'}), "$($col.Tag) center", $detail)
                if ($CaptureDir) { Save-Capture $hwnd (Join-Path $CaptureDir "$shift-$($col.Tag)-centering.png") | Out-Null }
            }
        }

        # ---- Ctrl+N: a new conversation from anywhere
        $proc.Refresh()
        if (-not $proc.HasExited) {
            $chatItem = $rail | Where-Object { $_.Tag -eq 'Chat' } | Select-Object -First 1
            $awayItem = $rail | Where-Object { $_.Tag -ne 'Chat' } | Select-Object -First 1
            $navChat = if ($chatItem) { Find-ById $root $chatItem.Id } else { $null }
            $navAway = if ($awayItem) { Find-ById $root $awayItem.Id } else { $null }

            if (-not ($navChat -and $navAway)) {
                $results += [pscustomobject]@{ Shift=$shift; Page='(Ctrl+N)'; Result='FAIL'; Detail='could not find the Chat rail item and one other to start from' }
                Write-Host "  FAIL  (Ctrl+N)           could not find the Chat rail item and one other to start from"
            } else {
                # Start somewhere that is not Chat, so selecting Chat is evidence the
                # accelerator navigated rather than evidence of where we already were.
                try { ($navAway.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Select() } catch { }
                Start-Sleep -Seconds 2
                $before = try { ($navChat.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected } catch { $true }

                if (-not (Set-Foreground $hwnd)) {
                    $results += [pscustomobject]@{ Shift=$shift; Page='(Ctrl+N)'; Result='FAIL'; Detail='could not foreground the window to send Ctrl+N' }
                    Write-Host "  FAIL  (Ctrl+N)           could not foreground the window to send Ctrl+N"
                } else {
                    [System.Windows.Forms.SendKeys]::SendWait('^n')
                    Start-Sleep -Seconds 3
                    $proc.Refresh()
                    if ($proc.HasExited) {
                        $results += [pscustomobject]@{ Shift=$shift; Page='(Ctrl+N)'; Result='FAIL'; Detail='app died on Ctrl+N' }
                        Write-Host "  FAIL  (Ctrl+N)           app died on Ctrl+N"
                    } else {
                        $after = try { ($navChat.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)).Current.IsSelected } catch { $false }
                        # The blank conversation shows Chat's empty state. Resolved from the
                        # resw, so a localized build checks its own string.
                        $emptyTitle = $reswValues['Chat_EmptyTitle.Text']
                        $live = Get-LiveIdentifiers $root
                        $blank = $emptyTitle -and $live.Contains($emptyTitle)

                        $ok = (-not $before) -and $after -and $blank
                        $detail = "from $($awayItem.Tag): Chat selected $before -> $after, blank conversation=$blank"
                        if ($before) { $detail += "; started on Chat already, so this proves nothing" }
                        if (-not $blank) { $detail += "; empty state '$emptyTitle' not on screen" }

                        $results += [pscustomobject]@{ Shift=$shift; Page='(Ctrl+N)'; Result=($(if ($ok) {'PASS'} else {'FAIL'})); Detail=$detail }
                        Write-Host ("  {0,-5} {1,-18} {2}" -f $(if ($ok) {'PASS'} else {'FAIL'}), '(Ctrl+N)', $detail)
                        if ($CaptureDir) { Save-Capture $hwnd (Join-Path $CaptureDir "$shift-ctrl-n.png") | Out-Null }
                    }
                }
            }
        }

        # ---- onboarding (off the rail; reached through Jump To)
        $proc.Refresh()
        if ($Onboarding -and -not $proc.HasExited -and (Set-Foreground $hwnd)) {
            [System.Windows.Forms.SendKeys]::SendWait('^p')
            Start-Sleep -Seconds 2
            [System.Windows.Forms.SendKeys]::SendWait('Onboarding')
            Start-Sleep -Seconds 1
            [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
            Start-Sleep -Seconds 3
            $anchors = Get-PageAnchors 'Onboarding'
            $live = Get-LiveIdentifiers $root
            $hit = $null; foreach ($a in $anchors) { if ($live.Contains($a)) { $hit = $a; break } }
            $stepped = 0
            if ($hit) {
                foreach ($label in @('Get Started', 'Next', 'Next')) {
                    if (Invoke-ByName $root $label) { $stepped++; Start-Sleep -Milliseconds 900 }
                }
            }
            $ok = $hit -and $stepped -eq 3
            $detail = if ($hit) { "anchor '$hit', stepped $stepped/3 to the Launch panel" } else { 'Onboarding page not reached through Jump To' }
            $results += [pscustomobject]@{ Shift=$shift; Page='Onboarding'; Result=($(if ($ok) {'PASS'} else {'FAIL'})); Detail=$detail }
            Write-Host ("  {0,-5} {1,-18} {2}" -f $(if ($ok) {'PASS'} else {'FAIL'}), 'Onboarding', $detail)
            if ($CaptureDir) { Save-Capture $hwnd (Join-Path $CaptureDir "$shift-Onboarding-launch.png") | Out-Null }
        }
    }
    finally {
        if ($proc -and -not $proc.HasExited) {
            $proc.CloseMainWindow() | Out-Null
            Start-Sleep -Seconds 3
            if (-not $proc.HasExited) { $proc.Kill() }
        }
        if ($backup -ne $null) { Set-Content $settingsPath $backup -Encoding UTF8 -NoNewline }
    }
    return $results
}

$shifts = if ($Theme -eq 'Both') { @('Dark', 'Light') } else { @($Theme) }
$all = @()
foreach ($s in $shifts) { $all += Invoke-Shift $s }

$fails = @($all | Where-Object Result -eq 'FAIL')
Write-Host "`n$($all.Count) checks, $($fails.Count) failed" -ForegroundColor $(if ($fails.Count) {'Red'} else {'Green'})
if ($CaptureDir) { $all | ConvertTo-Json | Set-Content (Join-Path $CaptureDir 'uia-nav-smoke.json') -Encoding UTF8 }
exit $fails.Count
