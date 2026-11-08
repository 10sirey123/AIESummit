param([int]$TimeoutSeconds = 180)
$ErrorActionPreference = 'Stop'
$origin = 'http://localhost:18080'
$auth = 'http://localhost:19099/identitytoolkit.googleapis.com/v1/accounts:'
$suffix = [guid]::NewGuid().ToString('N')
$password = [guid]::NewGuid().ToString('N')
$fixture = Join-Path ([IO.Path]::GetTempPath()) "aiesummit-$suffix.pdf"

function New-User([string]$prefix) {
    $request = @{email="$prefix-$suffix@example.test"; password=$password; returnSecureToken=$true} | ConvertTo-Json -Compress
    Invoke-RestMethod -Uri "${auth}signUp?key=demo-key" -Method Post -ContentType 'application/json' -Body $request
}

function New-Fixture {
    $objects = [System.Collections.Generic.List[string]]::new()
    $objects.Add('<< /Type /Catalog /Pages 2 0 R >>')
    $objects.Add('<< /Type /Pages /Count 3 /Kids [4 0 R 6 0 R 8 0 R] >>')
    $objects.Add('<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>')
    $texts = @(
        'NORTHWIND INVOICE 1842 - Page 1 of 2|Invoice date: October 3, 2026|Consulting services for Redwood Labs|Continued on page 2.',
        'NORTHWIND INVOICE 1842 - Page 2 of 2|Invoice continued from page 1.|Subtotal: USD 540.00|Balance due: USD 540.00|End of invoice.',
        'BIRCH COUNTY PUBLIC LIBRARY - EVENT FLYER|Community book fair|Saturday November 14, 10 AM to 4 PM|Admission is free. Everyone welcome.'
    )
    for ($i = 0; $i -lt $texts.Count; $i++) {
        $contentId = 5 + $i * 2
        $objects.Add("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R >> >> /Contents $contentId 0 R >>")
        $stream = "BT /F1 15 Tf 19 TL 40 735 Td " + (($texts[$i] -split '\|' | ForEach-Object { "($_) Tj T*" }) -join ' ') + " ET`n"
        $objects.Add("<< /Length $($stream.Length) >>`nstream`n$($stream)endstream")
    }
    $pdf = [System.Text.StringBuilder]::new("%PDF-1.4`n")
    $offsets = foreach ($i in 0..($objects.Count - 1)) {
        $pdf.Length
        [void]$pdf.Append("$($i + 1) 0 obj`n$($objects[$i])`nendobj`n")
    }
    $xref = $pdf.Length
    [void]$pdf.Append("xref`n0 $($objects.Count + 1)`n0000000000 65535 f `n")
    foreach ($offset in $offsets) { [void]$pdf.Append(('{0:D10} 00000 n ' -f $offset) + "`n") }
    [void]$pdf.Append("trailer`n<< /Size $($objects.Count + 1) /Root 1 0 R >>`nstartxref`n$xref`n%%EOF`n")
    [IO.File]::WriteAllText($fixture, $pdf.ToString(), [Text.Encoding]::ASCII)
}

try {
    $first = New-User 'owner'
    $second = New-User 'other'
    $request = @{email=$first.email; password=$password; returnSecureToken=$true} | ConvertTo-Json -Compress
    $signedIn = Invoke-RestMethod -Uri "${auth}signInWithPassword?key=demo-key" -Method Post -ContentType 'application/json' -Body $request
    $owner = @{Authorization="Bearer $($signedIn.idToken)"}
    $other = @{Authorization="Bearer $($second.idToken)"}
    New-Fixture
    $upload = Invoke-WebRequest "$origin/api/v1/uploads" -Method Post -Headers $owner -Form @{file=Get-Item $fixture}
    if ($upload.StatusCode -ne 202) { throw "Unexpected upload response $($upload.StatusCode)." }
    $path = "$origin$($upload.Headers.Location[0])"
    if ((Invoke-WebRequest $path -SkipHttpErrorCheck).StatusCode -ne 401) { throw 'Anonymous access was accepted.' }
    if ((Invoke-WebRequest $path -Headers $other -SkipHttpErrorCheck).StatusCode -ne 404) { throw 'A second user could read the upload.' }
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $result = Invoke-RestMethod $path -Headers $owner
        if ($result.upload.status -eq 'failed') { throw "Upload $($result.upload.id) failed. $($result.upload.error)" }
        if ($result.upload.status -eq 'ready') { break }
        Start-Sleep -Seconds 3
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($result.upload.status -ne 'ready') { throw "Upload $($result.upload.id) timed out in $($result.upload.status)." }
    if ($result.upload.pageCount -ne 3 -or $result.documents.Count -ne 2 -or
        $result.documents[0].firstPage -ne 1 -or $result.documents[0].lastPage -ne 2 -or
        $result.documents[1].firstPage -ne 3 -or $result.documents[1].lastPage -ne 3 -or
        $result.documents[0].markdown -notmatch 'INVOICE 1842' -or
        $result.documents[0].markdown -notmatch '<!-- page break -->' -or
        $result.documents[1].markdown -notmatch 'COMMUNITY BOOK FAIR') {
        throw 'The result did not preserve the expected two documents and their Markdown.'
    }
    $id = $result.upload.id
    $ownerUploads = Invoke-RestMethod "$origin/api/v1/uploads" -Headers $owner
    $otherUploads = Invoke-RestMethod "$origin/api/v1/uploads" -Headers $other
    if ($id -notin $ownerUploads.id -or $id -in $otherUploads.id) { throw 'The upload list did not isolate users.' }

    $adminToken = (Get-Content -Raw -LiteralPath "$PSScriptRoot/../artifacts/admin.token.local.txt").Trim()
    $adminHeaders = @{Authorization="Bearer $adminToken"}
    $operations = 'http://localhost:13088/admin/v1/agents/executions'
    if ((Invoke-WebRequest $operations -SkipHttpErrorCheck).StatusCode -ne 401) { throw 'Agent telemetry allowed anonymous access.' }
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $fileRuns = (Invoke-RestMethod "${operations}?entityType=UploadedFile&entityId=$id" -Headers $adminHeaders).items
        $promptRuns = (Invoke-RestMethod "${operations}?agentName=LlmPromptAgent&take=50" -Headers $adminHeaders).items
        $ocr = $fileRuns | Where-Object { $_.agentName -eq 'OcrAgent' -and $_.state -eq 'Succeeded' }
        $step = $fileRuns | Where-Object { $_.statusField -eq 'LLMStepStatus' -and $_.state -eq 'Succeeded' }
        $prompt = $promptRuns | Where-Object { $_.rootEntityId -eq $id -and $_.state -eq 'Succeeded' }
        if ($ocr -and $step -and $prompt) { break }
        Start-Sleep -Seconds 1
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$ocr -or !$step -or !$prompt) { throw "Admin did not report successful agent runs for upload $id." }

    Write-Host "Verified Firebase sign-up and sign-in, upload $id, user isolation, Gemini OCR, document ranges 1–2 and 3, and Admin telemetry."
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}


