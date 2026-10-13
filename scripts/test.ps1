param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $SkipBuild) { & (Join-Path $PSScriptRoot 'build.ps1') }
& (Join-Path $PSScriptRoot 'test-merge.ps1')
$resultsRoot = Join-Path $projectRoot 'test-results'
New-Item -ItemType Directory -Force -Path $resultsRoot | Out-Null
$pdfPath = Join-Path $resultsRoot (([string][char]0x6587) + ' test paper #1.pdf')

# Generate a small, valid three-page PDF without an additional dependency.
$encoding = [Text.Encoding]::ASCII
$objects = [Collections.Generic.List[string]]::new()
$objects.Add('<< /Type /Catalog /Pages 2 0 R >>')
$objects.Add('<< /Type /Pages /Kids [3 0 R 5 0 R 7 0 R] /Count 3 >>')
for ($page = 0; $page -lt 3; $page++) {
    $content = 'BT /F1 26 Tf 64 740 Td (Simple-PDF - Page ' + ($page + 1) + ') Tj 0 -48 Td /F1 14 Tf (Local reading and printer selection test.) Tj ET'
    $streamNumber = 4 + $page * 2
    $objects.Add('<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 9 0 R >> >> /Contents ' + $streamNumber + ' 0 R >>')
    $objects.Add('<< /Length ' + $encoding.GetByteCount($content) + " >>`nstream`n" + $content + "`nendstream")
}
$objects.Add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
$pdf = "%PDF-1.4`n"
$offsets = [Collections.Generic.List[int]]::new()
$offsets.Add(0)
for ($index = 0; $index -lt $objects.Count; $index++) {
    $offsets.Add($encoding.GetByteCount($pdf))
    $pdf += ($index + 1).ToString() + " 0 obj`n" + $objects[$index] + "`nendobj`n"
}
$xref = $encoding.GetByteCount($pdf)
$pdf += "xref`n0 " + $offsets.Count + "`n0000000000 65535 f `n"
for ($index = 1; $index -lt $offsets.Count; $index++) {
    $pdf += $offsets[$index].ToString('D10') + " 00000 n `n"
}
$pdf += "trailer`n<< /Size " + $offsets.Count + " /Root 1 0 R >>`nstartxref`n" + $xref + "`n%%EOF`n"
[IO.File]::WriteAllBytes($pdfPath, $encoding.GetBytes($pdf))

$exePath = Join-Path $projectRoot 'dist\Simple-PDF\Simple-PDF.exe'
$cases = @(
    @{ Name = 'english-default'; Extra = @() },
    @{ Name = 'chinese'; Extra = @('--language=zh-CN') },
    @{ Name = 'language-switch'; Extra = @('--test-switch') },
    @{ Name = 'merged-document'; Extra = @(); Pdf = (Join-Path $resultsRoot 'merge\merged.pdf') }
)
foreach ($case in $cases) {
    $caseRoot = Join-Path $resultsRoot $case.Name
    $casePdf = if ($case.ContainsKey('Pdf')) { $case.Pdf } else { $pdfPath }
    $arguments = @('--self-test', ('"' + $casePdf + '"'), ('"' + $caseRoot + '"')) + $case.Extra
    $process = Start-Process -FilePath $exePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $null = $process.Handle
    if (-not $process.WaitForExit(45000)) {
        Stop-Process -Id $process.Id
        throw ('Smoke test timed out: ' + $case.Name)
    }
    $resultPath = Join-Path $caseRoot 'result.txt'
    if (-not (Test-Path -LiteralPath $resultPath)) { throw ('No result for: ' + $case.Name) }
    $result = Get-Content -LiteralPath $resultPath -Raw
    Write-Host ($case.Name + ': ' + $result)
    if ($process.ExitCode -ne 0 -or -not $result.StartsWith('PASS')) { throw ('Smoke test failed: ' + $case.Name) }
}
Write-Host 'All smoke tests passed. No physical print job was sent.'
