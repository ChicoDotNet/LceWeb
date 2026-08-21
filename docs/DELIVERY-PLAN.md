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

Status: in progress.

Outputs:

- repository README;
- .NET-oriented ignore rules;
- delivery plan;
- feature branch for the first increment.

Acceptance:

- all implementation work happens outside `main` after the bootstrap commit;
- delivery sequence and architecture are documented in the repository.

## Delivery 1 — diagnostic contract and API skeleton

Outputs:

- ASP.NET Core API project;
- strongly typed diagnostic definition model;
- JSON schema/examples for configurable hierarchical diagnostics;
- `GET /api/diagnostics/{diagnosticId}`;
- local/in-memory repository first, behind an abstraction suitable for Azure Tables;
- validation for GUID, active definition and version.

Diagnostic definition should support at minimum:

- stable diagnostic GUID;
- version;
- active/inactive state;
- ordered steps/questions;
- stable question IDs;
- single-choice answers;
- multiple-choice answers;
- optional free-text questions;
- ordered answer options;
- optional conditional branches;
- introductory/completion copy;
- optional result/scoring metadata without coupling it to a specific landing.

Acceptance:

- a GUID returns its definition as JSON;
- changing the stored definition changes the rendered contract without recompiling the landing;
- unknown/inactive IDs return appropriate HTTP errors.

## Delivery 2 — Azure Table Storage persistence

Outputs:

- Azure Table implementation of diagnostic repository;
- `DiagnosticDefinitions` table;
- versioned definition storage;
- startup/configuration through App Service settings;
- Managed Identity / role-based access where supported;
- PowerShell seed script for diagnostic JSON.

Recommended keying:

```text
DiagnosticDefinitions
PartitionKey = diagnostic GUID
RowKey       = zero-padded version or active/version convention
```

The hierarchical definition remains canonical JSON; Table Storage is used to locate and version it rather than decomposing each question into a separate entity.

Acceptance:

- a JSON definition can be added from Cloud Shell without redeploying the API;
- API resolves the active definition by GUID from Azure Tables;
- no Storage Account access key is embedded in source control.

## Delivery 3 — reusable vanilla diagnostic runtime

Outputs:

- shared vanilla JS API client;
- shared diagnostic renderer/runtime;
- state model for answers;
- conditional navigation;
- client-side result preview when the definition supports it;
- landing configuration reduced primarily to a diagnostic GUID.

Example integration:

```html
<body data-diagnostic-id="00000000-0000-0000-0000-000000000000">
```

Acceptance:

- one selected landing renders its diagnostic entirely from the API definition;
- question copy/order/options can change without modifying its HTML;
- no framework dependency is introduced.

## Delivery 4 — lead capture and authoritative submission

Outputs:

- replace `mailto` completion with contact form;
- required name and email;
- optional telephone with country calling code;
- `POST /api/leads`;
- server-side revalidation of question IDs / answer IDs against the referenced diagnostic version;
- server-side computation of the authoritative diagnostic result;
- UTM/referrer/page URL capture;
- persisted submission JSON.

Suggested submission shape:

```json
{
  "diagnosticId": "00000000-0000-0000-0000-000000000000",
  "definitionVersion": 1,
  "contact": {
    "name": "Example",
    "email": "person@example.com",
    "phone": {
      "callingCode": "+52",
      "number": "5512345678"
    }
  },
  "answers": [
    {
      "questionId": "stage",
      "answerIds": ["planning"]
    }
  ],
  "acquisition": {
    "utmSource": "google",
    "utmMedium": "cpc",
    "utmCampaign": "example",
    "utmTerm": null,
    "utmContent": null,
    "referrer": null,
    "pageUrl": "https://example.test/landing"
  }
}
```

Recommended storage:

```text
LeadSubmissions
PartitionKey = diagnostic GUID
RowKey       = lead submission GUID
```

Store searchable metadata as columns plus the complete canonical submission JSON.

Acceptance:

- valid submission is persisted before email delivery is attempted;
- malformed/tampered answer IDs are rejected;
- lead can be reconstructed with diagnostic version, answers, result and acquisition context.

## Delivery 5 — Azure Communication Services email

Outputs:

- ACS Email integration;
- recipient list read from App Service configuration;
- sender configuration read from App Service configuration;
- human-readable lead email containing contact data, landing/diagnostic identity, acquisition context, questions, answers and computed result;
- persisted email delivery state / failure detail sufficient for retry/support.

Acceptance:

- a stored lead is never lost because email delivery fails;
- successful submission produces a readable notification for all configured recipients;
- secrets are not exposed to browser code or committed source.

## Delivery 6 — migrate all four pages

Outputs:

- unique GUID per experience;
- four diagnostic definition JSON documents;
- all four vanilla HTML experiences use the shared API/runtime;
- existing visual/song differences remain intact;
- contact completion uses the shared lead mechanism.

Acceptance:

- four pages operate against one backend;
- each page can have a different question tree;
- adding a fifth page does not require a backend code change when its question types are already supported.

## Delivery 7 — Azure CLI / PowerShell provisioning

Outputs under `scripts/azure/`:

- `bootstrap.ps1` — creates base resources;
- `configure-dns.ps1` — creates/configures Azure DNS records and prints any registrar delegation required;
- `configure-email.ps1` — provisions/configures Communication Services email resources and domain settings;
- `configure-app.ps1` — App Service settings, identities and RBAC;
- `seed-diagnostics.ps1` — publishes definition JSON to Table Storage;
- `deploy.ps1` — builds/publishes/deploys application;
- `verify.ps1` — runs post-deployment smoke checks.

The scripts use Azure CLI commands from PowerShell and are designed for Azure Cloud Shell. They must be idempotent where practical and fail clearly when a manual/external DNS step is required.

Resources to provision/configure:

- Resource Group;
- App Service Plan;
- App Service;
- Storage Account + tables;
- Managed Identity / RBAC;
- Azure Communication Services;
- Email Communication Service/domain configuration;
- Azure DNS Zone;
- App Service application settings.

Acceptance:

- a new Azure subscription/user can follow the documented Cloud Shell flow and create the environment without Bicep;
- rerunning scripts does not unnecessarily recreate existing resources;
- scripts print required manual DNS delegation/verification actions explicitly;
- no production secret is committed to Git.

## Delivery 8 — operations handoff and training

Outputs:

- `docs/DIAGNOSTICS.md` — create/change/version a diagnostic JSON;
- `docs/AZURE-SETUP.md` — run provisioning/deployment scripts;
- `docs/OPERATIONS.md` — recipients, app settings, storage inspection, troubleshooting;
- worked example: copy an existing definition, generate a new GUID, seed it, assign it to a new HTML page;
- short handoff checklist for LCE team member.

Acceptance:

A trained team member can, without modifying C#:

1. create a new GUID;
2. copy/edit a diagnostic JSON;
3. publish it with the seed script;
4. assign the GUID to a landing;
5. change notification recipients through App Service settings;
6. inspect captured leads and diagnose common delivery failures.

## Cross-cutting requirements

### Security

- server validates all submitted answers against the stored definition;
- diagnostic result stored by the server is authoritative;
- no storage/email credentials in browser code;
- use Managed Identity/RBAC where supported;
- ASP.NET Core rate limiting on public write endpoints;
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

Never silently mutate historical meaning. A stored lead must keep the diagnostic version that was used when it was submitted. Editing a definition that changes semantics should create a new version.

### Delivery strategy

Implement one vertical slice before migrating all four pages:

```text
one diagnostic
  -> API
  -> Azure Tables
  -> one landing
  -> lead persistence
  -> email
  -> validate
  -> generalize
  -> migrate remaining three
```

This avoids repeating an incorrect integration four times.
