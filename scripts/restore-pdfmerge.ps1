param([switch]$SkipDownload)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$packages = Get-Content -LiteralPath (Join-Path $projectRoot 'dependencies.json') -Raw | ConvertFrom-Json
$libraries = @()
$references = '<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003"><ItemGroup>'
foreach ($package in $packages) {
    $packageRoot = Join-Path $projectRoot ('.build\pdfmerge\' + $package.id)
    $firstLibrary = Join-Path $packageRoot ('lib\' + $package.framework + '\' + $package.assemblies[0])
    if (-not (Test-Path -LiteralPath $firstLibrary)) {
        if ($SkipDownload) { throw ('Missing package: ' + $package.id) }
        New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
        $archive = Join-Path $projectRoot ('.build\' + $package.id + '.zip')
        $url = 'https://api.nuget.org/v3-flatcontainer/' + $package.id + '/' + $package.version + '/' + $package.id + '.' + $package.version + '.nupkg'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Write-Host ('Restoring ' + $package.id + ' ' + $package.version + '...')
        Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
        Expand-Archive -LiteralPath $archive -DestinationPath $packageRoot -Force
    }
    foreach ($assembly in $package.assemblies) {
        $library = Join-Path $packageRoot ('lib\' + $package.framework + '\' + $assembly)
        if (-not (Test-Path -LiteralPath $library)) { throw ('Missing assembly: ' + $assembly) }
        $libraries += $library
        $references += '<Reference Include="' + [IO.Path]::GetFileNameWithoutExtension($assembly) + '"><HintPath>' + [Security.SecurityElement]::Escape($library) + '</HintPath></Reference>'
    }
}
$references += '</ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $projectRoot '.build\PdfMerge.references.props'), $references)
Write-Output $libraries
