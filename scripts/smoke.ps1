param([int]$TimeoutSeconds = 180, [switch]$VerifyPageSections, [switch]$KeepTestSession, [switch]$ContractFixture,
    [string]$ExpectedPageModel)
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
    if ($ContractFixture) {
        $texts = @(
            'MASTER SERVICES AGREEMENT - Page 1 of 2|Synthetic contract for viewer and agent verification.|1. Parties and scope|Northwind Services supplies support to Redwood Labs.|Services are described in written statements of work.|2. Indemnity|Supplier indemnifies Customer for third-party IP claims.|Customer must promptly notify Supplier of any claim.|3. Liability limits|Aggregate liability is limited to fees paid in 12 months.|Neither party is liable for consequential damages.|4. Payment|Invoices are payable within 30 days.|Disputed charges must be identified in writing.|5. Delivery|Supplier will provide monthly service reports.|Reports must identify completed and pending tasks.|6. Records|Each party keeps accurate records of its obligations.|Records may be reviewed on reasonable written notice.|Continued on page 2.',
            'MASTER SERVICES AGREEMENT - Page 2 of 2|7. Renewal|The agreement renews annually unless notice is given.|Non-renewal requires 60 days written notice.|8. Termination|Either party may terminate for material breach.|The breaching party has 30 days to cure after notice.|9. Non-compete|No non-compete restriction is agreed in this example.|10. Confidentiality|Each party protects the other party confidential data.|Access is limited to personnel who need the information.|11. Assignment|Assignment requires prior written consent.|12. Notices|Notices must be sent to the designated contact.|13. Amendments|Changes require a writing signed by both parties.|14. Governing terms|The signed statement of work identifies the jurisdiction.|15. Signatures|Authorized representatives may sign electronically.|End of master services agreement.',
            'NON-DISCLOSURE AGREEMENT - Page 1 of 1|Separate synthetic agreement for viewer verification.|1. Purpose|The parties will discuss a possible services engagement.|2. Confidential information|Non-public commercial information must remain confidential.|3. Permitted disclosure|Information may be shared with authorized advisers.|4. Return of information|Materials must be returned or destroyed on written request.|5. Duration|The confidentiality obligation lasts for two years.|6. No license|Disclosure does not grant a license to intellectual property.'
        )
    }
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
        $result.documents[0].markdown -notmatch $(if ($ContractFixture) { 'MASTER SERVICES AGREEMENT' } else { 'INVOICE 1842' }) -or
        $result.documents[0].markdown -notmatch '<!-- page break -->' -or
        $result.documents[1].markdown -notmatch $(if ($ContractFixture) { 'NON-DISCLOSURE AGREEMENT' } else { 'COMMUNITY BOOK FAIR' })) {
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

    if ($VerifyPageSections) {
        $source = Invoke-WebRequest "$origin/api/v1/uploads/$id/source" -Headers $owner
        if ([Convert]::ToBase64String($source.Content) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes($fixture))) { throw 'Original source bytes changed.' }
        if ((Invoke-WebRequest "$origin/api/v1/uploads/$id/source" -Headers $other -SkipHttpErrorCheck).StatusCode -ne 404) { throw 'Original source leaked to another owner.' }
        if ((Invoke-WebRequest "$origin/api/v1/uploads/$id/source" -SkipHttpErrorCheck).StatusCode -ne 401) { throw 'Original source allowed anonymous access.' }
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        do {
            $result = Invoke-RestMethod $path -Headers $owner
            if ($result.pages.analysisStatus -contains 'failed') { throw "Page classification failed for upload $id. Inspect LLMPrompts in Admin." }
            if ($result.pages.Count -eq 3 -and @($result.pages | Where-Object analysisStatus -NE 'ready').Count -eq 0) { break }
            Start-Sleep -Seconds 3
        } while ([DateTime]::UtcNow -lt $deadline)
        if (@($result.pages | Where-Object analysisStatus -NE 'ready').Count -gt 0) { throw 'Page analysis did not complete.' }
        foreach ($page in $result.pages) {
            if (!$page.sections.Count) { throw "No sections found on synthetic page $($page.number)." }
            foreach ($section in $page.sections) {
                if (!$section.summary -or !$page.markdown.Contains($section.quote)) { throw 'Section evidence or summary is invalid.' }
            }
            $deadline = [DateTime]::UtcNow.AddSeconds(15)
            do {
                $runs = (Invoke-RestMethod "${operations}?entityType=OcrPage&entityId=$($page.id)" -Headers $adminHeaders).items
                $pageRuns = $runs | Where-Object { $_.state -eq 'Succeeded' }
                if ($pageRuns) { break }; Start-Sleep -Seconds 1
            } while ([DateTime]::UtcNow -lt $deadline)
            if (!$pageRuns) { throw 'Page step has no successful Admin execution.' }
        }
        $pagePrompts = (Invoke-RestMethod 'http://localhost:13088/admin/v1/entities/LLMPrompt?f.DocumentType=OcrPage&take=100' -Headers $adminHeaders).items |
            Where-Object { $_.DocumentID -in $result.pages.id }
        if (@($pagePrompts).Count -ne 3 -or @($pagePrompts | Where-Object { $_.ProcessingStatus -ne 'Processed' -or !$_.UsedModel }).Count) {
            throw 'Admin must show three processed page prompts with a linked used model.'
        }
        if ($ExpectedPageModel) {
            foreach ($pagePrompt in $pagePrompts) {
                $usedModel = Invoke-RestMethod "http://localhost:13088/admin/v1/entities/LLMModel/$($pagePrompt.UsedModel.id)" -Headers $adminHeaders
                if ($usedModel.Name -ne $ExpectedPageModel -or ($ExpectedPageModel -like 'muse-*' -and $usedModel.Provider -ne 'Meta') -or ($ExpectedPageModel -like 'subq-*' -and $usedModel.Provider -ne 'SubQ')) {
                    throw "Page prompt $($pagePrompt.ID) did not resolve through the expected model/provider."
                }
            }
            Write-Host "Verified all three page prompts used $ExpectedPageModel. Prompt IDs: $($pagePrompts.ID -join ', ')."
        }
        Write-Host "Verified source-byte fidelity/ownership and three completed native page-section workflows for upload $id."
    }
    if ($KeepTestSession) {
        @{email=$first.email; password=$password; uploadId=$id} | ConvertTo-Json | Set-Content "$PSScriptRoot/../artifacts/page-smoke-session.local.json"
    }

    Write-Host "Verified Firebase sign-up and sign-in, upload $id, user isolation, Mistral OCR, document ranges 1–2 and 3, and Admin telemetry."
} finally {
    if (Test-Path -LiteralPath $fixture) { Remove-Item -LiteralPath $fixture }
}


