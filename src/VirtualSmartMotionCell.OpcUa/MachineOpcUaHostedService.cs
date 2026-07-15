using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Opc.Ua;
using Opc.Ua.Configuration;
using VirtualSmartMotionCell.Application;
using VirtualSmartMotionCell.Runtime;

namespace VirtualSmartMotionCell.OpcUa;

public sealed class MachineOpcUaHostedService(
    MachineStateStore stateStore,
    IOptions<OpcUaOptions> options,
    ILogger<MachineOpcUaHostedService> logger) : BackgroundService
{
    private MachineOpcUaServer? _server;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("OPC UA simulation server is disabled");
            return;
        }

        var configuration = await CreateConfigurationAsync(options.Value, stoppingToken).ConfigureAwait(false);
        var application = new ApplicationInstance
        {
            ApplicationName = options.Value.ApplicationName,
            ApplicationType = ApplicationType.Server,
            ApplicationConfiguration = configuration
        };

        var certificateValid = await application.CheckApplicationInstanceCertificate(false, 2048).ConfigureAwait(false);
        if (!certificateValid) throw new InvalidOperationException("The OPC UA application certificate could not be created or validated.");

        _server = new MachineOpcUaServer(stateStore);
        await application.Start(_server).ConfigureAwait(false);
        logger.LogInformation("OPC UA simulation server listening at {Endpoint}", options.Value.Endpoint);

        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Stop();
        await base.StopAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ApplicationConfiguration> CreateConfigurationAsync(OpcUaOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pkiRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VirtualSmartMotionCell", "pki");
        var hostName = Utils.GetHostName();
        var configuration = new ApplicationConfiguration
        {
            ApplicationName = options.ApplicationName,
            ApplicationUri = $"urn:{hostName}:VirtualSmartMotionCell",
            ProductUri = "https://github.com/YOUR_GITHUB_HANDLE/virtual-smart-motion-cell",
            ApplicationType = ApplicationType.Server,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier
                {
                    StoreType = "Directory",
                    StorePath = Path.Combine(pkiRoot, "own"),
                    SubjectName = $"CN={options.ApplicationName}, DC={hostName}"
                },
                TrustedPeerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pkiRoot, "trusted") },
                TrustedIssuerCertificates = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pkiRoot, "issuer") },
                RejectedCertificateStore = new CertificateTrustList { StoreType = "Directory", StorePath = Path.Combine(pkiRoot, "rejected") },
                AutoAcceptUntrustedCertificates = true,
                AddAppCertToTrustedStore = true,
                MinimumCertificateKeySize = 2048
            },
            TransportConfigurations = new TransportConfigurationCollection(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 },
            ServerConfiguration = new ServerConfiguration
            {
                BaseAddresses = new StringCollection { options.Endpoint },
                SecurityPolicies = new ServerSecurityPolicyCollection
                {
                    new ServerSecurityPolicy { SecurityMode = MessageSecurityMode.None, SecurityPolicyUri = SecurityPolicies.None }
                },
                UserTokenPolicies = new UserTokenPolicyCollection
                {
                    new UserTokenPolicy { TokenType = UserTokenType.Anonymous }
                },
                DiagnosticsEnabled = true,
                MaxSessionCount = 20,
                MinSessionTimeout = 10000,
                MaxSessionTimeout = 3600000
            },
            CertificateValidator = new CertificateValidator()
        };
        await configuration.Validate(ApplicationType.Server).ConfigureAwait(false);
        await configuration.CertificateValidator.Update(configuration.SecurityConfiguration).ConfigureAwait(false);
        return configuration;
    }
}
