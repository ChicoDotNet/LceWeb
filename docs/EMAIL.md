# Azure Communication Services Email notifications

## Purpose

Lead notification is intentionally downstream from durable lead persistence:

```text
POST /api/leads
  -> validate diagnostic + answers
  -> calculate authoritative result
  -> store LeadSubmission (EmailStatus=Pending)
  -> send notification email
  -> update EmailStatus to Sent / Failed / Disabled
  -> return 201 Created
```

An email failure does **not** roll back or delete the lead. The prospect submission remains stored even when Azure Communication Services is unavailable.

## Providers

Local development and CI use the disabled provider by default:

```text
Email__Provider=None
```

Production uses:

```text
Email__Provider=AzureCommunicationServices
Email__Endpoint=https://<acs-resource>.communication.azure.com
Email__SenderAddress=<mail-from-address>
Email__Recipients=ventas@example.com;angelica@example.com
Email__SubjectPrefix=Nuevo prospecto LCE
```

`Email__Recipients` is a comma- or semicolon-separated list. At least one recipient is required when ACS is enabled.

The preferred production authentication path is `DefaultAzureCredential`, which resolves to the App Service Managed Identity. No ACS access key is required in App Service settings.

`Email__ConnectionString` exists only as an emergency/local escape hatch. Do not commit production connection strings.

The sender display name belongs to the ACS domain Sender Username resource. `provision-email.ps1` configures it there; it is deliberately **not** presented as an App Service setting because the .NET email payload sends the verified sender address, not a runtime display-name override.

## Email contents

The server builds both plaintext and HTML versions of the notification. The message contains:

- diagnostic name and exact version;
- lead/submission id and timestamp;
- prospect name, email and optional telephone;
- UTM/referrer/page information when available;
- each answered diagnostic question in definition order;
- human-readable answer labels rather than answer IDs;
- server-computed scores and result bands.

The browser does not provide the authoritative score or result used in the email.

User-controlled values are HTML encoded before they are placed in the HTML message.

## Delivery state in Table Storage

The `LeadSubmissions` entity is created first with:

```text
EmailStatus            = Pending
EmailStatusUpdatedUtc  = <lead creation time>
EmailOperationId       = ""
EmailError             = ""
```

After the notification attempt it is merged to one of:

```text
Disabled
Sent
Failed
```

For ACS, `Sent` means the Azure Communication Services send operation completed successfully and the message is **out for delivery**. It does not claim that the recipient mailbox has already delivered the message. Final mailbox delivery/bounce status belongs to ACS delivery events / Azure Monitor and can be added later if required.

`EmailOperationId` retains the ACS operation id for troubleshooting. `EmailError` retains a bounded diagnostic error when the send operation fails.

The immutable `SubmissionJson` continues to describe the lead itself; notification delivery metadata is stored in separate Table properties so transport state does not rewrite the captured diagnostic evidence.

## Provision an Azure-managed sender

The first production bootstrap can use an Azure Managed Domain. It requires no external DNS verification and is useful while the final LCE custom sender domain/DNS workflow is being automated.

From Azure Cloud Shell / PowerShell:

```powershell
./scripts/azure/provision-email.ps1 `
  -ResourceGroupName '<resource-group>' `
  -CommunicationServiceName '<acs-resource-name>' `
  -EmailServiceName '<email-service-name>' `
  -DataLocation 'United States' `
  -AppServicePrincipalId '<app-service-managed-identity-object-id>' `
  -Recipients @('ventas@example.com','otra@example.com')
```

If the resource group does not exist, also provide `-ResourceGroupLocation`.

The script:

1. ensures the Azure CLI `communication` extension exists;
2. creates/reuses the Communication Services resource;
3. creates/reuses the Email Communication Services resource;
4. creates/reuses `AzureManagedDomain`;
5. links that domain to the Communication Services resource;
6. resolves the generated MailFrom sender address;
7. optionally applies the sender display name to the ACS Sender Username resource;
8. when an App Service principal id is supplied, discovers the configured ACS email-sender role by name and assigns it at the Communication Services resource scope;
9. prints the App Service settings required by the application.

The script deliberately fails rather than silently granting a broader Azure role if the expected email-sender role cannot be resolved in the tenant/subscription.

## Custom LCE domain

A custom domain is a later infrastructure step because it requires DNS ownership verification plus sender authentication records and must be coordinated with the Azure DNS zone / registrar delegation work.

The application code does not change when moving from the Azure Managed Domain to a verified LCE domain. Only `Email__SenderAddress` and the Azure resource/domain configuration change.

## Notes on ACS resources

Azure Communication Services Email uses two related resources:

- an Azure Communication Services resource used by the application/API;
- a separate Email Communication Services resource that owns the sending domain.

The email domain must be linked to the Communication Services resource before the SDK can send from it.
