# AI Working Journal

## Understanding the brief and shaping the design

I asked Codex to compare the three Chubb assessment briefs, explain the Fullstack requirements, and help me plan a backend-first submission. I approved a design with .NET services, an Angular app if time permits, JWT login and roles, and Kafka for in-app notifications after the claims workflow is working. I asked for OWASP-aligned security and explicit SQL injection prevention.

I challenged the initial design because it left the core models open. I specified `User`, `Role`, `Claim`, and `ClaimHistory`. I clarified that both roles should use the same user model, a claim needs a current status, and the assigned staff member needs to ask the claimant questions before approving or rejecting. Codex proposed a child `ClaimMessage` record for the conversation, a separate audit history, and two steps for approval and settlement. I approved the reviewed design and asked Codex to implement the initial API milestone before the later workflow and services.

## First backend milestone

I asked Codex to build login, roles, claim submission, claimant access to their own claims, and staff pickup. I also asked it to verify the result locally, update this journal, and make a meaningful local Git commit.

Codex generated the .NET API, SQLite migration, HTTP integration tests, and demo steps. It used a development-only seed with a password supplied at runtime and an ephemeral development JWT key. It implemented an optimistic concurrency check for pickup and saved claim updates with their audit entries. The tests check that competing staff requests produce one assignment and that access respects roles and claim ownership.

Codex ran six passing integration tests and exercised the login, submission, queue, pickup, claimant status, and history endpoints on the local HTTPS server. It found that Kestrel initially selected an untrusted localhost certificate on this machine. It selected the existing trusted certificate from the user store and documented that setup in the README. The NuGet advisory check reported no known vulnerable packages for the configured source.

## Claim conversation and decisions

I asked Codex to add staff questions, claimant replies, approval or rejection, and claim history. I specifically asked it to verify the full workflow and make sure users can access only claims they are authorized to see.

Codex implemented the reviewed design with a separate conversation record and audit history. The assigned staff member can ask a question, the claim owner can reply, and each action moves the claim to the next allowed status. Approval and settlement are separate actions. Each question or reply is committed with the status change and a history entry that links to the message.

The generated SQLite migration initially needed to rebuild the history table. Codex revised it to add the nullable message reference in place. Ten integration tests passed, including permission checks, both decision paths, and a concurrent question test. Codex also ran both decision paths over local HTTPS on an upgraded copy of the first milestone's database and verified that the original claim and history were preserved.

## Kafka notifications

I asked Codex to add `Notifications.Api` and its Kafka consumer, verify that a staff question or decision reaches the correct claimant, update this journal, and commit the milestone.

Codex implemented the transactional outbox from the reviewed design. The Claims API commits each event with the claim change. Its worker publishes pending events to Kafka. The Notifications API saves each notification before committing the offset. Codex used the event ID as the notification's primary key so a replay cannot create a duplicate. The services share a JWT signing key for the local demo, and the notifications endpoint derives the recipient from the validated token.

Codex ran 12 passing tests with a real local Kafka broker, covering question, approval, rejection, and settlement notifications, recipient isolation, and replay of the original question event. It also checked the NuGet dependencies against the configured advisory source.

## Angular workspace

I asked Codex to build one Angular app for claimants and staff. I prioritized login, claim tracking and replies, and the staff queue and review screen. I asked for a modern, responsive interface, security, verification, and a local Git commit.

Codex used Angular 22 standalone routes, signals, and Signal Forms. It reused the claim detail screen for both roles and showed actions according to role, assignment, and status. The app proxies same-origin API requests over HTTPS and keeps the JWT in memory. A page reload requires signing in again. The APIs enforce access independently of the UI. Codex fixed a local HTTPS certificate mismatch by configuring Node to trust the APIs' public development certificate.

Codex exercised both roles in the browser against the running APIs and Kafka. It verified submission, pickup, a staff question, the claimant reply, approval and settlement, a separate rejection, and the claimant notifications. It checked the desktop review layout, mobile widths of 320 and 390 pixels, and that sign-in left browser storage empty.

## Swagger and the running demo

I asked Codex to run the .NET services, Angular, and Kafka, and to add Swagger. Codex kept ASP.NET Core's OpenAPI generation and added Swagger UI to each service for Development. The generated contracts mark protected operations with JWT bearer authentication. The login endpoint stays public and documented with its request and response.

Codex verified both Swagger pages in the browser, confirmed that protected operations reject anonymous requests, and used a claimant token to reach both APIs through Angular's proxy. All 12 backend tests passed with Kafka running. Codex documented Swagger sign-in and left the local stack running.

## Demo claims and staff reporting

I asked Codex to add sample claims, test the app, add a password visibility button, and build a reporting module. Codex added eight fictional claims through the API and checked that rerunning the seed script skipped matching records.

Codex added a staff-only report for claim status, staff workload and decisions, and outstanding exposure. It modeled exposure as an optional, currency-specific estimate entered by the assigned staff member. The report groups the estimates for open claims by currency and counts claims with missing estimates. The claim owner still sees their workflow history. Staff estimate changes appear in staff history.

Codex ran 13 passing backend tests with Kafka, built Angular, and tested the password toggle, staff reports, estimate editing, and seeded claimant notifications in the browser. The tests verified that a claimant cannot read staff reports or estimates, that unrelated staff cannot edit estimates, and that a closed claim's estimate does not contribute to outstanding exposure.

## Claim rejection review

During submission review, I pointed out that the claimant would see a rejection without a reason or a customer service contact. I asked Codex to improve that experience. Codex made a claimant-facing reason required and added a decision panel with phone and email links for Chubb Malaysia Customer Service.

Codex kept the reason on the protected claim, with only the claim reference and event metadata sent through Kafka. Assigned staff can record a missing reason once for an older rejection. The demo seed uses that path to backfill the existing fictional sample. Codex tested the required reason, preservation of the recorded decision, and both roles in the browser. It confirmed the claimant could open the rejected claim from the notification and see the recorded explanation.

## Live updates and competing pickups

I asked Codex to replace manual refresh with SignalR for messaging, the queue, and claim requests. I also asked it to make sure staff cannot take a claim that someone else picked up, to improve the error messages, and to add a third staff login.

Codex kept REST for reading and writing authorized claim data. SignalR carries a change signal so open pages fetch the latest state. Kafka still delivers events to the Notifications API. Codex reused the claim version check to enforce one successful pickup and added clear messages for a stale pickup attempt and for a staff member whose open claim is no longer available.

Codex ran 14 passing backend tests with Kafka and tested three staff sessions and a claimant in the browser. The queue updated when a claim arrived and when it was picked up. A competing staff member could not reopen it, and a stale pickup received a `409 Conflict`. The staff question, claimant reply, and approval notification appeared without a refresh.
