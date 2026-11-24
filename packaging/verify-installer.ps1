[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Setup,
    [string]$UpgradeSetup,
    [string]$PortableArchive,
    [string]$Checksums,
    [string]$WorkRoot,
    [switch]$AllowDefaultDataSentinel
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

if (-not $WorkRoot) {
    $WorkRoot = Join-Path ([IO.Path]::GetTempPath()) ('HotkeyLens-verify-' + [Guid]::NewGuid().ToString('N'))
}
$WorkRoot = [IO.Path]::GetFullPath($WorkRoot)
New-Item -ItemType Directory -Path $WorkRoot -Force | Out-Null
$installDir = Join-Path $WorkRoot 'installed'
$dataDir = Join-Path $WorkRoot 'user-data'
New-Item -ItemType Directory -Path $dataDir -Force | Out-Null

$results = [ordered]@{}
$createdSentinel = $null
$defaultDataDir = Join-Path $env:LOCALAPPDATA 'HotkeyLens'
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'HotkeyLens.lnk'
$startMenuShortcut = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\HotkeyLens.lnk'
$portableDir = Join-Path $WorkRoot 'portable'
$portableData = Join-Path $WorkRoot 'portable-data'

function Run-Process([string]$file, [string[]]$arguments, [int]$timeoutSeconds) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new($file)
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $file
    foreach ($argument in $arguments) { $startInfo.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
        try { $process.Kill($true) } catch { }
        throw "$([IO.Path]::GetFileName($file)) did not finish within $timeoutSeconds seconds."
    }
    $exitCode = $process.ExitCode
    $process.Dispose()
    return $exitCode
}

function Assert-Installed([string]$expectedVersion) {
    $exe = Join-Path $installDir 'HotkeyLens.exe'
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) {
        throw "Installed executable is missing: $exe"
    }
    $version = [Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
    if ($version -ne $expectedVersion) {
        throw "Installed version is '$version', expected '$expectedVersion'."
    }
    if (-not (Test-Path -LiteralPath (Join-Path $installDir 'unins000.exe') -PathType Leaf)) {
        throw 'Uninstaller is missing after installation.'
    }
    if (-not (Test-Path -LiteralPath $desktopShortcut -PathType Leaf)) {
        throw "Desktop shortcut was not created: $desktopShortcut"
    }
    if (-not (Test-Path -LiteralPath $startMenuShortcut -PathType Leaf)) {
        throw "Start menu shortcut was not created: $startMenuShortcut"
    }
    $installerFiles = @(Get-ChildItem -LiteralPath $installDir -Recurse -File |
        Where-Object { $_.Name -in @('scan-snapshot.json', 'preferences.json') })
    if ($installerFiles.Count -ne 0) {
        throw 'Installer wrote application data inside its program directory.'
    }
}

function Install-Package([string]$setupPath, [string]$expectedVersion, [string]$label, [switch]$Upgrade) {
    if (-not $Upgrade) {
        if (Test-Path -LiteralPath $desktopShortcut) {
            throw "Refusing to run: a pre-existing shortcut would be overwritten: $desktopShortcut"
        }
        if (Test-Path -LiteralPath $startMenuShortcut) {
            throw "Refusing to run: a pre-existing shortcut would be overwritten: $startMenuShortcut"
        }
    }

    $log = Join-Path $WorkRoot "$label-install.log"
    $arguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-',
        "/DIR=$installDir",
        '/TASKS=desktopicon',
        "/LOG=$log"
    )
    $exitCode = Run-Process $setupPath $arguments 300
    if ($exitCode -ne 0) {
        $tail = if (Test-Path -LiteralPath $log) { (Get-Content -LiteralPath $log -Tail 20) -join "`n" } else { 'no installer log' }
        throw "Installer $label failed with exit code $exitCode.`n$tail"
    }
    Assert-Installed $expectedVersion
    $results["$label.InstalledVersion"] = $expectedVersion
    $results["$label.Shortcuts"] = 'desktop + start menu'
    if ($AllowDefaultDataSentinel -and -not (Test-Path -LiteralPath (Join-Path $defaultDataDir $createdSentinel))) {
        throw "User data sentinel disappeared during $label."
    }
}

function Test-AppLaunch([string]$exe, [string]$dataDirectory, [string]$label, [switch]$RequireWindow) {
    New-Item -ItemType Directory -Path $dataDirectory -Force | Out-Null
    $startInfo = [Diagnostics.ProcessStartInfo]::new($exe)
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = Split-Path -Parent $exe
    $startInfo.Environment['HOTKEYLENS_DATA_DIR'] = $dataDirectory
    $process = [Diagnostics.Process]::Start($startInfo)

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 250
            $process.Refresh()
        } while ($process.MainWindowHandle -eq 0 -and -not $process.HasExited -and [DateTime]::UtcNow -lt $deadline)

        if ($process.HasExited) {
            throw "$label exited during startup with code $($process.ExitCode)."
        }

        $windowTested = $false
        if ($process.MainWindowHandle -ne 0) {
            $root = [System.Windows.Automation.AutomationElement]::RootElement
            $condition = [System.Windows.Automation.PropertyCondition]::new(
                [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $process.Id)
            $window = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
            if ($window) {
                $nameProperty = [System.Windows.Automation.AutomationElement]::NameProperty
                $scanCondition = [System.Windows.Automation.OrCondition]::new(
                    [System.Windows.Automation.Condition[]]@(
                        [System.Windows.Automation.PropertyCondition]::new($nameProperty, '开始扫描'),
                        [System.Windows.Automation.PropertyCondition]::new($nameProperty, '重新扫描')
                    ))
                $button = $window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $scanCondition)
                if (-not $button) { throw "$label window was found, but the scan button was not." }
                $invoke = $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
                $invoke.Invoke()
                $snapshot = Join-Path $dataDirectory 'scan-snapshot.json'
                $scanDeadline = [DateTime]::UtcNow.AddSeconds(30)
                while (-not (Test-Path -LiteralPath $snapshot) -and [DateTime]::UtcNow -lt $scanDeadline) {
                    Start-Sleep -Milliseconds 250
                }
                if (-not (Test-Path -LiteralPath $snapshot)) {
                    throw "$label completed no saveable scan within 30 seconds."
                }
                $json = Get-Content -LiteralPath $snapshot -Raw | ConvertFrom-Json
                $results["$label.Scan"] = "apps=$(@($json.applications).Count), shortcuts=$(@($json.shortcuts).Count), conflicts=$(@($json.conflicts).Count)"
                $windowTested = $true
            }
        }

        if (-not $windowTested -and $RequireWindow) {
            throw "$label did not expose a testable main window."
        }

        if ($process.MainWindowHandle -ne 0) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(15000)) {
                throw "$label did not exit within 15 seconds after its window closed."
            }
            $results["$label.ExitCode"] = $process.ExitCode
        } else {
            $results["$label.Gui"] = 'not available on this runner; process stayed alive'
        }
    }
    finally {
        if (-not $process.HasExited) {
            try { $process.Kill($true) } catch { }
        }
        $process.Dispose()
    }
}

try {
    if ($AllowDefaultDataSentinel) {
        New-Item -ItemType Directory -Path $defaultDataDir -Force | Out-Null
        $createdSentinel = '.installer-test-' + [Guid]::NewGuid().ToString('N') + '.sentinel'
        Set-Content -LiteralPath (Join-Path $defaultDataDir $createdSentinel) -Value 'hotkeylens-data-must-survive' -Encoding utf8NoBOM
    }

    if ($Checksums) {
        $setupName = Split-Path -Leaf $Setup
        $expected = (Get-Content -LiteralPath $Checksums | Where-Object { $_ -match "\s$([regex]::Escape($setupName))$" })
        if (-not $expected) { throw "No checksum entry for $setupName." }
        $actual = (Get-FileHash -LiteralPath $Setup -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($expected.Split()[0] -ne $actual) { throw 'Installer SHA256 does not match SHA256SUMS.txt.' }
        $results['SetupSha256Verified'] = $true
    }

    Install-Package $Setup '1.0.0' 'first'
    if ($UpgradeSetup) {
        Install-Package $UpgradeSetup '1.0.1' 'upgrade' -Upgrade
        $results['UpgradePreservedData'] = $true
    }

    Test-AppLaunch (Join-Path $installDir 'HotkeyLens.exe') $dataDir 'installed'
    if ($AllowDefaultDataSentinel -and -not (Test-Path -LiteralPath (Join-Path $defaultDataDir $createdSentinel))) {
        throw 'The installed app removed the user data sentinel.'
    }

    $uninstaller = Join-Path $installDir 'unins000.exe'
    $uninstallCode = Run-Process $uninstaller @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') 180
    if ($uninstallCode -ne 0) { throw "Uninstaller failed with exit code $uninstallCode." }
    if (Test-Path -LiteralPath (Join-Path $installDir 'HotkeyLens.exe')) { throw 'Uninstall did not remove the application executable.' }
    if (Test-Path -LiteralPath $desktopShortcut) { throw 'Uninstall did not remove the desktop shortcut.' }
    if (Test-Path -LiteralPath $startMenuShortcut) { throw 'Uninstall did not remove the start menu shortcut.' }
    if ($AllowDefaultDataSentinel -and -not (Test-Path -LiteralPath (Join-Path $defaultDataDir $createdSentinel))) {
        throw 'Uninstall removed the user data sentinel.'
    }
    if (-not (Test-Path -LiteralPath $dataDir)) { throw 'Uninstall touched the external application data directory.' }
    $results['Uninstall'] = 'program files and shortcuts removed; external data preserved'

    if ($PortableArchive) {
        Expand-Archive -LiteralPath $PortableArchive -DestinationPath $portableDir -Force
        $portableExe = Join-Path $portableDir 'HotkeyLens.exe'
        if (-not (Test-Path -LiteralPath $portableExe)) { throw 'Portable archive does not contain HotkeyLens.exe at its root.' }
        if (-not (Test-Path -LiteralPath (Join-Path $portableDir 'README-PORTABLE.txt'))) { throw 'Portable archive does not contain README-PORTABLE.txt.' }
        Test-AppLaunch $portableExe $portableData 'portable'
        $results['PortableArchive'] = 'launched without an installed .NET runtime'
    }

    $results['Status'] = 'PASS'
}
catch {
    $results['Status'] = 'FAIL'
    $results['Error'] = $_.Exception.Message
    throw
}
finally {
    if ($createdSentinel) {
        $sentinelPath = Join-Path $defaultDataDir $createdSentinel
        if (Test-Path -LiteralPath $sentinelPath) { Remove-Item -LiteralPath $sentinelPath -Force }
    }
    $results.GetEnumerator() | ForEach-Object { Write-Host ("{0}: {1}" -f $_.Key, $_.Value) }
    if ($env:GITHUB_STEP_SUMMARY) {
        "### Installer verification: $($results['Status'])" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8NoBOM
        $results.GetEnumerator() | ForEach-Object {
            "- **$($_.Key)**: $($_.Value)" | Out-File -FilePath $env:GITHUB_STEP_SUMMARY -Append -Encoding utf8NoBOM
        }
    }
}