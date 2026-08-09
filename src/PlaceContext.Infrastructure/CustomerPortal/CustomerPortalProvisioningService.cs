using System.Net;
using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using PlaceContext.Application.Ports;

namespace PlaceContext.Infrastructure.CustomerPortal;

public sealed class CustomerPortalProvisioningService : ICustomerPortalProvisioner
{
    private const string DefaultImage = "registry.digitalocean.com/ctrlsignalregistryimg/placecontext-customer-portal:latest";
    private readonly string _namespace;
    private readonly string _portalImage;
    private readonly string _sharedHost;
    private readonly string _sharedSecretName;
    private readonly string _crmApiUrl;

    public CustomerPortalProvisioningService(IConfiguration configuration)
    {
        _namespace = ResolveNamespace();
        _portalImage = configuration["PlaceContext:CustomerPortal:Image"] ?? DefaultImage;
        _sharedHost = ResolveSharedHost(configuration);
        _sharedSecretName = configuration["PlaceContext:CustomerPortal:SharedTlsSecret"] ?? "feasibility-tls";
        _crmApiUrl = configuration["PlaceContext:CustomerPortal:CrmApiUrl"]
            ?? $"http://placecontext.{_namespace}.svc.cluster.local";
    }

    public async Task ProvisionAsync(
        Guid tenantId,
        string slug,
        string? customDomain,
        string? brandName,
        string? brandLogoUrl,
        string? defaultPortalUserName = null,
        string? defaultPortalUserEmail = null,
        string? defaultPortalUserPassword = null,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty) throw new InvalidOperationException("Tenant ID is required.");
        if (string.IsNullOrWhiteSpace(slug)) throw new ArgumentException("Portal slug is required.", nameof(slug));

        var slugValue = slug.Trim().ToLowerInvariant();
        var normalizedDomain = string.IsNullOrWhiteSpace(customDomain) ? null : customDomain.Trim();
        var normalizedBrandName = NormalizeBrandValue(brandName);
        var normalizedBrandLogoUrl = NormalizeBrandValue(brandLogoUrl);
        var portalName = $"customer-portal-{slugValue}";
        var portalPath = $"/p/{slugValue}".TrimEnd('/');

        var cfg = KubernetesClientConfiguration.InClusterConfig();
        using var client = new Kubernetes(cfg);

        var portalDomain = normalizedDomain ?? _sharedHost;
        var deployment = BuildDeployment(
            tenantId,
            slugValue,
            portalName,
            portalPath,
            portalDomain,
            normalizedBrandName,
            normalizedBrandLogoUrl,
            defaultPortalUserName?.Trim(),
            defaultPortalUserEmail?.Trim(),
            defaultPortalUserPassword);
        var service = BuildService(portalName);
        var ingress = BuildIngress(portalName, portalPath, normalizedDomain);

        await ApplyAsync(
            () => client.AppsV1.ReadNamespacedDeploymentAsync(portalName, _namespace, cancellationToken: ct),
            resource => client.AppsV1.ReplaceNamespacedDeploymentAsync(resource, portalName, _namespace, cancellationToken: ct),
            () => client.AppsV1.CreateNamespacedDeploymentAsync(deployment, _namespace, cancellationToken: ct),
            deployment,
            ct);
        await ApplyAsync(
            () => client.CoreV1.ReadNamespacedServiceAsync(portalName, _namespace, cancellationToken: ct),
            resource => client.CoreV1.ReplaceNamespacedServiceAsync(resource, portalName, _namespace, cancellationToken: ct),
            () => client.CoreV1.CreateNamespacedServiceAsync(service, _namespace, cancellationToken: ct),
            service,
            ct);
        await ApplyAsync(
            () => client.NetworkingV1.ReadNamespacedIngressAsync(portalName, _namespace, cancellationToken: ct),
            resource => client.NetworkingV1.ReplaceNamespacedIngressAsync(resource, portalName, _namespace, cancellationToken: ct),
            () => client.NetworkingV1.CreateNamespacedIngressAsync(ingress, _namespace, cancellationToken: ct),
            ingress,
            ct);
    }

    private static async Task ApplyAsync<T>(
        Func<Task<T>> read,
        Func<T, Task<T>> replace,
        Func<Task<T>> create,
        T desired,
        CancellationToken ct = default) where T : class
    {
        try
        {
            var existing = await read();
            if (existing is null)
            {
                await create();
                return;
            }

            if (desired is V1Deployment deployment)
                deployment.Metadata!.ResourceVersion = (existing as V1Deployment)?.Metadata?.ResourceVersion;
            if (desired is V1Service service)
                service.Metadata!.ResourceVersion = (existing as V1Service)?.Metadata?.ResourceVersion;
            if (desired is V1Ingress ingress)
                ingress.Metadata!.ResourceVersion = (existing as V1Ingress)?.Metadata?.ResourceVersion;

            await replace(desired);
        }
        catch (HttpOperationException ex) when (ex.Response?.StatusCode == HttpStatusCode.NotFound)
        {
            await create();
        }
    }

    private V1Deployment BuildDeployment(
        Guid tenantId,
        string slug,
        string portalName,
        string portalPath,
        string portalDomain,
        string? brandName,
        string? brandLogoUrl,
        string? defaultPortalUserName = null,
        string? defaultPortalUserEmail = null,
        string? defaultPortalUserPassword = null)
    {
        var labels = new Dictionary<string, string> { ["app"] = portalName };
        var normalizedDefaultPortalUserName = NormalizeValue(defaultPortalUserName);
        var normalizedDefaultPortalUserEmail = NormalizeValue(defaultPortalUserEmail);
        var normalizedDefaultPortalUserPassword = NormalizeValue(defaultPortalUserPassword);
        var env = new List<V1EnvVar>
        {
            new() { Name = "RAILS_ENV", Value = "production" },
            new() { Name = "RAILS_FORCE_SSL", Value = "false" },
            new() { Name = "PORT", Value = "7701" },
            new() { Name = "PLACE_CONTEXT_TENANT_ID", Value = tenantId.ToString() },
            new() { Name = "PLACE_CONTEXT_TENANT_SLUG", Value = slug },
            new() { Name = "PLACE_CONTEXT_PORTAL_DOMAIN", Value = portalDomain },
            new() { Name = "PLACE_CONTEXT_PORTAL_SHARED_HOST", Value = _sharedHost },
            new() { Name = "PLACE_CONTEXT_PORTAL_PATH", Value = portalPath },
            new()
            {
                Name = "RAILS_RELATIVE_URL_ROOT",
                Value = portalPath,
            },
            new() { Name = "PLACE_CONTEXT_CRM_API_URL", Value = _crmApiUrl },
            new()
            {
                Name = "PLACE_CONTEXT_CUSTOMER_PORTAL_API_KEY",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = "customer-portal-secrets",
                        Key = "core-api-key",
                    },
                },
            },
            new()
            {
                Name = "PLACE_CONTEXT_PROVISIONING_KEY",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = "customer-portal-secrets",
                        Key = "provisioning-key",
                    },
                },
            },
            new()
            {
                Name = "DATABASE_URL",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = "customer-portal-db",
                        Key = "connection-string",
                    },
                },
            },
            new()
            {
                Name = "SECRET_KEY_BASE",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = "customer-portal-secrets",
                        Key = "secret-key-base",
                    },
                },
            },
            new()
            {
                Name = "SMTP_ADDRESS",
                Value = "smtp.postmarkapp.com",
            },
            new() { Name = "SMTP_PORT", Value = "587" },
            new() { Name = "SMTP_DOMAIN", Value = "placecontext.ai" },
            new() { Name = "SMTP_AUTHENTICATION", Value = "plain" },
            new() { Name = "SMTP_ENABLE_STARTTLS", Value = "true" },
            new()
            {
                Name = "SMTP_USERNAME",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = "customer-portal-smtp",
                        Key = "smtp-username",
                    },
                },
            },
            new()
            {
                Name = "SMTP_PASSWORD",
                ValueFrom = new V1EnvVarSource
                {
                    SecretKeyRef = new V1SecretKeySelector
                    {
                        Name = "customer-portal-smtp",
                        Key = "smtp-password",
                    },
                },
            },
        };
        if (brandName is not null)
            env.Add(new() { Name = "PORTAL_BRAND_NAME", Value = brandName });
        if (brandLogoUrl is not null)
            env.Add(new() { Name = "PORTAL_BRAND_LOGO_URL", Value = brandLogoUrl });
        if (normalizedDefaultPortalUserName is not null)
            env.Add(new() { Name = "PORTAL_DEFAULT_USER_NAME", Value = normalizedDefaultPortalUserName });
        if (normalizedDefaultPortalUserEmail is not null)
            env.Add(new() { Name = "PORTAL_DEFAULT_USER_EMAIL", Value = normalizedDefaultPortalUserEmail });
        if (normalizedDefaultPortalUserPassword is not null)
            env.Add(new() { Name = "PORTAL_DEFAULT_USER_PASSWORD", Value = normalizedDefaultPortalUserPassword });

        return new V1Deployment
        {
            Metadata = new V1ObjectMeta
            {
                Name = portalName,
                NamespaceProperty = _namespace,
                Labels = labels,
            },
            Spec = new V1DeploymentSpec
            {
                Replicas = 1,
                Selector = new V1LabelSelector { MatchLabels = labels },
                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta { Labels = labels },
                    Spec = new V1PodSpec
                    {
                        NodeSelector = new Dictionary<string, string>
                        {
                            ["node-role.kubernetes.io/control-plane"] = "true",
                        },
                        Tolerations = new List<V1Toleration>
                        {
                            new()
                            {
                                Key = "node-role.kubernetes.io/control-plane",
                                OperatorProperty = "Exists",
                                Effect = "NoSchedule",
                            },
                        },
                        Containers = new List<V1Container>
                        {
                            new V1Container
                            {
                                Name = "customer-portal",
                                Image = _portalImage,
                                ImagePullPolicy = "IfNotPresent",
                                Ports = new List<V1ContainerPort> { new() { Name = "http", ContainerPort = 7701 } },
                                Env = env,
                                ReadinessProbe = new V1Probe
                                {
                                    HttpGet = new V1HTTPGetAction { Path = "/healthz", Port = 7701 },
                                    InitialDelaySeconds = 5,
                                    PeriodSeconds = 10,
                                },
                                LivenessProbe = new V1Probe
                                {
                                    HttpGet = new V1HTTPGetAction { Path = "/healthz", Port = 7701 },
                                    InitialDelaySeconds = 15,
                                    PeriodSeconds = 20,
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private V1Service BuildService(string portalName)
        => new()
        {
            Metadata = new V1ObjectMeta { Name = portalName, NamespaceProperty = _namespace },
            Spec = new V1ServiceSpec
            {
                Selector = new Dictionary<string, string> { ["app"] = portalName },
                Ports = new List<V1ServicePort>
                {
                    new() { Name = "http", Port = 80, TargetPort = 7701 },
                },
            },
        };

    private V1Ingress BuildIngress(
        string portalName,
        string portalPath,
        string? customDomain)
    {
        var target = new List<V1IngressRule>
        {
            new()
            {
                Host = customDomain ?? _sharedHost,
                Http = new V1HTTPIngressRuleValue
                {
                    Paths = new List<V1HTTPIngressPath>
                    {
                        new()
                        {
                            Path = customDomain is null ? portalPath : "/",
                            PathType = "Prefix",
                            Backend = new V1IngressBackend
                            {
                                Service = new V1IngressServiceBackend
                                {
                                    Name = portalName,
                                    Port = new V1ServiceBackendPort { Number = 80 },
                                },
                            },
                        },
                    },
                },
            },
        };

        return new V1Ingress
        {
            Metadata = new V1ObjectMeta
            {
                Name = portalName,
                NamespaceProperty = _namespace,
                Annotations = new Dictionary<string, string>
                {
                    ["cert-manager.io/cluster-issuer"] = "letsencrypt-prod",
                },
            },
            Spec = new V1IngressSpec
            {
                IngressClassName = "traefik",
                Tls = new List<V1IngressTLS>
                {
                    new() { Hosts = new List<string> { customDomain ?? _sharedHost }, SecretName = _sharedSecretName },
                },
                Rules = target,
            },
        };
    }

    private static string ResolveSharedHost(IConfiguration configuration)
    {
        var publicUrl = configuration["PlaceContext:PublicBaseUrl"];
        if (Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Host))
            return uri.Host;

        var legacyHost = configuration["PLACECONTEXT_HOSTNAME"];
        if (!string.IsNullOrWhiteSpace(legacyHost)) return legacyHost.Trim('.');
        return "localhost";
    }

    private static string? NormalizeBrandValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string? NormalizeValue(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string ResolveNamespace()
    {
        if (Environment.GetEnvironmentVariable("KUBERNETES_NAMESPACE") is { Length: > 0 } envNs)
            return envNs.Trim();
        return File.Exists("/var/run/secrets/kubernetes.io/serviceaccount/namespace")
            ? File.ReadAllText("/var/run/secrets/kubernetes.io/serviceaccount/namespace").Trim()
            : "placecontext";
    }
}
