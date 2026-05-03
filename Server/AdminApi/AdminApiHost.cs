using Library.Db;
using Library.Db.Broadcast;
using Library.Db.Cache;
using Library.Db.Sql;
using Library.Logger;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using Server.Actors;

namespace Server.AdminApi;

public class AdminApiHost
{
    private static readonly IServerLogger _logger = ServerLoggerFactory.CreateLogger();
    private WebApplication? _app;

    public async Task StartAsync(
        AdminApiConfig adminConfig,
        DbConfig dbConfig,
        UserObjectPoolManager userManager,
        SqlWorkerManager sqlManager,
        CacheWorkerManager cacheManager,
        RedisBroadcastManager broadcastManager)
    {
        if (!adminConfig.Enabled)
        {
            _logger.Info(() => "AdminApi 비활성화됨");
            return;
        }

        var builder = WebApplication.CreateBuilder();

        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.ListenAnyIP(adminConfig.HttpsPort, listen =>
            {
                listen.Protocols = HttpProtocols.Http1AndHttp2;
                listen.UseHttps();
            });
        });

        var stats = new ServerStats(userManager);
        var keyStore = new SessionKeyStore(cacheManager, adminConfig);

        builder.Services.AddSingleton(adminConfig);
        builder.Services.AddSingleton(dbConfig);
        builder.Services.AddSingleton(userManager);
        builder.Services.AddSingleton(sqlManager);
        builder.Services.AddSingleton(cacheManager);
        builder.Services.AddSingleton(broadcastManager);
        builder.Services.AddSingleton(stats);
        builder.Services.AddSingleton(keyStore);

        builder.Services.AddControllers()
            .AddApplicationPart(typeof(AdminApiHost).Assembly);

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Game Server Admin API", Version = "v1" });
            c.AddSecurityDefinition(SessionKeyMiddleware.HeaderName, new OpenApiSecurityScheme
            {
                Name = SessionKeyMiddleware.HeaderName,
                Type = SecuritySchemeType.ApiKey,
                In = ParameterLocation.Header,
                Description = "POST /api/auth/login으로 발급받은 sessionKey"
            });
            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = SessionKeyMiddleware.HeaderName
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        _app = builder.Build();

        _app.UseSwagger();
        _app.UseSwaggerUI();
        _app.UseMiddleware<SessionKeyMiddleware>();
        _app.MapControllers();

        await _app.StartAsync();
        _logger.Info(() => $"AdminApi 시작: https://localhost:{adminConfig.HttpsPort}/swagger");
    }

    public async Task StopAsync()
    {
        if (_app != null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
            _app = null;
        }
    }
}
