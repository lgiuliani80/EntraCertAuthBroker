using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Identity.Client;

namespace EntraCertAuthBroker;

public class TokenFunction(ILogger<TokenFunction> logger)
{
    private readonly ILogger<TokenFunction> _logger = logger;

    [Function("token")]
    public async Task<IActionResult> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = "token")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        try
        {
            var clientId = GetRequiredEnvironmentVariable("CLIENT_ID");
            var tenantId = GetRequiredEnvironmentVariable("TENANT_ID");
            var scopes = GetScopes(GetRequiredEnvironmentVariable("TARGET_SCOPE"));
            var thumbprint = GetThumbprint(GetRequiredEnvironmentVariable("WEBSITE_LOAD_CERTIFICATES"));

            using var certificate = LoadCertificate(thumbprint);

            var app = ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithAuthority(new Uri($"https://login.microsoftonline.com/{tenantId}"))
                .WithCertificate(certificate)
                .Build();

            var result = await app
                .AcquireTokenForClient(scopes)
                .ExecuteAsync(cancellationToken);

            return new OkObjectResult(new { token = result.AccessToken });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire a token with certificate-based client credentials.");

            return new ObjectResult(new { error = ex.Message })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    private static string GetRequiredEnvironmentVariable(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"The '{name}' environment variable is required.");
        }

        return value.Trim();
    }

    private static string[] GetScopes(string value) =>
        value.Split([' ', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static string GetThumbprint(string value)
    {
        var thumbprint = value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(item => !string.Equals(item, "*", StringComparison.Ordinal));

        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new InvalidOperationException("WEBSITE_LOAD_CERTIFICATES must contain the certificate thumbprint.");
        }

        return thumbprint.Replace(" ", string.Empty, StringComparison.Ordinal).ToUpperInvariant();
    }

    private static X509Certificate2 LoadCertificate(string thumbprint)
    {
        var linuxCertificatePath = $"/var/ssl/private/{thumbprint}.p12";

        if (File.Exists(linuxCertificatePath))
        {
            return X509CertificateLoader.LoadPkcs12FromFile(
                linuxCertificatePath,
                string.Empty,
                X509KeyStorageFlags.DefaultKeySet);
        }

        var currentUserCertificate = FindCertificate(StoreLocation.CurrentUser, thumbprint);
        if (currentUserCertificate is not null)
        {
            return currentUserCertificate;
        }

        var localMachineCertificate = FindCertificate(StoreLocation.LocalMachine, thumbprint);
        if (localMachineCertificate is not null)
        {
            return localMachineCertificate;
        }

        throw new FileNotFoundException(
            $"Certificate '{thumbprint}' was not found in '/var/ssl/private/{thumbprint}.p12' or in the local certificate stores.");
    }

    private static X509Certificate2? FindCertificate(StoreLocation location, string thumbprint)
    {
        using var store = new X509Store(StoreName.My, location);
        store.Open(OpenFlags.ReadOnly);

        var certificates = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
        return certificates.Count > 0 ? new X509Certificate2(certificates[0]) : null;
    }
}
