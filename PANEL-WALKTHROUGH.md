# Chubb Fullstack panel walkthrough

Use an approximately **45-minute** agenda. Spend 18 minutes presenting, 12 minutes on panel Q&A, 10 minutes on future work, and 5 minutes on your questions. The brief allows 30–60 minutes overall.

## Before the call

- Follow the [local setup](README.md#run-the-full-stack-locally) and seed the fictional examples. Verify that both Swagger pages and the Angular app load.
- Use separate browser profiles for `claimant@example.test` and `staff@example.test`. Use a third isolated profile with `other-staff@example.test` if you want to show competing acceptance. Accounts share the password configured when starting the Claims API. Browser tabs in the same profile share a session.
- Keep the [AI journal](AI-JOURNAL.md), Git log, and GitHub repository open. Use fictional claims and avoid showing credentials.

## Presentation and demo · 18 minutes

| Time | Show | Explain |
| --- | --- | --- |
| 2 min | First four Git milestones and the AI journal | I started with the claims domain, access checks, and workflow. I added Kafka after the synchronous workflow worked, then built one Angular app for both roles. |
| 4 min | Architecture and data model | The Claims API owns users, roles, claims, messages, history, and the outbox. The Notifications API owns its own store and consumes Kafka events. The services use separate SQLite databases. REST returns immediately for actions that need a clear answer. Kafka delivers notices asynchronously. SignalR tells the browser to re-fetch authorized data. |
| 7 min | A new motor claim, staff acceptance, question, claimant reply, approval, then settlement | Use **Accept for review**, open chat from the row or confirmation, and send a question. The claimant sees the notification and replies. Show the audit history and record an approved amount before settlement. Approval is a decision. Settlement is a separate workflow action that records resolution. |
| 3 min | A seeded rejected claim, Dashboard, and Reports | Show the claimant-facing reason and customer-service links. On the staff side, show open workload, pending information, status counts, and requested, approved, and estimated exposure by currency. Filter by submission date and download the PDF. |
| 2 min | Verification and one AI decision | Show the test command and two journal entries. Explain why you specified the core models and corrected the original proposal to keep the conversation readable apart from audit history. Show refresh retention or competing acceptance if time remains. |

Suggested opening:

> I built the claims lifecycle first because assignment and status transitions determine what both roles can safely do. Once that worked, I used Kafka for claimant notices and Angular for the two workflows. I kept each milestone in Git and used the journal to record design corrections and what I verified.

## Trade-off discussion · 12 minutes

- **Why split the services here?** Claim transitions and their audit entries share a transaction. Notifications have a different delivery and retry lifecycle. A separate consumer demonstrates that boundary without putting Kafka in the critical path for accepting or deciding a claim.
- **What happens when two staff members act at once?** The Claims API checks the current status and uses an EF Core concurrency token. A competing acceptance gets a conflict. Role, ownership, and assignment are enforced in the API.
- **What if Kafka goes down?** The advisory notice waits in the transactional outbox. The publisher retries. The consumer persists before committing the offset and deduplicates by event ID. This supports at-least-once delivery with idempotent storage.
- **What does the dashboard measure?** Current claim counts, staff workload, and recorded estimates for open claims. Missing estimates are visible, and monetary aggregates stay separated by currency. Date-filtered reports use inclusive UTC submission dates.
- **How did you direct AI?** You set the milestone order, specified the core models, required meaningful commits, and clarified that the thread and audit history serve different purposes. Describe tests you reviewed and one implementation choice you can defend. The journal is a record of what happened.

## What I would do with more time · 10 minutes

| Deferred | Current behavior and next step |
| --- | --- |
| Policy, payment, and document-provider integrations | Settlement marks the claim resolved in the workflow. Next, integrate policy validation, verified payment execution and reconciliation, and secure evidence upload. |
| Production identity and roles | The demo uses claimant and staff roles, a short-lived browser session, and a shared JWT signing key. Next, use an identity provider with MFA, session revocation, and distinct management permissions if needed. |
| Six-market deployment and financial controls | Amounts are separated by currency. Next, define market-specific policy, residency, and FX rules and introduce a production database with backups and recovery. |
| Production operations | The application runs locally. Next, add deployment automation, telemetry for outbox lag and consumer failures, retention rules, and operational alerting. |

## Questions for the panel · 5 minutes

1. Which claim transition or integration would you prioritize first for a pilot?
2. How does your team define outstanding exposure across markets and handle approvals versus settlement?
3. What evidence do you expect engineers to bring when signing off AI-generated changes?
