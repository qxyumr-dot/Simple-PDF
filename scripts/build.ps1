param([switch]$SkipDownload)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$sdkVersion = '1.0.4258.31'
$sdkRoot = Join-Path $projectRoot '.build\webview2'
$sdkCore = Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'
if (-not (Test-Path -LiteralPath $sdkCore)) {
    if ($SkipDownload) { throw 'The WebView2 SDK has not been restored.' }
    New-Item -ItemType Directory -Force -Path $sdkRoot | Out-Null
    $packagePath = Join-Path $projectRoot '.build\webview2.zip'
    $packageUrl = "https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/$sdkVersion/microsoft.web.webview2.$sdkVersion.nupkg"
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Write-Host "Restoring Microsoft.Web.WebView2 $sdkVersion..."
    Invoke-WebRequest -Uri $packageUrl -OutFile $packagePath -UseBasicParsing
    Expand-Archive -LiteralPath $packagePath -DestinationPath $sdkRoot -Force
}
$distribution = Join-Path $projectRoot 'dist\Simple-PDF'
New-Item -ItemType Directory -Force -Path $distribution | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is required.' }
$core = Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.Core.dll'
$forms = Join-Path $sdkRoot 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
$sourceFiles = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$icon = Join-Path $projectRoot 'assets\app.ico'
$executable = Join-Path $distribution 'Simple-PDF.exe'
$resources = @('welcome.en.html','welcome.zh-CN.html','strings.en.json','strings.zh-CN.json') | ForEach-Object {
    '/resource:' + (Join-Path $projectRoot ('src\' + $_)) + ',' + $_
}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$executable" "/win32icon:$icon" /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/reference:$core" "/reference:$forms" @resources @sourceFiles
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath $core,$forms,(Join-Path $sdkRoot 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $distribution -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'src\App.config') -Destination (Join-Path $distribution 'Simple-PDF.exe.config') -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'LICENSE'),(Join-Path $projectRoot 'docs\WebView2-LICENSE.txt') -Destination $distribution -Force
$archive = Join-Path $projectRoot 'dist\Simple-PDF-Windows-x64.zip'
Compress-Archive -LiteralPath $distribution -DestinationPath $archive -Force
$bytes = (Get-ChildItem -LiteralPath $distribution -File | Measure-Object Length -Sum).Sum
Write-Host ('Portable application: {0:N0} bytes. ZIP: {1:N0} bytes.' -f $bytes,(Get-Item -LiteralPath $archive).Length)
Write-Host "Built $executable"
