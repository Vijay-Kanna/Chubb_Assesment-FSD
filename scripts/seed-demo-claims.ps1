[CmdletBinding()]
param(
    [string] $Password = $env:Demo__Password,
    [Uri] $ApiBase = 'https://localhost:7164'
)

$ErrorActionPreference = 'Stop'
if (-not $Password) { throw 'Set Demo__Password or pass the local demo password to -Password.' }
if (-not $ApiBase.IsLoopback -or $ApiBase.Scheme -ne 'https') { throw 'Use the local HTTPS Claims API.' }
$base = $ApiBase.AbsoluteUri.TrimEnd('/')

function Invoke-ClaimsRequest($method, $path, $headers, $data) {
    $arguments = @{ Method = $method; Uri = "$base$path" }
    if ($headers) { $arguments.Headers = $headers }
    if ($null -ne $data) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $data | ConvertTo-Json -Compress
    }
    Invoke-RestMethod @arguments
}

$accounts = @{}
foreach ($name in 'claimant', 'other-claimant', 'staff', 'other-staff') {
    $session = Invoke-ClaimsRequest 'Post' '/auth/login' $null @{ email = "$name@example.test"; password = $Password }
    $accounts[$name] = @{ Authorization = "Bearer $($session.accessToken)" }
}

$samples = @(
    @{ Owner = 'claimant'; Type = 'Motor'; Age = 2; Description = 'Demo - Rear bumper damage in a shopping centre car park.'; Status = 'Submitted' }
    @{ Owner = 'claimant'; Type = 'Property'; Age = 5; Description = 'Demo - Kitchen ceiling damaged by a leak from upstairs.'; Status = 'AwaitingInformation'; Staff = 'staff'; Amount = 5400; Currency = 'MYR'; Question = 'When did you first notice the water damage?' }
    @{ Owner = 'claimant'; Type = 'Motor'; Age = 7; Description = 'Demo - Side mirror damaged in a parking incident.'; Status = 'UnderReview'; Staff = 'staff'; Amount = 1500; Currency = 'MYR' }
    @{ Owner = 'claimant'; Type = 'Property'; Age = 11; Description = 'Demo - A power surge damaged two appliances.'; Status = 'Approved'; Staff = 'other-staff'; Amount = 1200; Currency = 'SGD' }
    @{ Owner = 'claimant'; Type = 'Motor'; Age = 16; Description = 'Demo - Front fender damage after an intersection collision.'; Status = 'Settled'; Staff = 'staff'; Amount = 3200; Currency = 'MYR' }
    @{ Owner = 'claimant'; Type = 'Motor'; Age = 19; Description = 'Demo - Windscreen damage reported after a previous repair.'; Status = 'Rejected'; Staff = 'other-staff'; Amount = 600; Currency = 'MYR' }
    @{ Owner = 'other-claimant'; Type = 'Property'; Age = 4; Description = 'Demo - Storm runoff damaged a utility room.'; Status = 'UnderReview'; Staff = 'other-staff'; Amount = 2600; Currency = 'SGD' }
    @{ Owner = 'other-claimant'; Type = 'Motor'; Age = 1; Description = 'Demo - Passenger door dent discovered after street parking.'; Status = 'Submitted' }
)

$known = @{}
foreach ($name in 'claimant', 'other-claimant') {
    for ($page = 1; $page -le 10000; $page++) {
        $claims = Invoke-ClaimsRequest 'Get' "/claims?page=$page" $accounts[$name] $null
        foreach ($claim in $claims) { $known["$name|$($claim.description)"] = $claim }
        if ($claims.Count -lt 50) { break }
    }
}

foreach ($sample in $samples) {
    $claimant = $accounts[$sample.Owner]
    $existing = $known["$($sample.Owner)|$($sample.Description)"]
    if ($existing) {
        [pscustomobject]@{ Id = $existing.id; Owner = $sample.Owner; Status = $existing.status; Result = 'Existing' }
        continue
    }

    $claim = Invoke-ClaimsRequest 'Post' '/claims' $claimant @{
        type = $sample.Type
        incidentDate = [DateTime]::UtcNow.AddDays(-$sample.Age).ToString('yyyy-MM-dd')
        description = $sample.Description
    }
    if ($sample.Status -ne 'Submitted') {
        $staff = $accounts[$sample.Staff]
        $path = "/claims/$($claim.id)"
        $claim = Invoke-ClaimsRequest 'Post' "$path/pick-up" $staff $null
        if ($null -ne $sample.Amount) {
            $null = Invoke-ClaimsRequest 'Put' "$path/exposure" $staff @{ amount = $sample.Amount; currency = $sample.Currency }
        }
        switch ($sample.Status) {
            'AwaitingInformation' { $claim = Invoke-ClaimsRequest 'Post' "$path/questions" $staff @{ body = $sample.Question } }
            'Approved' { $claim = Invoke-ClaimsRequest 'Post' "$path/approve" $staff $null }
            'Rejected' { $claim = Invoke-ClaimsRequest 'Post' "$path/reject" $staff $null }
            'Settled' {
                $null = Invoke-ClaimsRequest 'Post' "$path/approve" $staff $null
                $claim = Invoke-ClaimsRequest 'Post' "$path/settle" $staff $null
            }
        }
    }
    [pscustomobject]@{ Id = $claim.id; Owner = $sample.Owner; Status = $claim.status; Result = 'Created' }
}
