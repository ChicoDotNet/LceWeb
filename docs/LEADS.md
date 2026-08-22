# Lead capture and authoritative diagnostic results

## Flow

The browser never submits client-computed scores as authoritative data.

```text
diagnostic:completed
  -> contact form
  -> POST /api/leads
  -> load exact diagnostic GUID + version
  -> validate contact and visible answers
  -> reject unknown/hidden/tampered answers
  -> recompute scores and result bands in C#
  -> persist LeadSubmission
  -> return submission id + authoritative result
```

The machine-readable public request contract is `contracts/lead-submission.schema.json`.

## POST /api/leads

Example:

```json
{
  "diagnosticId": "16f5812b-6a27-44f6-b5b4-557a720a6425",
  "definitionVersion": 1,
  "contact": {
    "name": "Persona Demo",
    "email": "persona@example.com",
    "phone": {
      "callingCode": "+52",
      "number": "55 1234 5678"
    }
  },
  "answers": [
    {
      "questionId": "stage",
      "answerIds": ["planning"]
    },
    {
      "questionId": "priorities",
      "answerIds": ["cost"]
    }
  ],
  "acquisition": {
    "utmSource": "google",
    "utmMedium": "cpc",
    "utmCampaign": "importaciones",
    "utmTerm": null,
    "utmContent": null,
    "referrer": "https://www.google.com/",
    "pageUrl": "https://example.test/landing"
  },
  "website": ""
}
```

`name` and `email` are required. `phone` is optional. When supplied, `callingCode` uses international format such as `+52`; formatting characters in the local number are normalized by the server.

`website` is a honeypot and legitimate browser code must leave it empty.

The endpoint returns `201 Created` only after the submission has been stored. The response contains the submission id and server-computed scores/results.

## Validation

The server loads the exact `diagnosticId` + `definitionVersion` before accepting the lead.

It rejects, among other invalid states:

- unknown diagnostic versions;
- inactive diagnostic versions;
- duplicate question submissions;
- unknown question ids;
- answers to questions that are hidden by the submitted route;
- unknown option ids;
- too many answers for a single-choice question;
- violations of min/max selection constraints;
- missing required visible answers;
- excessive text length;
- invalid email or telephone formats;
- invalid acquisition URLs.

Client `scores` and `results` are intentionally not part of the POST contract.

## Browser integration

`/js/leads/lead-capture.js` exposes `attachLeadCapture`.

It listens for `diagnostic:completed`, reveals a simple contact form, captures UTM/referrer/page URL, and converts the diagnostic answer map to the POST shape.

The reusable module emits:

- `lead:ready` when a completed diagnostic can accept contact data;
- `lead:submitted` after a successful API response;
- `lead:error` after a failed submission.

The technical harness at `/diagnostic-demo.html` demonstrates the complete browser flow without representing a production landing design.

## Storage providers

Local development and CI default to:

```text
Leads__Storage__Provider=Memory
```

When `Leads:Storage` is omitted, its provider/endpoint/connection-string settings inherit from `Diagnostics:Storage`. This makes one Storage Account and one Managed Identity the normal production topology.

Production settings:

```text
Leads__Storage__Provider=AzureTable
Leads__Storage__TableName=LeadSubmissions
Leads__Storage__TableEndpoint=https://<storage-account>.table.core.windows.net/
```

`DefaultAzureCredential` is used when no connection string is supplied. App Service is expected to use its Managed Identity.

## LeadSubmissions table

```text
PartitionKey      = diagnostic GUID
RowKey            = submission GUID
CreatedUtc        = DateTimeOffset
DefinitionVersion = Int32
Name              = searchable contact name
Email             = searchable contact email
CallingCode       = optional
PhoneNumber       = optional
UtmSource         = optional
UtmMedium         = optional
UtmCampaign       = optional
PageUrl           = optional
EmailStatus       = Pending
SubmissionJson    = canonical complete lead submission
```

The table stores metadata columns for support/search plus the complete canonical JSON. `SubmissionJson` has the same conservative 60 KiB guard used for diagnostic definition JSON.

`EmailStatus=Pending` is reserved for the Azure Communication Services email delivery increment.

## Provisioning

`scripts/azure/provision-storage.ps1` now creates both tables by default:

- `DiagnosticDefinitions`;
- `LeadSubmissions`.

It prints both diagnostic and lead App Service settings. The same `Storage Table Data Contributor` assignment covers both tables because the role is scoped to the Storage Account.

## Public-endpoint protections

The current API applies:

- a 128 KiB request-body ceiling at Kestrel level;
- a fixed-window rate limit of 10 lead submissions per minute, partitioned by the remote IP observed by the application instance;
- a honeypot field;
- full server-side revalidation.

A CAPTCHA is intentionally not part of the first production path; it can be introduced later if observed abuse justifies the user friction.
