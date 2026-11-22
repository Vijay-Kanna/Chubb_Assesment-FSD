# AI Working Journal

## Understanding the brief and shaping the design

I asked Codex to compare the three Chubb assessment briefs, explain the Fullstack requirements, and help me plan a backend-first submission. I approved a design with .NET services, an Angular app if time permits, JWT login and roles, and Kafka for in-app notifications after the claims workflow is working. I asked for OWASP-aligned security and explicit SQL injection prevention.

I challenged the initial design because it left the core models open. I specified `User`, `Role`, `Claim`, and `ClaimHistory`. I clarified that both roles should use the same user model, a claim needs a current status, and the assigned staff member needs to ask the claimant questions before approving or rejecting. Codex proposed a child `ClaimMessage` record for the conversation, a separate audit history, and two steps for approval and settlement. I approved the reviewed design and asked Codex to implement the initial API milestone before the later workflow and services.

## First backend milestone

I asked Codex to build login, roles, claim submission, claimant access to their own claims, and staff pickup. I also asked it to verify the result locally, update this journal, and make a meaningful local Git commit.

Codex generated the .NET API, SQLite migration, HTTP integration tests, and demo steps. It used a development-only seed with a password supplied at runtime and an ephemeral development JWT key. It implemented an optimistic concurrency check for pickup and saved claim updates with their audit entries. The tests check that competing staff requests produce one assignment and that access respects roles and claim ownership.

Codex ran six passing integration tests and exercised the login, submission, queue, pickup, claimant status, and history endpoints on the local HTTPS server. It found that Kestrel initially selected an untrusted localhost certificate on this machine. It selected the existing trusted certificate from the user store and documented that setup in the README. The NuGet advisory check reported no known vulnerable packages for the configured source.
