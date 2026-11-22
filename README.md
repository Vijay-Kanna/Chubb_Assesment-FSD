# Chubb Claims Fullstack Assessment

The first milestone is an ASP.NET Core API for JWT login, claim submission and tracking, and staff pickup. It uses EF Core with SQLite. The longer review workflow, Kafka notification service, and Angular app are later milestones.

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
Invoke-RestMethod -Uri "$base/claims/${id}" -Headers $claimant
Invoke-RestMethod -Uri "$base/claims/${id}/history" -Headers $claimant
```

The claimant first sees `Submitted`, then `UnderReview` after pickup. History records submission and assignment. Staff can see unassigned claims and claims assigned to them. The list supports `status`, `assignedStaffId` (staff only), and `page` (50 results per page).

## Verification

Run `dotnet test` from the repository root. Integration tests use the HTTP pipeline and temporary SQLite databases. They cover login, JWT rejection, role and claim ownership checks, claim validation, competing pickups, and history. Pickup uses optimistic concurrency so only one request can assign the claim. EF Core commits the status and history entry together.
