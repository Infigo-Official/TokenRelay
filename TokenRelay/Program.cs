using TokenRelay.Middleware;
using TokenRelay.Models;
using TokenRelay.Services;
using TokenRelay.HealthChecks;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);

InitializeLogging(builder);

builder.Logging.AddConsole(options =>
{
    options.FormatterName = ConsoleFormatterNames.Simple;
});

builder.Logging.AddSimpleConsole(options =>
{
    options.IncludeScopes = false;
    options.SingleLine = true;
    options.TimestampFormat = "[yyyy-MM-dd HH:mm:ss.fff] ";
    options.UseUtcTimestamp = false;
});

// Configure TokenRelay configuration
builder.Services.Configure<TokenRelayConfig>(builder.Configuration.GetSection("TokenRelay"));

// Add configuration from external file
var configPath = builder.Configuration.GetValue<string>("ConfigPath") ?? "tokenrelay.json";
if (File.Exists(configPath))
{
    builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: true);
    builder.Services.Configure<TokenRelayConfig>(builder.Configuration);
}

// Add services to the container
builder.Services.AddControllers();

// Configure HttpClient with logging handler
builder.Services.AddTransient<HttpLoggingHandler>();

// Configure default named HttpClient for standard use cases
builder.Services.AddHttpClient(HttpClientService.StandardClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 100,
        // Certificate validation enabled by default
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = null // Use default validation
        }
    })
    .AddHttpMessageHandler<HttpLoggingHandler>()
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan); // Let SocketsHttpHandler manage connection lifetime

// Configure HttpClient for targets that ignore certificate validation (dev/test only)
builder.Services.AddHttpClient(HttpClientService.IgnoreCertsClientName)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 100,
        // WARNING: Certificate validation disabled - use only in dev/test
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) => true
        }
    })
    .AddHttpMessageHandler<HttpLoggingHandler>()
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

// Configure HttpClient for Downloader plugin (no logging handler to avoid logging full file content)
builder.Services.AddHttpClient("DownloaderClient")
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        MaxConnectionsPerServer = 50,
        AllowAutoRedirect = true,
        MaxAutomaticRedirections = 10
    })
    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);

// Also add the default HttpClient factory for backward compatibility
builder.Services.AddHttpClient();

// Add health checks
builder.Services.AddHealthChecks()
    .AddTypeActivatedCheck<ConfigurationHealthCheck>("configuration")
    .AddTypeActivatedCheck<PluginHealthCheck>("plugins")
    .AddTypeActivatedCheck<ConnectivityHealthCheck>("connectivity");

// Add custom services
builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IOAuthService, OAuthService>();
builder.Services.AddSingleton<IOAuth1Service, OAuth1Service>();
builder.Services.AddScoped<IProxyService, ProxyService>();
builder.Services.AddSingleton<IPluginService, PluginService>();
builder.Services.AddSingleton<IMemoryLogService, MemoryLogService>();
builder.Services.AddSingleton<ILogLevelService, LogLevelService>();
builder.Services.AddSingleton<IHttpClientService, HttpClientService>();
builder.Services.AddHostedService<LogCleanupService>();

// Note: Memory logging provider will be added after app.Build() to avoid circular dependency

// Configure request buffering for chain mode scenarios
builder.Services.Configure<IISServerOptions>(options =>
{
    options.AllowSynchronousIO = true;
});

// Configure Kestrel for better streaming support
builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.AllowSynchronousIO = true;
    options.Limits.MaxRequestBodySize = 100_000_000; // 100MB limit
    options.Limits.RequestHeadersTimeout = TimeSpan.FromMinutes(5);
});

// Configure OpenAPI/Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "TokenRelay Proxy API",
        Version = "v1",
        Description = "A secure HTTP proxy service with credential management and plugin support. Health checks are available at /health, /health/live, and /health/ready endpoints."
    });

    c.AddSecurityDefinition("TokenRelayAuth", new()
    {
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Name = "TOKEN-RELAY-AUTH",
        Description = "TokenRelay authentication token"
    });

    c.AddSecurityRequirement(new()
    {
        {
            new()
            {
                Reference = new() { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "TokenRelayAuth" }
            },
            Array.Empty<string>()
        }
    });
});

// Configure CORS if needed
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Configure memory logging after app is built to avoid circular dependency
var memoryLogService = app.Services.GetRequiredService<IMemoryLogService>();
var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
loggerFactory.AddProvider(new MemoryLoggerProvider(memoryLogService));

// Get logger for startup logging
var logger = app.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("Starting TokenRelay Proxy application");
logger.LogInformation("Environment: {Environment}", app.Environment.EnvironmentName);

try
{
    // Load configuration and log startup info
    var configService = app.Services.GetRequiredService<IConfigurationService>();
    var config = configService.GetConfiguration();

    // Update memory log service with configuration now that everything is available
    if (Enum.TryParse<LogLevel>(config.Logging.Level, true, out var configuredLevel))
    {
        memoryLogService.UpdateConfiguration(config.Proxy.LogBufferMinutes, configuredLevel);

        // Update the log level service which will affect future logging
        var logLevelService = app.Services.GetRequiredService<ILogLevelService>();
        logLevelService.SetLogLevel(configuredLevel);

        logger.LogDebug("Logging system configured with level: {LogLevel}", config.Logging.Level);
    }

    logger.LogInformation("Configuration loaded successfully");
    logger.LogInformation("Proxy mode: {Mode}", config.Proxy.Mode);
    logger.LogInformation("Configured targets: {TargetCount}", config.Proxy.Targets.Count);
    logger.LogInformation("Authentication tokens: {TokenCount}", config.Proxy.Auth.Tokens.Count);
    logger.LogInformation("Log level: {LogLevel}", config.Logging.Level);
    logger.LogInformation("Log buffer minutes: {BufferMinutes}", config.Proxy.LogBufferMinutes);

    if (config.Proxy.Mode.Equals("chain", StringComparison.OrdinalIgnoreCase))
    {
        logger.LogInformation("Chain mode enabled - Target proxy: {ChainEndpoint}",
            config.Proxy.Chain?.TargetProxy?.Endpoint ?? "not configured");
    }

    // Log target details at debug level
    foreach (var target in config.Proxy.Targets)
    {
        logger.LogDebug("Target '{TargetName}' configured: {Endpoint}", target.Key, target.Value.Endpoint);
        if (!string.IsNullOrEmpty(target.Value.HealthCheckUrl))
        {
            logger.LogDebug("Target '{TargetName}' health check: {HealthCheckUrl}", target.Key, target.Value.HealthCheckUrl);
        }
    }
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Failed to load configuration during startup");
    throw;
}

// Load plugins on startup
try
{
    logger.LogInformation("Loading plugins...");
    var pluginService = app.Services.GetRequiredService<IPluginService>();
    pluginService.LoadPluginsAsync();

    var loadedPlugins = pluginService.GetLoadedPlugins();
    logger.LogInformation("Plugins loaded successfully: {PluginCount}", loadedPlugins.Count);

    foreach (var plugin in loadedPlugins)
    {
        logger.LogDebug("Plugin loaded: {PluginName} v{Version} - {Status}",
            plugin.Name, plugin.Version, plugin.Status);
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Failed to load plugins during startup");
    // Continue startup even if plugins fail to load
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "TokenRelay Proxy API v1");
        c.RoutePrefix = string.Empty; // Serve Swagger UI at root
    });
}

app.UseCors();

// Enable request body buffering so it can be read by the proxy service.
// By default, ASP.NET Core's Request.Body is a forward-only network stream.
// Without buffering, CopyToAsync reads 0 bytes because something in the
// middleware pipeline (possibly routing or CORS) inspects or consumes the stream.
// EnableBuffering() replaces the stream with a FileBufferingReadStream that
// supports seeking, allowing the body to be read multiple times.
app.Use(async (context, next) =>
{
    context.Request.EnableBuffering();
    await next();
});

// Add authentication middleware (before authorization)
app.UseMiddleware<AuthenticationMiddleware>();

app.UseHttpsRedirection();
app.UseAuthorization();

// Map health check endpoints
app.MapHealthChecks("/health", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";

        var response = new
        {
            status = report.Status.ToString(),
            totalDuration = report.TotalDuration,
            checks = report.Entries.Select(entry => new
            {
                name = entry.Key,
                status = entry.Value.Status.ToString(),
                description = entry.Value.Description,
                duration = entry.Value.Duration,
                data = entry.Value.Data
            })
        };

        await context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(response, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            WriteIndented = true
        }));
    }
});

app.MapControllers();

logger.LogInformation("TokenRelay Proxy startup completed successfully");
logger.LogInformation("Application is ready to accept requests");

try
{
    app.Run();
}
catch (Exception ex)
{
    logger.LogCritical(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    logger.LogInformation("TokenRelay Proxy application shutdown");
}

static void InitializeLogging(WebApplicationBuilder builder)
{
    // Configure console logging format
    builder.Logging.ClearProviders();

    // Read TokenRelay configuration early to get the log level
    var tempConfig = new ConfigurationBuilder()
        .AddJsonFile("tokenrelay.json", optional: true)
        .Build();

    var tokenRelayLogLevel = tempConfig.GetValue<string>("logging:level") ?? "Information";
    if (Enum.TryParse<LogLevel>(tokenRelayLogLevel, true, out var startupLogLevel))
    {
        // Clear all existing logging configuration and set our own
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(startupLogLevel);

        // Configure logging rules to completely override appsettings.json
        // This ensures that ONLY our tokenrelay.json log level is respected
        builder.Logging.AddFilter((category, level) =>
        {
            // Use the runtime log level filter that can be updated at runtime
            return RuntimeLogLevel.IsEnabled(category, level);
        });
        
        // Set the initial runtime log level
        RuntimeLogLevel.SetLogLevel(startupLogLevel);
    }
}