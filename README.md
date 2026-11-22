# Chubb Claims Fullstack Assessment

The Angular workspace serves claimants and claims staff. The .NET Claims API owns the claims workflow, and the Notifications API consumes claim events from Kafka. SignalR keeps open views up to date. Each service owns a SQLite database. You can also demonstrate the backend directly over HTTP.

## Run the full stack locally

You need PowerShell 7, the .NET 10 SDK, Node.js 24.15 or later in the Node 24 series, and Docker with Compose.

1. From the repository root, trust the development HTTPS certificate using the commands below.
2. Follow [Run Kafka notifications](#run-kafka-notifications) to start Kafka and both APIs. Use that section's Claims API command for the full stack so both services use the same signing key.
3. Follow [Run the Angular workspace](#run-the-angular-workspace) to install packages, configure Node's certificate trust, and start the UI. Use the demo password generated in step 2.
4. Run the [sample claim script](#sample-claims-and-staff-reports), then open [Claims Workspace](http://127.0.0.1:4200/).

See the [panel walkthrough](PANEL-WALKTHROUGH.md) for the demo sequence, trade-offs, and deferred work. The [AI journal](AI-JOURNAL.md) records the development decisions and verification.

## Run the Claims API alone

Use the .NET 10 SDK. Run these commands from the repository root in PowerShell 7:

```powershell
dotnet dev-certs https --check --trust
if ($LASTEXITCODE -ne 0) { dotnet dev-certs https --trust }
$env:Demo__Password = [guid]::NewGuid().ToString('N')
dotnet run --project backend/Claims.Api
```

Open `https://localhost:7164/swagger/` to explore the Claims API. SQLite migrations run at startup. Development accounts use the password set in `Demo__Password`. Keep the first terminal open and copy the variable to the second PowerShell session. Alternatively, start both sessions from a shell with the variable already set.

| Account | Role |
| --- | --- |
| `claimant@example.test` and `other-claimant@example.test` | Claimant |
| `staff@example.test`, `other-staff@example.test`, and `third-staff@example.test` | ClaimsStaff |

The default database is `backend/Claims.Api/claims.db`. Override it with `ConnectionStrings__Claims`. The development JWT key is random per process by default, so restarting invalidates outstanding tokens. Set a Base64-encoded random key of at least 32 bytes in `Jwt__SigningKey` to keep tokens valid across restarts. Outside Development, the API requires a configured key and does not seed demo accounts.

If Windows has multiple localhost certificates and Kestrel selects an untrusted one, stop the API and run the following in the server terminal before restarting. This uses the localhost certificate from your user store. Verify that both Swagger pages open over HTTPS before starting Angular.

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

Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/approve" -Headers $staff -ContentType 'application/json' -Body '{"amount":850,"currency":"MYR"}'
Invoke-RestMethod -Method Post -Uri "$base/claims/${id}/settle" -Headers $staff
Invoke-RestMethod -Uri "$base/claims/${id}" -Headers $claimant
Invoke-RestMethod -Uri "$base/claims/${id}/history" -Headers $claimant
```

Pickup moves the claim from `Submitted` to `UnderReview`. A question moves it to `AwaitingInformation` and the owner's reply returns it to `UnderReview`. The cycle can repeat. To demo rejection, create another claim and call `POST /claims/{id}/reject` with a JSON body such as `{"reason":"The assessment found that the damage occurred before the policy start date."}`. Only assigned staff can reject a claim. Approval and settlement are separate actions. Settlement records the outcome of the claim in the API.

The claimant sees the decision reason and Chubb Malaysia Customer Service contact options on the rejected claim. For older rejected claims, assigned staff can add a missing reason once from the review screen or by calling `PUT /claims/{id}/rejection-reason` with the same body. The service records this action without sending a second decision notification. [Chubb Malaysia Customer Service](https://www.chubb.com/my-en/contact-us.html)

The conversation is readable by the claimant who owns the claim and the assigned staff member. History records the actor, action, time, and status change, and links questions and replies to their message IDs. Staff can see unassigned claims and claims assigned to them. The list supports `status`, `assignedStaffId` (staff only), and `page` (50 results per page by default).

For `GET /claims` and `GET /notifications`, set `pageSize` (1–500) to receive `X-Total-Count` for the filtered, authorized records. Claim history and messages enable paging and return the count when `page` or `pageSize` is supplied. Page 1 contains the latest activity, with messages in chronological order within a page. For messages, `throughMessageId` can keep older pages stable while new messages arrive. Existing requests without the new parameters keep their original array responses.

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

Open [Claims Workspace](http://127.0.0.1:4200/). Sign in as the demo claimant and submit a claim. In a separate browser profile or private window, sign in as staff and select **Accept for review** in the claim's row in **Staff queue**. Open the claim's thread from **Conversations** and send a question. The claimant can open the same thread from their **Conversations** menu or question notification. Both sides receive new messages live. Select **View claim** to return to the review and audit history.

To demo pickup contention, display the same unassigned row in two staff queues using separate browser profiles. The first successful **Accept for review** wins, and the claim drops out of the other staff member's queue. If a stale client still submits pickup, the API returns a `409 Conflict` with an explanation.

The API commits claim changes before publishing SignalR updates. SignalR tells the browser when to fetch the latest data through the authenticated REST endpoints. The notification service sends its SignalR update after committing the notification. Reconnecting clients reload the current state. Claims updates use `/api/claims/hubs/claims` through the Angular proxy, and claimant notifications use `/api/live/notifications`.

The Angular dev server listens on loopback and proxies API requests over HTTPS with certificate validation. The browser session uses a Secure, HttpOnly, SameSite=Strict cookie and survives a reload until the 20-minute session expires or the user signs out. Tabs in the same browser profile share the session. Swagger and scripts still use bearer authentication. When deploying behind a same-origin reverse proxy, set `BrowserSession__Origin` on both APIs to the exact public HTTPS origin for browser SignalR connections. Angular route guards handle navigation. The APIs enforce roles and record ownership. To build the Angular app, run `npm run build`. A deployed static host would need HTTPS, SPA routing, the same API path mappings as the development proxy, and WebSocket upgrade support for the SignalR routes.

## Sample claims and staff reports

With the Claims API running and `Demo__Password` set to the demo password, run `.\scripts\seed-demo-claims.ps1` from the repository root. The script creates ten fictional motor and property claims across both claimant accounts. It uses the regular API workflow. On a rerun, it skips matching examples and fills in the known fictional reason if the sample rejected claim was created before reasons were supported.

Both claim lists open on **Submitted**. Use the status tabs to see other statuses or **All claims**. Conversations has its own menu and shows threads across statuses. Claim lists, messages, history, notifications, and the staff activity table default to 10 items per page. Use **Items per page** to choose 10, 25, 50, 100, 250, or 500. Changing the size returns to page 1. Conversations open on the latest messages, and **Older** and **Newer** navigate the thread. PDF export covers the full filtered report.

Sign in as `staff@example.test` and open **Dashboard** for all-time status counts, requested amounts, approved amounts, and open exposure. Amounts use their recorded currencies. Staff records an approved amount when confirming approval. Claimants can enter an optional requested amount when filing. Older submissions or approvals without an amount remain visible in status counts.

Open **Reports** and choose 7, 30, or 90 days or apply a custom start and end date. Both UTC dates are inclusive. Counts, amounts, and current assignments reflect claims submitted in that range. The activity table counts decisions and settlements on those claims during the selected period. **Export PDF** downloads the filtered report. The Claims API accepts `GET /reports/overview?startDate=YYYY-MM-DD&endDate=YYYY-MM-DD`. The existing `?days=30` query retains its all-claims overview and rolling staff activity window.

Assigned staff can add or edit a claim's **Exposure estimate** on its review screen. The report totals recorded estimates for open claims by currency and shows how many open claims lack an estimate. Settled and rejected claims are excluded. Claimants can see their claim progress and conversation.

## Verification

Run `dotnet test` from the repository root. Integration tests use the HTTP pipeline and temporary SQLite databases. They cover login, validation, role and ownership checks, approval and settlement, rejection, invalid transitions, and competing pickups and questions. The claim version guards concurrent updates. EF Core commits each workflow change, history entry, and any related message together.

With the Compose broker running, set `$env:KAFKA_TEST_BOOTSTRAP_SERVERS = '127.0.0.1:9092'` before running `dotnet test`. This enables the end-to-end Kafka and SignalR test for questions, approval, rejection, settlement, replay, and claimant isolation. It uses a temporary topic and consumer group. The test suite also checks queue broadcasts, pickup conflicts, and claim access after a competing pickup.

## Try the APIs in Swagger

Both APIs serve Swagger UI in the Development environment.

| API | Swagger UI | OpenAPI JSON |
| --- | --- | --- |
| Claims | [Claims Swagger](https://localhost:7164/swagger/) | [Claims spec](https://localhost:7164/openapi/v1.json) |
| Notifications | [Notifications Swagger](https://localhost:7165/swagger/) | [Notifications spec](https://localhost:7165/openapi/v1.json) |

1. In Claims Swagger, expand `POST /auth/login` and select **Try it out**. Enter your demo email and the password you set at startup, then execute the request.
2. Copy the returned `accessToken`. Select **Authorize** in the Swagger page you want to use and paste the token alone. Swagger adds the `Bearer` prefix.
3. Try `GET /claims` in Claims Swagger or `GET /notifications` in Notifications Swagger. The notifications endpoint requires a claimant token. Authorize each Swagger page separately.
