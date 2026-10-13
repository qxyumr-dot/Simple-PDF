$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$runnerRoot = Join-Path $projectRoot '.build\merge-tests'
New-Item -ItemType Directory -Force -Path $runnerRoot | Out-Null
$libraries = @(& (Join-Path $PSScriptRoot 'restore-pdfmerge.ps1') -SkipDownload)
Copy-Item -LiteralPath $libraries -Destination $runnerRoot -Force
$references = $libraries | Where-Object { [IO.Path]::GetFileName($_) -in @('PdfSharp-gdi.dll','PdfSharp.Shared.dll') } | ForEach-Object { '/reference:' + $_ }
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$runner = Join-Path $runnerRoot 'MergeTests.exe'
$facade = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\netstandard.dll'
if (-not (Test-Path -LiteralPath $facade)) { $facade = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\Facades\netstandard.dll' }
& $compiler /nologo /utf8output /target:exe /platform:x64 "/out:$runner" /reference:System.dll /reference:System.Core.dll "/reference:$facade" @references (Join-Path $projectRoot 'src\PdfMergeService.cs') (Join-Path $projectRoot 'tests\MergeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Merge test compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination ($runner + '.config') -Force
& $runner (Join-Path $projectRoot 'tests\fixtures') (Join-Path $projectRoot 'test-results\merge')
if ($LASTEXITCODE -ne 0) { throw 'Merge tests failed.' }
