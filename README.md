# EntraCertAuthBroker

`EntraCertAuthBroker` is a .NET 10 isolated Azure Function project that exposes an HTTP trigger named `token`.

When invoked, the function uses the Microsoft Entra **client credentials** flow with a certificate and returns the access token as JSON:

```json
{ "token": "jwt-token" }
```

## Required configuration

Set the following application settings in Azure or in `local.settings.json` for local development:

| Setting | Description |
| --- | --- |
| `CLIENT_ID` | Client ID of the app registration. |
| `TENANT_ID` | Microsoft Entra tenant ID. |
| `TARGET_SCOPE` | Target scope for the client credentials flow, typically ending with `/.default`. |
| `WEBSITE_LOAD_CERTIFICATES` | Thumbprint of the certificate to load. |

On a Linux Function App, the certificate is expected at:

```text
/var/ssl/private/<thumbprint>.p12
```

## Local run

```powershell
func start
```

If you run locally on Windows, the function also tries to resolve the certificate from the `CurrentUser\My` or `LocalMachine\My` certificate stores using the same thumbprint.

## Azure CLI deployment to Flex Consumption

The commands below use PowerShell syntax.

### 1. Sign in and select the subscription

```powershell
az login
az account set --subscription "<SUBSCRIPTION_ID_OR_NAME>"
```

### 2. Define deployment variables

```powershell
$resourceGroup = "rg-entra-cert-broker"
$location = "westeurope"
$storageAccount = "entracertbroker$(Get-Random -Maximum 99999)"
$functionApp = "entra-cert-broker-$(Get-Random -Maximum 99999)"

$clientId = "<APP_REGISTRATION_CLIENT_ID>"
$tenantId = "<TENANT_ID>"
$targetScope = "api://<RESOURCE-APP-ID>/.default"

$certificatePath = "C:\path\to\certificate.pfx"
$certificatePassword = "<PFX_PASSWORD>"
```

> Use `az functionapp list-flexconsumption-locations -o table` to see the supported Flex Consumption regions.

### 3. Create the resource group and storage account

```powershell
az group create --name $resourceGroup --location $location

az storage account create \
  --name $storageAccount \
  --resource-group $resourceGroup \
  --location $location \
  --sku Standard_LRS \
  --allow-blob-public-access false
```

### 4. Create the Function App on Flex Consumption

```powershell
az functionapp create \
  --name $functionApp \
  --resource-group $resourceGroup \
  --storage-account $storageAccount \
  --flexconsumption-location $location \
  --runtime dotnet-isolated \
  --runtime-version 10 \
  --functions-version 4
```

### 5. Upload the certificate and capture its thumbprint

```powershell
$thumbprint = az webapp config ssl upload \
  --resource-group $resourceGroup \
  --name $functionApp \
  --certificate-file $certificatePath \
  --certificate-password $certificatePassword \
  --query thumbprint \
  -o tsv
```

### 6. Configure the required app settings

```powershell
az functionapp config appsettings set \
  --name $functionApp \
  --resource-group $resourceGroup \
  --settings \
    CLIENT_ID=$clientId \
    TENANT_ID=$tenantId \
    TARGET_SCOPE=$targetScope \
    WEBSITE_LOAD_CERTIFICATES=$thumbprint
```

### 7. Publish and deploy the function code

```powershell
dotnet publish .\EntraCertAuthBroker.csproj -c Release -o .\publish
Compress-Archive -Path .\publish\* -DestinationPath .\publish.zip -Force

az functionapp deploy \
  --resource-group $resourceGroup \
  --name $functionApp \
  --src-path .\publish.zip \
  --type zip
```

### 8. Get the function key and test the endpoint

```powershell
$functionKey = az functionapp function keys list \
  --resource-group $resourceGroup \
  --name $functionApp \
  --function-name token \
  --query default \
  -o tsv

Invoke-RestMethod -Method Get -Uri "https://$functionApp.azurewebsites.net/api/token?code=$functionKey"
```

If the configuration is correct, the endpoint returns:

```json
{ "token": "jwt-token" }
```

## GitHub Release workflow

The repository includes a workflow in `.github/workflows/release.yml`.

It:

- restores and builds the Function project
- publishes the app with `dotnet publish`
- creates a ZIP package
- uploads the ZIP file as a GitHub Release asset

### Trigger by tag

```powershell
git tag v1.0.0
git push origin v1.0.0
```

### Trigger manually

Open the **Actions** tab, run **Build and release Function package**, and provide a tag such as `v1.0.0`.
