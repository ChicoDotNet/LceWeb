# LceWeb delivery plan

## Goal

Turn the four LCE vanilla HTML experiences into configurable diagnostic landing pages backed by an ASP.NET Core API. Each landing references a unique diagnostic GUID. The API resolves the GUID to a hierarchical JSON definition stored in Azure Table Storage, accepts lead contact data plus diagnostic answers, persists submissions, and sends a lead notification through Azure Communication Services Email.

Infrastructure is provisioned with PowerShell scripts that call Azure CLI and are intended to run from Azure Cloud Shell / Azure Portal. No Bicep or Terraform is required.

## Target architecture

```text
Vanilla HTML landing
  pageId / diagnosticId = GUID
          |
          v
ASP.NET Core API
  |-- GET  /api/diagnostics/{guid}
  |-- POST /api/leads
  |
  |-- Azure Table Storage
  |     |-- DiagnosticDefinitions
  |     `-- LeadSubmissions
  |
  `-- Azure Communication Services Email
          |
          `-- configured recipients from App Service settings
```

## Delivery 0 — repository bootstrap

Status: complete.

Outputs:

- repository README;
- .NET-oriented ignore rules;
- delivery plan;
- feature branch for the first increment.

## Delivery 1 — diagnostic contract and API skeleton

Status: complete.

Outputs:

- ASP.NET Core API project;
- strongly typed diagnostic definition model;
- JSON schema/examples for configurable hierarchical diagnostics;
- `GET /api/diagnostics/{diagnosticId}`;
- local/in-memory repository behind an abstraction;
- validation for GUID, active definition and version.

## Delivery 2 — Azure Table Storage persistence

Status: complete.

Outputs:

- Azure Table implementation of diagnostic repository;
- `DiagnosticDefinitions` table;
- versioned definition storage;
- startup/configuration through App Service settings;
- Managed Identity / role-based access;
- PowerShell seed script for diagnostic JSON.

## Delivery 3 — reusable vanilla diagnostic runtime

Status: complete.

Outputs:

- shared Vanilla JS API client/renderer;
- pure diagnostic core;
- answer state model;
- conditional navigation;
- client-side result preview;
- landing configuration reduced primarily to a diagnostic GUID;
- technical demo harness;
- Node tests for branching, validation and scoring.

The four production HTML files are not yet present in this repository, so the runtime is validated through the technical harness until those pages are added.

## Delivery 4 — lead capture and authoritative submission

Status: complete.

Outputs:

- reusable contact form for required name/email and optional telephone with international calling code;
- `POST /api/leads`;
- exact diagnostic GUID/version lookup;
- server-side revalidation of question IDs, visible branches and answer IDs;
- server-side authoritative scoring/result-band calculation;
- UTM/referrer/page URL capture;
- `LeadSubmissions` repository abstraction;
- in-memory and Azure Table persistence;
- searchable lead metadata plus complete canonical `SubmissionJson`;
- request body ceiling, per-IP rate limiting and honeypot;
- machine-readable `contracts/lead-submission.schema.json`;
- browser and API smoke tests including tampered-answer rejection.

Acceptance achieved:

- a valid submission is persisted before HTTP 201 is returned;
- client-computed scores/results are not trusted;
- malformed/tampered answer IDs are rejected;
- lead data can be reconstructed with diagnostic version, answers, authoritative result and acquisition context.

## Delivery 5 — Azure Communication Services email

Status: complete.

Outputs:

- `Azure.Communication.Email` integration behind `ILeadNotificationSender`;
- disabled provider by default for local development and CI;
- recipient list, sender address, subject prefix and ACS endpoint from App Service configuration;
- preferred production authentication through `DefaultAzureCredential` / App Service Managed Identity;
- plaintext + HTML lead email containing contact data, diagnostic identity/version, acquisition context, ordered questions, human-readable answers and server-computed results;
- HTML encoding for user-controlled content;
- lead-first flow: durable persistence precedes notification;
- persisted `Pending`, `Disabled`, `Sent` or `Failed` email state;
- ACS operation id and bounded error detail for troubleshooting;
- `scripts/azure/provision-email.ps1` to provision/reuse ACS, Email Communication Services, an Azure Managed Domain and domain linking;
- sender display name configured on the ACS Sender Username resource rather than as a misleading runtime setting;
- `docs/EMAIL.md` operational guidance.

Acceptance achieved:

- a stored lead is never lost because email delivery fails;
- notification failures are represented independently from the immutable lead JSON;
- successful ACS send operation records its operation id;
- all configured recipients receive the same readable notification payload;
- no ACS credential is required in browser code or committed source;
- App Service can use Managed Identity for ACS authentication.

## Delivery 6 — migrate all four pages

Status: waiting for production HTML files in this repository.

Outputs:

- unique GUID per experience;
- four diagnostic definition JSON documents;
- all four Vanilla HTML experiences use the shared API/runtime;
- existing visual/song differences remain intact;
- contact completion uses the shared lead mechanism.

## Delivery 7 — Azure CLI / PowerShell provisioning

Status: partial; Storage and ACS Email scripts exist. This is the next executable infrastructure slice while production HTML remains unavailable.

Outputs under `scripts/azure/`:

- base-resource bootstrap;
- App Service Plan + App Service;
- system-assigned Managed Identity;
- Storage/RBAC integration;
- Communication Services Email/RBAC integration;
- Azure DNS zone and documented registrar-delegation boundary;
- custom email-domain DNS verification/authentication workflow;
- consolidated App Service settings;
- diagnostic seed;
- deployment;
- post-deployment verification.

The scripts use Azure CLI commands from PowerShell and are designed for Azure Cloud Shell. They must be idempotent where practical and fail clearly when a manual/external DNS step is required.

## Delivery 8 — operations handoff and training

Status: pending.

Outputs:

- diagnostic authoring/versioning guide;
- Azure setup/deployment guide;
- operations/troubleshooting guide;
- worked example for creating a GUID, publishing JSON and assigning it to a new HTML page;
- short handoff checklist for the LCE team member.

## Cross-cutting requirements

### Security

- server validates all submitted answers against the stored definition;
- diagnostic result stored by the server is authoritative;
- no storage/email credentials in browser code;
- use Managed Identity/RBAC where supported;
- public write endpoints use ASP.NET Core rate limiting;
- simple honeypot initially; introduce CAPTCHA only if observed abuse warrants extra friction.

### Experiment attribution

Capture from the first production submission:

- diagnostic/page GUID;
- diagnostic version;
- submission GUID;
- UTM source/medium/campaign/term/content;
- referrer;
- page URL;
- created UTC;
- normalized answers;
- computed result.

### Versioning

Never silently mutate historical meaning. A stored lead keeps the diagnostic version used when it was submitted. Editing a definition that changes semantics creates a new version.

### Delivery strategy

Implement one vertical slice before migrating all four pages:

```text
one diagnostic
  -> API
  -> Azure Tables
  -> reusable runtime
  -> lead persistence
  -> email
  -> validate
  -> migrate remaining pages
```
