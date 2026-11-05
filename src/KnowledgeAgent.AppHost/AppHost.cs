using System.Security.Cryptography;
using Aspire.Hosting.Publishing;
using Microsoft.Extensions.Configuration;

// Postgres and ClickHouse are shared with Langfuse to save memory; keep docker-compose.yml in sync.
var builder = DistributedApplication.CreateBuilder(args);

const string LangfuseOrgId = "knowledge-agent";
const string LangfuseProjectId = "knowledge-agent";

// Fixed because NEXTAUTH_URL and the links shown in the portal point here.
const int LangfusePort = 3000;

// Fixed so the portal URL stays stable. The Vite dev server runs behind this proxy port.
const int PortalPort = 5173;

// Fixed because the portal links to the Inngest dashboard here.
const int InngestPort = 8288;

// Fixed because the token issuer is the URL the browser sees, and the API checks it.
const int KeycloakPort = 8180;

const string VolumePrefix = "knowledge-agent-aspire";
var langfuseUrl = $"http://localhost:{LangfusePort}";
var portalUrl = $"http://localhost:{PortalPort}";
var dockerDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "docker"));

// Generated secrets are persisted in the AppHost's user secrets so they stay valid for the existing data volumes.
var postgresPassword = AddGeneratedSecret("postgres-password");
var clickHouseUser = builder.AddParameter("clickhouse-user", Setting("clickhouse-user", "clickhouse"));
var clickHousePassword = AddGeneratedSecret("clickhouse-password");
var redisPassword = AddGeneratedSecret("redis-password");
var minioUser = builder.AddParameter("minio-user", Setting("minio-user", "minio"));
var minioPassword = AddGeneratedSecret("minio-password");
var langfuseNextAuthSecret = AddGeneratedSecret("langfuse-nextauth-secret", minLength: 32);
var langfuseSalt = AddGeneratedSecret("langfuse-salt", minLength: 32);
var langfuseEncryptionKey = builder.AddParameter("langfuse-encryption-key", new HexKeyParameterDefault(), secret: true, persist: true);

// Fixed local development values so the Langfuse project, its API keys and the admin login are predictable.
var langfusePublicKey = builder.AddParameter("langfuse-public-key", Setting("langfuse-public-key", "pk-lf-local-dev"));
var langfuseSecretKey = builder.AddParameter("langfuse-secret-key", Setting("langfuse-secret-key", "sk-lf-local-dev"), secret: true);
var langfuseAdminEmail = Setting("langfuse-admin-email", "admin@knowledge-agent.local");
var langfuseAdminPassword = builder.AddParameter("langfuse-admin-password", Setting("langfuse-admin-password", "knowledge-agent"), secret: true);

var postgres = builder.AddPostgres("postgres", password: postgresPassword)
    .WithImageTag("17-alpine")
    .WithDataVolume($"{VolumePrefix}-postgres-data");
var knowledgeDb = postgres.AddDatabase("knowledgedb", databaseName: "knowledge");
var langfuseDb = postgres.AddDatabase("langfusedb", databaseName: "langfuse");

var clickHouse = builder.AddContainer("clickhouse", "clickhouse/clickhouse-server", "25.12")
    .WithHttpEndpoint(targetPort: 8123, name: "http")
    .WithEndpoint(targetPort: 9000, name: "native", scheme: "tcp")
    .WithEnvironment("CLICKHOUSE_DB", "default")
    .WithEnvironment("CLICKHOUSE_USER", clickHouseUser)
    .WithEnvironment("CLICKHOUSE_PASSWORD", clickHousePassword)
    .WithContainerRuntimeArgs("--user", "101:101", "--ulimit", "nofile=262144:262144")
    .WithVolume($"{VolumePrefix}-clickhouse-data", "/var/lib/clickhouse")
    .WithBindMount(Path.Combine(dockerDirectory, "clickhouse", "initdb"), "/docker-entrypoint-initdb.d", isReadOnly: true)
    // Mounted as single files so the image's own config.d and users.d stay intact.
    .WithBindMount(Path.Combine(dockerDirectory, "clickhouse", "config.d", "low-memory.xml"), "/etc/clickhouse-server/config.d/low-memory.xml", isReadOnly: true)
    .WithBindMount(Path.Combine(dockerDirectory, "clickhouse", "users.d", "low-memory.xml"), "/etc/clickhouse-server/users.d/low-memory.xml", isReadOnly: true)
    .WithHttpHealthCheck("/ping", endpointName: "http");
var clickHouseHttp = clickHouse.GetEndpoint("http");
var clickHouseNative = clickHouse.GetEndpoint("native");

var redis = builder.AddContainer("redis", "redis", "7")
    .WithEndpoint(targetPort: 6379, name: "tcp", scheme: "tcp")
    .WithArgs("--requirepass", redisPassword.Resource, "--maxmemory-policy", "noeviction")
    .WithVolume($"{VolumePrefix}-redis-data", "/data");
var redisEndpoint = redis.GetEndpoint("tcp");

var minio = builder.AddContainer("minio", "chainguard/minio", "latest")
    .WithImageRegistry("cgr.dev")
    .WithEntrypoint("sh")
    // MinIO serves top-level directories as buckets, so this creates the "langfuse" bucket.
    .WithArgs("-c", "mkdir -p /data/langfuse && minio server --address :9000 --console-address :9001 /data")
    .WithHttpEndpoint(targetPort: 9000, name: "api")
    .WithHttpEndpoint(targetPort: 9001, name: "console")
    .WithEnvironment("MINIO_ROOT_USER", minioUser)
    .WithEnvironment("MINIO_ROOT_PASSWORD", minioPassword)
    .WithVolume($"{VolumePrefix}-minio-data", "/data")
    .WithHttpHealthCheck("/minio/health/live", endpointName: "api")
    .WithUrlForEndpoint("console", url => url.DisplayText = "MinIO console");
var minioApi = minio.GetEndpoint("api");

// Endpoint references resolve per caller: container network addresses for containers, localhost for host processes.
WithLangfuseEnvironment(builder.AddContainer("langfuse-worker", "langfuse/langfuse-worker", "4"))
    .WithHttpEndpoint(targetPort: 3030, name: "http")
    .WithHttpHealthCheck("/api/health", endpointName: "http");

var langfuse = WithLangfuseEnvironment(builder.AddContainer("langfuse", "langfuse/langfuse", "4"))
    .WithHttpEndpoint(port: LangfusePort, targetPort: 3000, name: "http")
    .WithHttpHealthCheck("/api/public/health", endpointName: "http")
    .WithUrlForEndpoint("http", url => url.DisplayText = "Langfuse UI")
    .WithEnvironment("NEXTAUTH_SECRET", langfuseNextAuthSecret)
    // The browser opens presigned media URLs, so they use the host address while server uploads stay internal.
    .WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_ENDPOINT", minio.GetEndpoint("api", KnownNetworkIdentifiers.LocalhostNetwork))
    .WithEnvironment("LANGFUSE_S3_MEDIA_UPLOAD_INTERNAL_ENDPOINT", minioApi)
    .WithEnvironment("LANGFUSE_INIT_ORG_ID", LangfuseOrgId)
    .WithEnvironment("LANGFUSE_INIT_ORG_NAME", "Knowledge Agent")
    .WithEnvironment("LANGFUSE_INIT_PROJECT_ID", LangfuseProjectId)
    .WithEnvironment("LANGFUSE_INIT_PROJECT_NAME", "Knowledge Agent")
    .WithEnvironment("LANGFUSE_INIT_PROJECT_PUBLIC_KEY", langfusePublicKey)
    .WithEnvironment("LANGFUSE_INIT_PROJECT_SECRET_KEY", langfuseSecretKey)
    .WithEnvironment("LANGFUSE_INIT_USER_EMAIL", langfuseAdminEmail)
    .WithEnvironment("LANGFUSE_INIT_USER_NAME", "Admin")
    .WithEnvironment("LANGFUSE_INIT_USER_PASSWORD", langfuseAdminPassword);

// No WaitFor(langfuse): the API serves its embedded prompts until Langfuse is reachable.
var api = builder.AddProject<Projects.KnowledgeAgent_Api>("api", launchProfileName: "http")
    .WithReference(knowledgeDb)
    .WithEnvironment("ConnectionStrings__clickhouse", ReferenceExpression.Create(
        $"Host={clickHouseHttp.Property(EndpointProperty.Host)};Port={clickHouseHttp.Property(EndpointProperty.Port)};Username={clickHouseUser};Password={clickHousePassword}"))
    .WithEnvironment("Langfuse__BaseUrl", langfuse.GetEndpoint("http"))
    .WithEnvironment("Langfuse__PublicUrl", langfuseUrl)
    .WithEnvironment("Langfuse__PublicKey", langfusePublicKey)
    .WithEnvironment("Langfuse__SecretKey", langfuseSecretKey)
    .WithEnvironment("Langfuse__ProjectId", LangfuseProjectId)
    .WithEnvironment("Cors__AllowedOrigins__0", portalUrl)
    .WithHttpHealthCheck("/health")
    .WithUrlForEndpoint("http", endpoint => new ResourceUrlAnnotation { Url = $"{endpoint.Url}/scalar", DisplayText = "API reference (Scalar)" })
    .WaitFor(knowledgeDb)
    .WaitFor(clickHouse);

// Inngest's dev server keeps the runs and calls the API back for each one at the address WithApp gives it.
var inngest = builder.AddInngest("inngest", port: InngestPort)
    .WithEnvironment("DO_NOT_TRACK", "1");
api.WithReference(inngest)
    .WithEnvironment("Jobs__Provider", "Inngest")
    .WaitFor(inngest);
inngest.WithApp(api);

// The realm, its clients and two dev users come from docker/keycloak. The data volume keeps the realm,
// so remove the volume after editing that file.
var portalClientSecret = AddGeneratedSecret("portal-client-secret", minLength: 32);
var langfuseClientSecret = AddGeneratedSecret("langfuse-client-secret", minLength: 32);
var keycloakUrl = $"http://localhost:{KeycloakPort}";
var keycloakAuthority = $"{keycloakUrl}/realms/knowledge-agent";
var keycloak = builder.AddKeycloak("keycloak", KeycloakPort, adminPassword: AddGeneratedSecret("keycloak-admin-password"))
    .WithDataVolume($"{VolumePrefix}-keycloak")
    .WithRealmImport(Path.Combine(dockerDirectory, "keycloak"))
    // Langfuse reaches Keycloak on the container network; pages and tokens keep the browser's URL.
    .WithEnvironment("KC_HOSTNAME", keycloakUrl)
    .WithEnvironment("KC_HOSTNAME_BACKCHANNEL_DYNAMIC", "true")
    .WithEnvironment("KA_PORTAL_CLIENT_SECRET", portalClientSecret)
    .WithEnvironment("KA_PORTAL_URL", portalUrl)
    .WithEnvironment("KA_LANGFUSE_CLIENT_SECRET", langfuseClientSecret)
    .WithEnvironment("KA_LANGFUSE_URL", langfuseUrl)
    .WithEnvironment("KA_ADMIN_PASSWORD", builder.AddParameter("portal-admin-password", Setting("portal-admin-password", "admin"), secret: true))
    .WithEnvironment("KA_USER_PASSWORD", builder.AddParameter("portal-user-password", Setting("portal-user-password", "user"), secret: true));
api.WithEnvironment("Auth__Mode", "Oidc")
    .WithEnvironment("Auth__Authority", keycloakAuthority)
    .WithEnvironment("Auth__ClientSecret", portalClientSecret)
    .WithEnvironment("Auth__PublicOrigin", portalUrl)
    .WaitFor(keycloak);

// Langfuse signs staff in through the realm's langfuse client, which only lets admins in.
// Set langfuse-disable-password-sign-in to false to get password sign-in back for break-glass.
langfuse
    .WithEnvironment("AUTH_CUSTOM_CLIENT_ID", "langfuse")
    .WithEnvironment("AUTH_CUSTOM_CLIENT_SECRET", langfuseClientSecret)
    // Keycloak's address inside the container network; the metadata found there names the browser's URL as the issuer.
    .WithEnvironment("AUTH_CUSTOM_ISSUER", ReferenceExpression.Create($"{keycloak.GetEndpoint("http")}/realms/knowledge-agent"))
    .WithEnvironment("AUTH_CUSTOM_NAME", "Knowledge Agent")
    .WithEnvironment("AUTH_CUSTOM_ALLOW_ACCOUNT_LINKING", "true")
    // Keycloak's token response has fields Langfuse's account table lacks.
    .WithEnvironment("AUTH_IGNORE_ACCOUNT_FIELDS", "refresh_expires_in,not-before-policy")
    .WithEnvironment("AUTH_DISABLE_USERNAME_PASSWORD", Setting("langfuse-disable-password-sign-in", "true"))
    // Without Langfuse's project roles (an enterprise feature) the organisation role is the one that counts.
    .WithEnvironment("LANGFUSE_DEFAULT_ORG_ID", LangfuseOrgId)
    .WithEnvironment("LANGFUSE_DEFAULT_ORG_ROLE", "ADMIN")
    .WithEnvironment("LANGFUSE_DEFAULT_PROJECT_ID", LangfuseProjectId)
    .WithEnvironment("LANGFUSE_DEFAULT_PROJECT_ROLE", "ADMIN");

// Provider keys are optional and never prompted for. Without them the portal asks for a model on the Models page.
PassOptionalSetting("anthropic-api-key", "ANTHROPIC_API_KEY", "AI__Providers__anthropic__ApiKey", secret: true);
PassOptionalSetting("openai-api-key", "OPENAI_API_KEY", "AI__Providers__openai__ApiKey", secret: true);
PassOptionalSetting("ollama-endpoint", "OLLAMA_ENDPOINT", "AI__Providers__ollama__Endpoint", secret: false);
PassOptionalSetting("voyage-api-key", "VOYAGE_API_KEY", "AI__Providers__voyage__ApiKey", secret: true);

// Off by default because the models take several GB; the volume keeps pulled models.
if (builder.Configuration.GetValue("Ollama:Enabled", defaultValue: false))
{
    var ollama = builder.AddContainer("ollama", "ollama/ollama", "0.40.13")
        .WithHttpEndpoint(targetPort: 11434, name: "http")
        .WithVolume($"{VolumePrefix}-ollama", "/root/.ollama")
        .WithHttpHealthCheck("/", endpointName: "http");
    api.WithEnvironment("AI__Providers__ollama__Endpoint", ollama.GetEndpoint("http"));
}

// The API serves the embeddable widget from the widget build, which a watch build keeps current while you edit it.
var webDirectory = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", "web"));
api.WithEnvironment("Widgets__AssetsPath", Path.Combine(webDirectory, "dist-widget"));

if (builder.Configuration.GetValue("Portal:Enabled", defaultValue: true))
{
    var web = builder.AddViteApp("web", Path.Combine("..", "..", "web"))
        .WithEndpoint("http", endpoint => endpoint.Port = PortalPort)
        .WithUrlForEndpoint("http", url => url.DisplayText = "Portal")
        // Provides services__api__http__0, the /api proxy target in vite.config.ts.
        .WithReference(api)
        .WaitFor(api);

    // Started after the portal, which installs the packages both use.
    builder.AddExecutable("widget", "npm", webDirectory, "run", "widget:watch")
        .WaitFor(web);
}

builder.Build().Run();

string Setting(string parameterName, string defaultValue) =>
    builder.Configuration[$"Parameters:{parameterName}"] is { Length: > 0 } configured ? configured : defaultValue;

// Letters and digits only, because the values end up unescaped in URLs and command lines.
IResourceBuilder<ParameterResource> AddGeneratedSecret(string name, int minLength = 24) =>
    builder.AddParameter(name, new GenerateParameterDefault { MinLength = minLength, Special = false }, secret: true, persist: true);

void PassOptionalSetting(string parameterName, string environmentVariable, string apiSetting, bool secret)
{
    var value = new[] { builder.Configuration[$"Parameters:{parameterName}"], builder.Configuration[environmentVariable] }
        .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate));
    if (value is null)
    {
        return;
    }

    api.WithEnvironment(apiSetting, builder.AddParameter(parameterName, value, secret: secret));
}

IResourceBuilder<ContainerResource> WithLangfuseEnvironment(IResourceBuilder<ContainerResource> container) => container
    .WithImageRegistry("docker.langfuse.com")
    .WithEnvironment("NEXTAUTH_URL", langfuseUrl)
    .WithEnvironment("DATABASE_URL", langfuseDb.Resource.UriExpression)
    .WithEnvironment("SALT", langfuseSalt)
    .WithEnvironment("ENCRYPTION_KEY", langfuseEncryptionKey)
    .WithEnvironment("TELEMETRY_ENABLED", "false")
    .WithEnvironment("LANGFUSE_ENABLE_EXPERIMENTAL_FEATURES", "false")
    // Next.js binds to $HOSTNAME, which the container runtime sets to the container id.
    .WithEnvironment("HOSTNAME", "0.0.0.0")
    .WithEnvironment("CLICKHOUSE_MIGRATION_URL", ReferenceExpression.Create($"clickhouse://{clickHouseNative.Property(EndpointProperty.HostAndPort)}"))
    .WithEnvironment("CLICKHOUSE_URL", ReferenceExpression.Create($"http://{clickHouseHttp.Property(EndpointProperty.HostAndPort)}"))
    .WithEnvironment("CLICKHOUSE_USER", clickHouseUser)
    .WithEnvironment("CLICKHOUSE_PASSWORD", clickHousePassword)
    .WithEnvironment("CLICKHOUSE_DB", "default")
    .WithEnvironment("CLICKHOUSE_CLUSTER_ENABLED", "false")
    .WithEnvironment("REDIS_HOST", redisEndpoint.Property(EndpointProperty.Host))
    .WithEnvironment("REDIS_PORT", redisEndpoint.Property(EndpointProperty.Port))
    .WithEnvironment("REDIS_AUTH", redisPassword)
    .WithS3Environment("EVENT", "events/", minioApi, minioUser, minioPassword)
    .WithS3Environment("MEDIA", "media/", minioApi, minioUser, minioPassword)
    .WaitFor(langfuseDb)
    .WaitFor(clickHouse)
    .WaitFor(redis)
    .WaitFor(minio);

// Langfuse requires ENCRYPTION_KEY to be a 256-bit key in hex.
internal sealed class HexKeyParameterDefault : ParameterDefault
{
    public override string GetDefaultValue() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    // The AppHost targets local development, so the manifest only describes a generated secret.
    public override void WriteToManifest(ManifestPublishingContext context) =>
        new GenerateParameterDefault { MinLength = 64, Upper = false, Special = false }.WriteToManifest(context);
}

internal static class LangfuseResourceExtensions
{
    public static IResourceBuilder<ContainerResource> WithS3Environment(
        this IResourceBuilder<ContainerResource> container,
        string kind,
        string prefix,
        EndpointReference endpoint,
        IResourceBuilder<ParameterResource> accessKey,
        IResourceBuilder<ParameterResource> secretKey) => container
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_BUCKET", "langfuse")
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_REGION", "auto")
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_ACCESS_KEY_ID", accessKey)
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_SECRET_ACCESS_KEY", secretKey)
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_ENDPOINT", endpoint)
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_FORCE_PATH_STYLE", "true")
        .WithEnvironment($"LANGFUSE_S3_{kind}_UPLOAD_PREFIX", prefix);
}
