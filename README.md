# Chubb Claims Fullstack Assessment

The Angular workspace serves claimants and claims staff. The .NET Claims API owns the claims workflow, and the Notifications API delivers in-app notifications over Kafka. Each service owns a SQLite database. You can also demonstrate the backend directly over HTTP.

## Run locally

Use the .NET 10 SDK. Run these commands from the repository root in PowerShell:

```powershell
dotnet dev-certs https --trust
$env:Demo__Password = [guid]::NewGuid().ToString('N')
dotnet run --project backend/Claims.Api
```

Open `https://localhost:7164/swagger/` to explore the Claims API. SQLite migrations run at startup. Development accounts use the password set in `Demo__Password`. Keep the first terminal open and copy the variable to the second PowerShell session. Alternatively, start both sessions from a shell with the variable already set.

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

## Run Kafka notifications

Start Docker, then run these commands from the repository root in PowerShell:

```powershell
docker compose up -d --wait
$env:Kafka__BootstrapServers = '127.0.0.1:9092'
$env:Jwt__SigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
$env:Demo__Password = [guid]::NewGuid().ToString('N')
dotnet run --project backend/Claims.Api
```

In a second terminal, set `Kafka__BootstrapServers` and `Jwt__SigningKey` to the **same values** and run `dotnet run --project backend/Notifications.Api`. Use the same issuer and audience settings if you override them. If Windows requires the certificate setup above, apply it in both service terminals. Run the claims demo, then use the claimant's bearer token to read their notifications:

```powershell
Invoke-RestMethod -Uri 'https://localhost:7165/notifications' -Headers $claimant
```

Allow a few seconds for delivery. There is a notification for a staff question, approval, rejection, and settlement. The API returns only the signed-in claimant's notifications. The default database is `backend/Notifications.Api/notifications.db` and can be overridden with `ConnectionStrings__Notifications`.

The Claims API stores each event in an outbox with the corresponding claim change. Its publisher retries pending events after broker failures. The Notifications API saves the notification before committing its Kafka offset. The event ID is the notification's primary key, so replay does not create another row. Kafka events carry identifiers, type, and timestamp. Claim descriptions and conversation text stay in the Claims API.

The Compose broker listens on loopback for local development. Both APIs use a shared JWT signing key for this demo. `docker compose stop` stops the broker while retaining its local data.

## Run the Angular workspace

Use Node.js 24.15 or later in the Node 24 series. Start the Claims API using the instructions above. To see notifications, also start the broker and Notifications API with the shared JWT key as described in [Run Kafka notifications](#run-kafka-notifications).

In another PowerShell terminal, run:

```powershell
Set-Location frontend/claims-web
npm ci
$env:NODE_EXTRA_CA_CERTS = Join-Path ([IO.Path]::GetTempPath()) 'chubb-claims-dev.pem'
dotnet dev-certs https --format PEM --export-path $env:NODE_EXTRA_CA_CERTS
npm start
```

The export contains the public certificate. If the APIs use a different trusted localhost certificate from the Windows user store, export the trusted certificate used by both services to the same PEM path before restarting Angular:

```powershell
$trusted = Get-ChildItem -LiteralPath 'Cert:\CurrentUser\Root' | Where-Object {
    $_.Subject -eq 'CN=localhost' -and
    $_.FriendlyName -eq 'ASP.NET Core HTTPS development certificate' -and
    $_.NotAfter -gt (Get-Date)
}
[IO.File]::WriteAllText($env:NODE_EXTRA_CA_CERTS, (($trusted | ForEach-Object {
    $_.ExportCertificatePem()
}) -join [Environment]::NewLine))
```

Open [Claims Workspace](http://127.0.0.1:4200/). Sign in as the demo claimant and submit a claim. In a second tab, sign in as staff, open the claim from the queue, pick it up, and send a question. In the claimant tab, refresh the claim and reply. Refresh the staff review before recording a decision. The claimant's notification inbox refreshes while its first page is open.

The Angular dev server listens on loopback and proxies API requests over HTTPS with certificate validation. The app keeps the bearer token in memory, so a page reload requires signing in again. Angular route guards handle navigation. The APIs enforce roles and record ownership. To build the Angular app, run `npm run build`. A deployed static host would need HTTPS, SPA routing, and the same API path mappings as the development proxy.

## Verification

Run `dotnet test` from the repository root. Integration tests use the HTTP pipeline and temporary SQLite databases. They cover login, validation, role and ownership checks, approval and settlement, rejection, invalid transitions, and competing pickups and questions. The claim version guards concurrent updates. EF Core commits each workflow change, history entry, and any related message together.

With the Compose broker running, set `$env:KAFKA_TEST_BOOTSTRAP_SERVERS = '127.0.0.1:9092'` before running `dotnet test`. This enables the end-to-end Kafka test for questions, approval, rejection, settlement, replay, and claimant isolation. It uses a temporary topic and consumer group.

## Try the APIs in Swagger

Both APIs serve Swagger UI in the Development environment.

| API | Swagger UI | OpenAPI JSON |
| --- | --- | --- |
| Claims | [Claims Swagger](https://localhost:7164/swagger/) | [Claims spec](https://localhost:7164/openapi/v1.json) |
| Notifications | [Notifications Swagger](https://localhost:7165/swagger/) | [Notifications spec](https://localhost:7165/openapi/v1.json) |

1. In Claims Swagger, expand `POST /auth/login` and select **Try it out**. Enter your demo email and the password you set at startup, then execute the request.
2. Copy the returned `accessToken`. Select **Authorize** in the Swagger page you want to use and paste the token alone. Swagger adds the `Bearer` prefix.
3. Try `GET /claims` in Claims Swagger or `GET /notifications` in Notifications Swagger. The notifications endpoint requires a claimant token. Authorize each Swagger page separately.
