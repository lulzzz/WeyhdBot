﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Bot.Builder.Ai.LUIS;
using Microsoft.Bot.Builder.BotFramework;
using Microsoft.Bot.Builder.Integration.AspNet.Core;
using Microsoft.Cognitive.LUIS;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WeyhdBot.Dispatch;
using System.IO.Compression;

namespace WeyhdBot
{
    /// <summary>
    /// https://github.com/geffzhang/botbuilder-dotnet/tree/master/samples-final/9.AspNetCore-Luis-Dispatch-Bot
    /// </summary>
    public class Startup
    {
        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public Startup(IHostingEnvironment env)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(env.ContentRootPath)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            Configuration = builder.Build();
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            

            services.AddSingleton(this.Configuration);
            services.AddScoped<IMessageDispatcher>( x => new MessageDispatcher(Configuration.GetSection("WechatOutgoingURI").Value));
            services.AddBot<LuisDispatchBot>(options =>
            {
                options.CredentialProvider = new ConfigurationCredentialProvider(Configuration);

                var (luisModelId, luisSubscriptionKey, luisUri) = GetLuisConfiguration(this.Configuration, "Dispatcher");

                var luisModel = new LuisModel(luisModelId, luisSubscriptionKey, luisUri);

                // If you want to get all intents scorings, add verbose in luisOptions
                var luisOptions = new LuisRequest { Verbose = true };

                var middleware = options.Middleware;
                middleware.Add(new LuisRecognizerMiddleware(luisModel, luisOptions: luisOptions));
            });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }
            app.UseDefaultFiles()
                .UseStaticFiles()
                .UseBotFramework();
        }

        public static (string modelId, string subscriptionId, Uri uri) GetLuisConfiguration(IConfiguration configuration, string serviceName)
        {
            var modelId = configuration.GetSection($"Luis-ModelId-{serviceName}")?.Value;
            var subscriptionId = configuration.GetSection("Luis-SubscriptionKey")?.Value;
            var uri = new Uri(configuration.GetSection("Luis-Url")?.Value);
            return (modelId, subscriptionId, uri);
        }

        public static (string knowledgeBaseId, string subscriptionKey, string uri) GetQnAMakerConfiguration(IConfiguration configuration)
        {
            var knowledgeBaseId = configuration.GetSection("QnAMaker-KnowledgeBaseId")?.Value;
            var subscriptionKey = configuration.GetSection("QnAMaker-SubscriptionKey")?.Value;
            var uri = configuration.GetSection("QnAMaker-Endpoint-Url")?.Value;
            return (knowledgeBaseId, subscriptionKey, uri);
        }
    }
}
