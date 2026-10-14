param([string]$Configuration = 'Release', [string]$AchievementSdk = '')
$ErrorActionPreference = 'Stop'
$launcherRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location -LiteralPath $launcherRoot
try {
    & dotnet build samples/Companion.Demo/Companion.Demo.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Demo build failed.' }
    $testArguments = @('run', '--project', 'tests/Companion.Launcher.Tests/Companion.Launcher.Tests.csproj', '-c', $Configuration, '--')
    if ($AchievementSdk) { $testArguments += @('--achievement-sdk', [IO.Path]::GetFullPath($AchievementSdk)) }
    $coreOutput = @(& dotnet @testArguments)
    $coreOutput | Write-Output
    if ($LASTEXITCODE -ne 0) { throw 'Core/process tests failed.' }
    $coreResult = ($coreOutput | Select-Object -Last 1) | ConvertFrom-Json
    & python -m unittest discover -s tests -p test_generator.py
    if ($LASTEXITCODE -ne 0) { throw 'Generator tests failed.' }
    & dotnet build tests/Companion.Launcher.Windows.Tests/Companion.Launcher.Windows.Tests.csproj -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw 'Windows interaction probe build failed.' }
    $interactionOutput = Join-Path $launcherRoot ('build\ui-integration\' + [Guid]::NewGuid().ToString('N'))
    & dotnet "tests/Companion.Launcher.Windows.Tests/bin/$Configuration/net8.0-windows/Companion.Launcher.Windows.Tests.dll" --output $interactionOutput
    if ($LASTEXITCODE -ne 0) { throw 'Windows interaction probe failed.' }
    $interactionResult = Get-Content -Raw -LiteralPath (Join-Path $interactionOutput 'integration.json') | ConvertFrom-Json
    $smokeOutput = Join-Path $launcherRoot ('build\ui-smoke\' + [Guid]::NewGuid().ToString('N'))
    $previousDemoData = $env:COMPANION_DEMO_DATA
    try {
        $env:COMPANION_DEMO_DATA = Join-Path $launcherRoot ('build\demo-smoke-data\' + [Guid]::NewGuid().ToString('N'))
        & dotnet "samples/Companion.Demo/bin/$Configuration/net8.0-windows/Companion.Demo.dll" --smoke-test $smokeOutput
        if ($LASTEXITCODE -ne 0) { throw 'Demo UI smoke failed.' }
    }
    finally { $env:COMPANION_DEMO_DATA = $previousDemoData }
    $smokeResult = Get-Content -Raw -LiteralPath (Join-Path $smokeOutput 'smoke-test.json') | ConvertFrom-Json
    & dotnet pack src/Companion.Launcher.Core/Companion.Launcher.Core.csproj -c $Configuration -o packages
    if ($LASTEXITCODE -ne 0) { throw 'Core package failed.' }
    & dotnet pack src/Companion.Launcher.Windows/Companion.Launcher.Windows.csproj -c $Configuration -o packages
    if ($LASTEXITCODE -ne 0) { throw 'Windows package failed.' }
    $consumerOutput = @(& python scripts/verify_consumers.py)
    if ($LASTEXITCODE -ne 0) { throw 'Pinned-package consumer proof failed.' }
    $consumerResult = ($consumerOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $exportOutput = @(& python scripts/export_source.py)
    if ($LASTEXITCODE -ne 0) { throw 'Source/package export failed.' }
    $exportResult = ($exportOutput -join [Environment]::NewLine) | ConvertFrom-Json
    $verification = [ordered]@{
        passed = $true
        launcherVersion = (Get-Content -Raw -LiteralPath VERSION).Trim()
        platform = 'Windows'
        coreAndProcessChecks = $coreResult.checks
        nativeAchievementInteropTested = [bool]$AchievementSdk
        generatorTests = 4
        windowsInteractionCases = $interactionResult.cases
        windowsInteractionReport = (Join-Path $interactionOutput 'integration.json')
        demoRenderedPages = $smokeResult.pageCount
        demoDraftChecks = $smokeResult.draftChecks
        demoSmokeReport = (Join-Path $smokeOutput 'smoke-test.json')
        pinnedPackageConsumers = $consumerResult.consumers
        packageHashes = $consumerResult.packages
        sourceArchive = $exportResult.sourceArchive
        sourceSha256 = $exportResult.sourceSha256
        packageArchive = $exportResult.packageArchive
        packageSha256 = $exportResult.packageSha256
        existingGamesMigrated = $false
    }
    $verification | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath dist/verification.json -Encoding utf8
    Write-Host 'Verified: shared packages, generated consumers, native processes and Windows UI. Delivery is in dist/.'
}
finally { Pop-Location }
