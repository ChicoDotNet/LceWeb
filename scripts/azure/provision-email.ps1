[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ResourceGroupName,

    [Parameter(Mandatory = $true)]
    [string]$CommunicationServiceName,

    [Parameter(Mandatory = $true)]
    [string]$EmailServiceName,

    [string]$ResourceGroupLocation,

    [string]$DataLocation = "United States",

    [string]$SenderDisplayName = "LCE Comercial",

    [string]$AppServicePrincipalId,

    [string[]]$Recipients,

    [string]$SubscriptionId
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Invoke-AzCli {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & az @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Azure CLI command failed: az $($Arguments -join ' ')"
    }
}

if ($SubscriptionId) {
    Invoke-AzCli -Arguments @("account", "set", "--subscription", $SubscriptionId)
}

$extension = & az extension show --name communication --query name --output tsv 2>$null
if ($LASTEXITCODE -ne 0 -or -not $extension) {
    Invoke-AzCli -Arguments @("extension", "add", "--name", "communication", "--yes", "--output", "none")
}

$groupExists = (& az group exists --name $ResourceGroupName) -eq "true"
if (-not $groupExists) {
    if (-not $ResourceGroupLocation) {
        throw "Resource group '$ResourceGroupName' does not exist. Provide -ResourceGroupLocation so the script can create it."
    }

    Invoke-AzCli -Arguments @(
        "group", "create",
        "--name", $ResourceGroupName,
        "--location", $ResourceGroupLocation,
        "--output", "none")
}

$communicationServiceId = & az communication show `
    --name $CommunicationServiceName `
    --resource-group $ResourceGroupName `
    --query id `
    --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $communicationServiceId) {
    Invoke-AzCli -Arguments @(
        "communication", "create",
        "--name", $CommunicationServiceName,
        "--resource-group", $ResourceGroupName,
        "--location", "Global",
        "--data-location", $DataLocation,
        "--output", "none")

    $communicationServiceId = & az communication show `
        --name $CommunicationServiceName `
        --resource-group $ResourceGroupName `
        --query id `
        --output tsv
}

$emailServiceId = & az communication email show `
    --email-service-name $EmailServiceName `
    --resource-group $ResourceGroupName `
    --query id `
    --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $emailServiceId) {
    Invoke-AzCli -Arguments @(
        "communication", "email", "create",
        "--email-service-name", $EmailServiceName,
        "--resource-group", $ResourceGroupName,
        "--location", "Global",
        "--data-location", $DataLocation,
        "--output", "none")
}

$domainName = "AzureManagedDomain"
$domainId = & az communication email domain show `
    --domain-name $domainName `
    --email-service-name $EmailServiceName `
    --resource-group $ResourceGroupName `
    --query id `
    --output tsv 2>$null

if ($LASTEXITCODE -ne 0 -or -not $domainId) {
    Invoke-AzCli -Arguments @(
        "communication", "email", "domain", "create",
        "--domain-name", $domainName,
        "--email-service-name", $EmailServiceName,
        "--resource-group", $ResourceGroupName,
        "--location", "Global",
        "--domain-management", "AzureManaged",
        "--output", "none")

    $domainId = & az communication email domain show `
        --domain-name $domainName `
        --email-service-name $EmailServiceName `
        --resource-group $ResourceGroupName `
        --query id `
        --output tsv
}

$linkedDomainsJson = & az communication show `
    --name $CommunicationServiceName `
    --resource-group $ResourceGroupName `
    --query linkedDomains `
    --output json

if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect linked email domains for Communication Service '$CommunicationServiceName'."
}

$linkedDomains = @()
if ($linkedDomainsJson) {
    $parsedLinkedDomains = $linkedDomainsJson | ConvertFrom-Json
    if ($null -ne $parsedLinkedDomains) {
        $linkedDomains = @($parsedLinkedDomains)
    }
}

if ($linkedDomains -notcontains $domainId) {
    $linkedDomains += $domainId
    $linkArguments = @(
        "communication", "update",
        "--name", $CommunicationServiceName,
        "--resource-group", $ResourceGroupName,
        "--linked-domains") + $linkedDomains + @("--output", "none")
    Invoke-AzCli -Arguments $linkArguments
}

$hostName = & az communication show `
    --name $CommunicationServiceName `
    --resource-group $ResourceGroupName `
    --query hostName `
    --output tsv
if ($LASTEXITCODE -ne 0 -or -not $hostName) {
    throw "Could not resolve the Communication Services host name."
}

$fromSenderDomain = & az communication email domain show `
    --domain-name $domainName `
    --email-service-name $EmailServiceName `
    --resource-group $ResourceGroupName `
    --query fromSenderDomain `
    --output tsv
if ($LASTEXITCODE -ne 0 -or -not $fromSenderDomain) {
    throw "Could not resolve the Azure Managed Domain sender domain."
}

$senderUsername = & az communication email domain sender-username list `
    --domain-name $domainName `
    --email-service-name $EmailServiceName `
    --resource-group $ResourceGroupName `
    --query "[0].username" `
    --output tsv
if ($LASTEXITCODE -ne 0 -or -not $senderUsername) {
    throw "Could not resolve a sender username for AzureManagedDomain."
}

if ($SenderDisplayName) {
    Invoke-AzCli -Arguments @(
        "communication", "email", "domain", "sender-username", "update",
        "--domain-name", $domainName,
        "--email-service-name", $EmailServiceName,
        "--resource-group", $ResourceGroupName,
        "--sender-username", $senderUsername,
        "--display-name", $SenderDisplayName,
        "--output", "none")
}

if ($AppServicePrincipalId) {
    $roleName = "Azure Communication Services Email Sender"
    $roleDefinition = & az role definition list `
        --name $roleName `
        --query "[0].name" `
        --output tsv

    if ($LASTEXITCODE -ne 0 -or -not $roleDefinition) {
        throw "Built-in role '$roleName' was not found. The App Service Managed Identity still needs a role granting ACS email send permissions on '$communicationServiceId'."
    }

    $existingRole = & az role assignment list `
        --assignee-object-id $AppServicePrincipalId `
        --scope $communicationServiceId `
        --role $roleName `
        --fill-principal-name false `
        --query "[0].id" `
        --output tsv

    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect ACS role assignments for App Service principal '$AppServicePrincipalId'."
    }

    if (-not $existingRole) {
        Invoke-AzCli -Arguments @(
            "role", "assignment", "create",
            "--assignee-object-id", $AppServicePrincipalId,
            "--assignee-principal-type", "ServicePrincipal",
            "--role", $roleName,
            "--scope", $communicationServiceId,
            "--output", "none")
    }
}

$endpoint = "https://$hostName"
$senderAddress = "$senderUsername@$fromSenderDomain"

Write-Host "Azure Communication Services Email ready."
Write-Host "  Communication Service: $CommunicationServiceName"
Write-Host "  Email Service:         $EmailServiceName"
Write-Host "  Domain:                $domainName"
Write-Host "  Endpoint:              $endpoint"
Write-Host "  Sender:                $senderAddress"
Write-Host ""
Write-Host "App Service settings:"
Write-Host "  Email__Provider=AzureCommunicationServices"
Write-Host "  Email__Endpoint=$endpoint"
Write-Host "  Email__SenderAddress=$senderAddress"
Write-Host "  Email__SenderDisplayName=$SenderDisplayName"
if ($Recipients -and $Recipients.Count -gt 0) {
    Write-Host "  Email__Recipients=$($Recipients -join ';')"
}
else {
    Write-Host "  Email__Recipients=<semicolon-separated recipients>"
}
