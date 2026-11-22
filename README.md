# Chubb Claims Fullstack Assessment

The ASP.NET Core API supports JWT login and the claims workflow from intake through review and settlement or rejection. It uses EF Core with SQLite. Kafka notifications and the Angular app are later milestones.

## Run locally

Use the .NET 10 SDK. Run these commands from the repository root in PowerShell:

```powershell
dotnet dev-certs https --trust
$env:Demo__Password = [guid]::NewGuid().ToString('N')
dotnet run --project backend/Claims.Api
```

Open `https://localhost:7164/openapi/v1.json` for the development API contract. SQLite migrations run at startup. Development accounts use the password set in `Demo__Password`. Keep the first terminal open and copy the variable to the second PowerShell session. Alternatively, start both sessions from a shell with the variable already set.

| Account | Role |
| --- | --- |
| `claimant@example.test` and `other-claimant@example.test` | Claimant |
| `staff@example.test` and `other-staff@example.test` | ClaimsStaff |

The default database is `backend/Claims.Api/claims.db`. Override it with `ConnectionStrings__Claims`. The development JWT key is random per process by default, so restarting invalidates outstanding tokens. Set a Base64-encoded random key of at least 32 bytes in `Jwt__SigningKey` to keep tokens valid across restarts. Outside Development, the API requires a configured key and does not seed demo accounts.

If Windows has multiple localhost certificates and Kestrel selects an untrusted one, stop the API and run the following in the server terminal before restarting. This selects a valid certificate from your user store.

```powershell
$env:Kestrel__Certificates__Default__Subject = 'localhost'
$env:Kestrel__Certificates__Default__Store = 'My'
$env:Kestrel__Certificates__Default__Location = 'CurrentUser'
```

## Demo

With `Demo__Password` set to the same value in the second session:

```powershell
$base = 'https://localhost:7164'
function Get-ClaimsToken($email) {
    $body = @{ email = $email; password = $env:Demo__Password } | ConvertTo-Json
    (Invoke-RestMethod -Method Post -Uri "$base/auth/login" -ContentType 'application/json' -Body $body).accessToken
}
$claimant = @{ Authorization = "Bearer $(Get-ClaimsToken 'claimant@example.test')" }
$staff = @{ Authorization = "Bearer $(Get-ClaimsToken 'staff@example.test')" }

$body = @{
    type = 'Motor'
    incidentDate = [DateTime]::UtcNow.AddDays(-1).ToString('yyyy-MM-dd')
    description = 'Rear bumper damaged while parked.'
} | ConvertTo-Json

$claim = Invoke-RestMethod -Method Post -Uri "$base/claims" -Headers $claimant -ContentType 'application/json' -Body $body
$id = $claim.id
Invoke-RestMethod -Uri "$base/claims/${id}" -Headers $claimant
Invoke-RestMethod -Uri "$base/claims?status=Submitted" -Headers $staff
Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/pick-up" -Headers $staff

$question = @{ body = 'Where was the car parked?' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/questions" -Headers $staff -ContentType 'application/json' -Body $question
$reply = @{ body = 'Outside my home.' } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/replies" -Headers $claimant -ContentType 'application/json' -Body $reply
Invoke-RestMethod -Uri "$base/claims/${id}/messages" -Headers $claimant

Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/approve" -Headers $staff
Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/settle" -Headers $staff
Invoke-RestMethod -Uri "$base/claims/${id}" -Headers $claimant
Invoke-RestMethod -Uri "$base/claims/${id}/history" -Headers $claimant
```

Pickup moves the claim from `Submitted` to `UnderReview`. A question moves it to `AwaitingInformation` and the owner's reply returns it to `UnderReview`. The cycle can repeat. To demo rejection, create another claim and call `/reject` while it is under review. Approval and settlement are separate actions. Settlement records the outcome of the claim in the API.

The conversation is readable by the claimant who owns the claim and the assigned staff member. History records the actor, action, time, and status change, and links questions and replies to their message IDs. Staff can see unassigned claims and claims assigned to them. The list supports `status`, `assignedStaffId` (staff only), and `page` (50 results per page).

## Verification

Run `dotnet test` from the repository root. Integration tests use the HTTP pipeline and temporary SQLite databases. They cover login, validation, role and ownership checks, approval and settlement, rejection, invalid transitions, and competing pickups and questions. The claim version guards concurrent updates. EF Core commits each workflow change, history entry, and any related message together.
