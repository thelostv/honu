using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using watchtower.Code.Hubs.Implementations;
using watchtower.Realtime;
using watchtower.Services;
using watchtower.Services.Hosted;
using watchtower.Models.Db;
using watchtower.Services.Db;
using watchtower.Services.Db.Implementations;
using watchtower.Services.Census;
using watchtower.Services.Repositories;
using watchtower.Services.Implementations;
using watchtower.Services.Db.Readers;
using System.Text.Json;
using DaybreakGames.Census;
using watchtower.Services.Offline;
using DaybreakGames.Census.Stream;
using watchtower.Models;
using watchtower.Services.Hosted.Startup;
using watchtower.Services.CharacterViewer;
using watchtower.Services.CharacterViewer.Implementations;
using watchtower.Services.Census.Readers;
using watchtower.Code.Converters;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using watchtower.Services.Queues;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using System.Net;
using watchtower.Models.PSB;
using watchtower.Services.Realtime;
using watchtower.Models.Health;
using watchtower.Models.Alert;
using watchtower.Code.Constants;
using System.Collections.Generic;
using watchtower.Services.Hosted.PSB;
using watchtower.Code.DiscordInteractions;
using Newtonsoft.Json.Linq;
using watchtower.Code;

//using honu_census;

namespace watchtower {

    public class Startup {

        // Will watchtower attempt to connect to the Census Streaming service?
        //      if false, yes, operation is performed as normal and events are recorded in real time
        //      if true, no, no connections are made, and mock events are created to create fake data
        private readonly bool OFFLINE_MODE = false;

        public Startup(IConfiguration configuration) {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services) {
            string stuff = ((IConfigurationRoot)Configuration).GetDebugView();
            Console.WriteLine(stuff);

            services.AddLogging(builder => {
                builder.AddFile("logs/honu-{0:yyyy}-{0:MM}-{0:dd}.log", options => {
                    options.FormatLogFileName = fName => {
                        return string.Format(fName, DateTime.UtcNow);
                    };
                });
            });

            services.AddRouting();

            if (OFFLINE_MODE == false) {
                services.AddCensusServices(options => {
                    options.CensusServiceId = "asdf";
                    options.CensusServiceNamespace = "ps2";
                    //options.LogCensusErrors = true;
                    options.LogCensusErrors = false;
                });
            } else {
                services.AddSingleton<ICensusQueryFactory, OfflineCensusQueryFactory>();
                services.AddSingleton<ICensusClient, OfflineCensusClient>();
                services.AddSingleton<ICensusStreamClient, OfflineCensusStreamClient>();
            }

            //services.AddSingleton<HonuCensus>();

            services.AddSignalR(options => {
                options.EnableDetailedErrors = true;
            }).AddJsonProtocol(options => {
                options.PayloadSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

            services.AddMvc(options => {

            }).AddJsonOptions(config => {
                config.JsonSerializerOptions.Converters.Add(new DateTimeJsonConverter());
            }).AddRazorRuntimeCompilation();

            services.AddSwaggerGen(doc => {
                doc.SwaggerDoc("api", new OpenApiInfo() { Title = "API", Version = "v0.1" });

                Console.Write("Including XML documentation in: ");
                foreach (string file in Directory.GetFiles(AppContext.BaseDirectory, "*.xml")) {
                    Console.Write($"{Path.GetFileName(file)} ");
                    doc.IncludeXmlComments(file);
                }
                Console.WriteLine("");
            });

            string gIDKey = "Authentication:Google:ClientId";
            string gSecretKey = "Authentication:Google:ClientSecret";

            string? googleClientID = Configuration[gIDKey];
            string? googleSecret = Configuration[gSecretKey];

            if (string.IsNullOrEmpty(googleClientID) == false && string.IsNullOrEmpty(googleSecret) == false) {
                services.AddAuthentication(options => {
                    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
                    options.DefaultChallengeScheme = GoogleDefaults.AuthenticationScheme;
                }).AddCookie(options => {
                    //options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    //options.Cookie.SameSite = SameSiteMode.Lax;
                }).AddGoogle(options => {
                    options.ClientId = googleClientID;
                    options.ClientSecret = googleSecret;
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                });

                Console.WriteLine($"Added Google auth");
            } else {
                Console.WriteLine($"===============================================================");
                Console.WriteLine($"!!! GOOGLE AUTH NOT SETUP");
                Console.WriteLine($"!!! missing either '{gIDKey}' ({string.IsNullOrEmpty(googleClientID)}) or '{gSecretKey}' ({string.IsNullOrEmpty(googleSecret)})");
                Console.WriteLine($"!!! GOOGLE AUTH NOT SETUP");
                Console.WriteLine($"===============================================================");
            }

            services.AddRazorPages();
            services.AddMemoryCache();
            services.AddHttpContextAccessor();

            services.AddCors(o => o.AddDefaultPolicy(builder => {
                builder.AllowAnyOrigin();
                builder.AllowAnyHeader();
            }));

            services.Configure<DiscordOptions>(Configuration.GetSection("Discord"));
            services.Configure<JaegerNsaOptions>(Configuration.GetSection("JaegerNsa"));
            services.Configure<CensusRealtimeHealthOptions>(Configuration.GetSection("RealtimeHealth"));
            services.Configure<DailyAlertOptions>(Configuration.GetSection("DailyAlert"));
            services.Configure<PsbDriveSettings>(Configuration.GetSection("PsbDrive"));
            services.Configure<PsbRoleMapping>(Configuration.GetSection("PsbRoleMapping"));
            services.Configure<InstanceOptions>(Configuration.GetSection("Instance"));
            services.Configure<HttpConfig>(Configuration.GetSection("Http"));

            services.AddTransient<HttpUtilService>();
            services.AddSingleton<InstanceInfo>();

            services.AddTransient<IActionResultExecutor<ApiResponse>, ApiResponseExecutor>();
            services.AddSingleton<IDbHelper, DbHelper>();
            services.AddSingleton<IDbCreator, DefaultDbCreator>();

            services.AddSingleton<RealtimeMonitor>();
            services.AddSingleton<IEventHandler, Realtime.EventHandler>();
            services.AddSingleton<CommandBus, CommandBus>();
            services.AddSingleton<ICharacterStatGeneratorStore, CharacterStatGeneratorStore>();
            services.AddSingleton<IServiceHealthMonitor, ServiceHealthMonitor>();
            services.AddSingleton<WorldTagManager>();
            services.AddSingleton<RealtimeAlertEventHandler>();

            services.AddHonuQueueServices(); // queue services

            services.AddSingleton<ExtraStatHoster>();

            services.AddHonuDatabasesServices(); // Db services
            services.AddHonuDatabaseReadersServices(); // DB readers
            services.AddHonuCollectionServices(); // Census services
            services.AddHonuCensusReadersServices(); // Census readers
            services.AddHonuRepositoryServices(); // Repositories

            // Hosted services
            services.AddHostedService<DbCreatorStartupService>(); // Have first to ensure DBs exist

            services.AddHostedService<HostedRealtimeMonitor>();
            services.AddHostedService<EventCleanupService>();
            services.AddHostedService<DataBuilderService>();
            services.AddHostedService<WorldDataBroadcastService>();
            services.AddHostedService<RealtimeResubcribeService>();
            services.AddHostedService<WorldOverviewBroadcastService>();
            services.AddHostedService<CharacterStatGeneratorPopulator>();
            services.AddHostedService<FacilityPopulatorStartupService>();
            services.AddHostedService<DirectiveCollectionsPopulator>();
            services.AddHostedService<ObjectiveCollectionsPopulator>();
            services.AddHostedService<ZoneCheckerService>();
            services.AddHostedService<AlertLoadStartupService>();
            services.AddHostedService<RealtimeNetworkBroadcastService>();
            services.AddHostedService<RealtimeNetworkBuilderService>();
            //services.AddHostedService<HostedBackgroundWeaponStatSnapshotCreator>(); // replaced with background weapon stat queue
            services.AddHostedService<RealtimeAlertBroadcastServer>();
            services.AddHostedService<HostedWrappedGenerationProcess>();
            services.AddHostedService<ZoneLastLockedStartupService>();

            // Hosted queues
            services.AddHostedService<HostedBackgroundCharacterCacheQueue>();
            services.AddHostedService<EventProcessService>();
            services.AddHostedService<HostedSessionStarterQueue>();
            services.AddHostedService<HostedJaegerSignInOutProcess>();
            services.AddHostedService<HostedDailyAlertCreator>();
            services.AddHostedService<HostedWatchtowerRecentCleanup>();
            services.AddHostedService<HostedBackgroundWeaponStatQueue>();
            services.AddHostedService<HostedPsbAccountPlaytimeQueue>();
            services.AddHostedService<SessionEndQueueProcessService>();
            services.AddHostedService<HostedAlertEndService>();
            services.AddHostedService<HostedBackgroundFacilityControlEventProcessQueue>(); // what a doozy of a name

            if (Configuration.GetValue<bool>("Discord:Enabled") == true) {
                services.AddSingleton<DiscordWrapper>();
                services.AddHostedService<DiscordService>();
                services.AddHonuDiscord();
            }

            services.AddTransient<CurrentHonuAccount>();

            //services.AddHostedService<PsbNamedImportStartupService>();
            //services.AddHostedService<PsbNamedCheckerService>();
            //services.AddHostedService<BackCreateAlertStartupService>();
            services.AddHostedService<HostedPopulationCreatorService>();

            if (OFFLINE_MODE == true) {
                services.AddHostedService<OfflineDataMockService>();
            } else {
                services.AddHostedService<HostedBackgroundCharacterWeaponStatQueue>();
                services.AddHostedService<HostedBackgroundWeaponPercentileCacheQueue>();
                services.AddHostedService<HostedBackgroundLogoutBuffer>();
                services.AddHostedService<HostedBackgroundCharacterPriorityUpdateQueue>();
                services.AddHostedService<HostedRealtimeMapStateCollector>();

                if (Configuration.GetValue<bool>("StartupServices:AlertPlayer") != false) {
                    services.AddHostedService<AlertParticipantBuilder>();
                }

                //services.AddHostedService<CharacterDatesFixerStartupService>();
                //services.AddHostedService<OutfitMemberFixerStartupService>();
            }

            services.AddTransient<WrappedHub>();

            //services.AddHostedService<KilledTeamIDFixerService>();

            // Needed for Honu on production, which is behind Nginx, will accept the Cookie for Google OAuth2 
            services.Configure<ForwardedHeadersOptions>(options => {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                options.KnownProxies.Add(IPAddress.Parse("64.227.19.86"));
            });

            Console.WriteLine($"!!!!! ConfigureServices finished !!!!!");
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env,
            IHostApplicationLifetime lifetime, ILogger<Startup> logger) {

            WorldIdSanityCheck();

            app.UseForwardedHeaders();

            app.UseMiddleware<ExceptionHandlerMiddleware>();

            /*
            if (env.IsDevelopment()) {
                app.UseDeveloperExceptionPage();
            }
            */

            logger.LogInformation($"Environment: {env.EnvironmentName}");

            //app.UseHttpsRedirection();

            app.UseStaticFiles();
            app.UseRouting();

            app.UseSwagger(doc => { });
            app.UseSwaggerUI(doc => {
                doc.SwaggerEndpoint("/swagger/api/swagger.json", "api");
                doc.RoutePrefix = "api-doc";
                doc.DocumentTitle = "Honu API documentation";
            });

            app.UseAuthentication();
            app.UseAuthorization();

            app.UseCors();
            app.UseEndpoints(endpoints => {
                endpoints.MapControllerRoute(
                    name: "psb",
                    pattern: "psb/{action}",
                    defaults: new { controller = "Psb", action = "Index" }
                );

                endpoints.MapControllerRoute(
                    name: "selectworld",
                    pattern: "/{action}",
                    defaults: new { controller = "Home", action = "SelectWorld" }
                );

                endpoints.MapControllerRoute(
                    name: "charview",
                    pattern: "/c/{charID}/{*.}",
                    defaults: new { controller = "Home", action = "CharacterViewer" }
                );

                endpoints.MapControllerRoute(
                    name: "sessionviewer",
                    pattern: "/s/{sessionID}/{*.}",
                    defaults: new { controller = "Home", action = "SessionViewer" }
                );

                endpoints.MapControllerRoute(
                    name: "outfitviewer",
                    pattern: "/o/{outfitID}/{*.}",
                    defaults: new { controller = "Home", action = "OutfitViewer" }
                );

                endpoints.MapControllerRoute(
                    name: "outfitviewer",
                    pattern: "/outfitsankey/{outfitID}",
                    defaults: new { controller = "Home", action = "OutfitSankey" }
                );

                endpoints.MapControllerRoute(
                    name: "itemstatviewer",
                    pattern: "/i/{itemID}/{*.}",
                    defaults: new { controller = "Home", action = "ItemStatViewer" }
                );

                endpoints.MapControllerRoute(
                    name: "directcharacter",
                    pattern: "/p/{name}/{*.}",
                    defaults: new { controller = "Home", action = "Player" }
                );

                endpoints.MapControllerRoute(
                    name: "outfitreport",
                    pattern: "/report/{*.}",
                    defaults: new { controller = "Home", action = "Report" }
                );

                endpoints.MapControllerRoute(
                    name: "alertview",
                    pattern: "/alert/{alertID}/{outfitID}",
                    defaults: new { controller = "Home", action = "Alert", outfitID = "" }
                );

                endpoints.MapControllerRoute(
                    name: "worlddata",
                    pattern: "/view/{*.}",
                    defaults: new { controller = "Home", action = "Index" }
                );

                endpoints.MapControllerRoute(
                    name: "jaegernsa",
                    pattern: "/jaegernsa/{*.}",
                    defaults: new { controller = "Home", action = "JaegerNsa" }
                );

                endpoints.MapControllerRoute(
                    name: "friendnetwork",
                    pattern: "/friendnetwork/{*.}",
                    defaults: new { controller = "Home", action = "FriendNetwork" }
                );

                endpoints.MapControllerRoute(
                    name: "realtimenetwork",
                    pattern: "/realtimenetwork/{*.}",
                    defaults: new { controller = "Home", action = "RealtimeNetwork" }
                );

                endpoints.MapControllerRoute(
                    name: "accountmanagement",
                    pattern: "/accountmanagement/{*.}",
                    defaults: new { controller = "Home", action = "AccountManagement" }
                );

                endpoints.MapControllerRoute(
                    name: "realtimealert",
                    pattern: "/realtimealert/{*.}",
                    defaults: new { controller = "Home", action = "RealtimeAlert" }
                );

                endpoints.MapControllerRoute(
                    name: "wrapped",
                    pattern: "/wrapped/{id}/{*.}",
                    defaults: new { controller = "Home", action = "Wrapped" }
                );

                endpoints.MapControllerRoute(
                    name: "api",
                    pattern: "/api/{controller}/{action}"
                );

                endpoints.MapHub<WorldDataHub>("/ws/data");
                endpoints.MapHub<WorldOverviewHub>("/ws/overview");
                endpoints.MapHub<ReportHub>("/ws/report");
                endpoints.MapHub<RealtimeMapHub>("/ws/realtime-map");
                endpoints.MapHub<RealtimeNetworkHub>("/ws/realtime-network");
                endpoints.MapHub<RealtimeAlertHub>("/ws/realtime-alert");
                endpoints.MapHub<WrappedHub>("/ws/wrapped");

                endpoints.MapSwagger();
            });
        }

        private void WorldIdSanityCheck() {
            List<short> worlds = new();
            worlds.AddRange(World.PcStreams);
            worlds.AddRange(World.Ps4UsStreams);
            worlds.AddRange(World.Ps4EuStreams);

            foreach (short worldID in worlds) {
                CensusEnvironment? env = CensusEnvironmentHelper.FromWorldID(worldID);
                if (env == null) {
                    throw new SystemException($"Sanity check for census environment for world ID {worldID} failed");
                }
            }
        }

    }
}
