﻿using Mapster;
using Microsoft.AspNet.OData.Builder;
using Microsoft.AspNet.OData.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OData.Edm;
using Newtonsoft.Json;
using Roadie.Library.Configuration;
using Roadie.Api.Services;
using Roadie.Library.Caching;
using Roadie.Library.Data;
using Roadie.Library.Identity;
using System;
using System.IO;
using System.Reflection;
using models = Roadie.Api.Data.Models;
using Roadie.Library.Encoding;

namespace Roadie.Api
{
    public class Startup
    {
        private readonly IConfiguration _configuration;
        private readonly ILoggerFactory _loggerFactory;

        public Startup(IConfiguration configuration, ILoggerFactory loggerFactory)
        {
            this._configuration = configuration;
            this._loggerFactory = loggerFactory;
            TypeAdapterConfig.GlobalSettings.Default.PreserveReference(true);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, ILoggerFactory loggerFactory, IHostingEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            loggerFactory.AddConsole(LogLevel.Trace);

            app.UseCors("Cors");
            app.UseAuthentication();
            //app.UseSwagger();
            //app.UseSwaggerUI(c =>
            //{
            //    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MyComics API v1");
            //    c.RoutePrefix = string.Empty;
            //});
            app.UseMvc(b =>
            {
                b.Select().Expand().Filter().OrderBy().MaxTop(100).Count();
                b.MapODataServiceRoute("odata", "odata", GetEdmModel());
            });
        }

        public static string AssemblyDirectory
        {
            get
            {
                string codeBase = Assembly.GetExecutingAssembly().CodeBase;
                var uri = new UriBuilder(codeBase);
                string path = Uri.UnescapeDataString(uri.Path);
                return Path.GetDirectoryName(path);
            }
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddCors(options => options.AddPolicy("Cors", builder =>
            {
                builder
                .AllowAnyOrigin()
                .AllowAnyMethod()
                .AllowAnyHeader();
            }));

            services.AddSingleton<ITokenService, TokenService>();

            services.AddSingleton<IHttpEncoder, HttpEncoder>();

            //services.AddSingleton<IRoadieSettings, RoadieSettings>(options =>
            //{
            //    var settingsPath = Path.Combine(AssemblyDirectory, "settings.json");
            //    var settings = new RoadieSettings();
            //    if (File.Exists(settingsPath))
            //    {
            //        var settingsFileContents = File.ReadAllText(settingsPath);
            //        var fromSettingsFile = Newtonsoft.Json.JsonConvert.DeserializeObject<RoadieSettings>(settingsFileContents);
            //        if (fromSettingsFile != null)
            //        {
            //            settings.MergeWith(fromSettingsFile);
            //        }
            //    }
            //    return settings;
            //});

            var cacheManager = new MemoryCacheManager(this._loggerFactory.CreateLogger<MemoryCacheManager>(), new CachePolicy(TimeSpan.FromHours(1)));
            services.AddSingleton<ICacheManager>(cacheManager);

            services.AddEntityFrameworkMySql().AddDbContext<ApplicationUserDbContext>(options =>
                options.UseMySql(this._configuration.GetConnectionString("RoadieDatabaseConnection")));

            services.AddEntityFrameworkMySql().AddDbContext<IRoadieDbContext, RoadieDbContext>(options =>
                options.UseMySql(this._configuration.GetConnectionString("RoadieDatabaseConnection")));

            services.AddIdentity<ApplicationUser, ApplicationRole>()
                .AddEntityFrameworkStores<ApplicationUserDbContext>()
                .AddClaimsPrincipalFactory<ApplicationClaimsFactory>();

            services.AddAuthorization(options =>
            {
                options.AddPolicy("Admin", policy => policy.RequireClaim("Admin"));
                options.AddPolicy("Editor", policy => policy.RequireClaim("Editor"));
            });

            services.Configure<IConfiguration>(this._configuration);

            var securityKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(System.Text.Encoding.Default.GetBytes(this._configuration["Tokens:PrivateKey"]));
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            }).AddJwtBearer(config =>
            {
                config.RequireHttpsMetadata = false;
                config.SaveToken = true;
                config.TokenValidationParameters = new TokenValidationParameters()
                {
                    IssuerSigningKey = securityKey,
                    ValidateAudience = true,
                    ValidAudience = this._configuration["Tokens:Audience"],
                    ValidateIssuer = true,
                    ValidIssuer = this._configuration["Tokens:Issuer"],
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
            });

            //services.AddSwaggerGen(c =>
            //{
            //    c.SwaggerDoc("v1", new Info
            //    {
            //        Title = "Roadie API",
            //        Version = "v1"
            //    });
            //});

            services.AddOData();

            services.AddMvc()
                .AddJsonOptions(options =>
                {
                    options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore;
                })
                .SetCompatibilityVersion(CompatibilityVersion.Version_2_1);

        }

        private static IEdmModel GetEdmModel()
        {
            ODataConventionModelBuilder builder = new ODataConventionModelBuilder();
            builder.EntitySet<models.Artist>("Artist");
            builder.EntitySet<models.Label>("Label");
            builder.EntitySet<models.Release>("Release");
            return builder.GetEdmModel();
        }
    }
}