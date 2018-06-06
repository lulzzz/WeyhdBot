﻿using Microsoft.Bot;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Ai.LUIS;
using Microsoft.Bot.Builder.Ai.QnA;
using Microsoft.Bot.Builder.Core.Extensions;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WeyhdBot.Dispatch;
using WeyhdBot.Extensions;

namespace WeyhdBot
{
    /// <summary>
    /// This bot demonstrates how to use a LUIS model generated by the Dispatch tool.
    /// 
    /// See https://aka.ms/bot-dispatch for more information on the Dispatch tool.
    /// 
    /// This example assumes the LUIS app from Dispatch is generated from two LUIS apps and one QnAMaker service:
    ///  * A LUIS app named "homeautomation", for handling messages about turning lights and appliances on and off
    ///  * A LUIS app named "weather", for handling requests for weather forecasts and conditions
    ///  * A QnA Maker service named "faq", for answering questions about using the hypothetical home automation system.
    ///  
    /// Generate the LUIS app using Dispatch tool. The tool creates a LUIS app with one intent
    /// for each of the constituent LUIS apps and QnAMaker services: "l_homeautomation", "l_weather", "q_faq".
    /// The bot can use the intents to route the user messages to the appropriate LUIS app or QnA Maker.
    /// 
    /// Dispatching the messages to the original LUIS apps allows the bot to get entities from the these apps. 
    /// The LUIS app generated by the Dispatch tool doesn't contain entity information.
    /// </summary>
    public class LuisDispatchBot : IBot
    {
        public LuisDispatchBot(IConfiguration configuration, IMessageDispatcher dispatcher)
        {
            var (luisModelId, luisSubscriptionKey, luisUri) = Startup.GetLuisConfiguration(configuration, "HomeAutomation");
            this.luisModelHomeAutomation = new LuisModel(luisModelId, luisSubscriptionKey, luisUri);

            (luisModelId, luisSubscriptionKey, luisUri) = Startup.GetLuisConfiguration(configuration, "Weather");
            this.luisModelWeather = new LuisModel(luisModelId, luisSubscriptionKey, luisUri);

            var (knowledgeBaseId, subscriptionKey, qnaUrl) = Startup.GetQnAMakerConfiguration(configuration);
            this.qnaEndpoint = new QnAMakerEndpoint
            {
                // add subscription key for QnA and knowledge base ID
                EndpointKey = subscriptionKey,
                KnowledgeBaseId = knowledgeBaseId,
                Host = qnaUrl
            };
            messageDispatcher = dispatcher;
        }

        private IMessageDispatcher messageDispatcher;

        private QnAMakerEndpoint qnaEndpoint;

        // App ID for a LUIS model named "homeautomation"
        private LuisModel luisModelHomeAutomation;

        // App ID for a LUIS model named "weather"
        private LuisModel luisModelWeather;

        public async Task OnTurn(ITurnContext context)
        {
            if (context.Activity.Type is ActivityTypes.Message)
            {
                // Get the intent recognition result from the context object.
                var dispatchResult = context.Services.Get<RecognizerResult>(LuisRecognizerMiddleware.LuisRecognizerResultKey) as RecognizerResult;
                var topIntent = dispatchResult?.GetTopScoringIntent();

                if (topIntent == null)
                {
                    var message = "Unable to get the top intent.";
                    await SendActivity(context, message);
                }
                else
                {
                    if (topIntent.Value.score < 0.3)
                    {
                        var message = "I'm not very sure what you want but will try to send your request.";
                        await SendActivity(context, message);
                    }

                    await DispatchToTopIntent(context, topIntent);
                }
            }
            else if (context.Activity.Type is ActivityTypes.ConversationUpdate)
            {
                await WelcomeMembersAdded(context, "Hello and welcome to the LUIS Dispatch sample bot. This bot dispatches messages to LUIS apps and QnA, using a LUIS model generated by the Dispatch tool.");
            }
        }

        private async Task DispatchToTopIntent(ITurnContext context, (string intent, double score)? topIntent)
        {
            switch (topIntent.Value.intent.ToLowerInvariant())
            {
                case "l_homeautomation":
                    await DispatchToLuisModel(context, this.luisModelHomeAutomation, "home automation");

                    // Here, you can add code for calling the hypothetical home automation service, passing in any entity information that you need
                    break;
                case "l_weather":
                    await DispatchToLuisModel(context, this.luisModelWeather, "weather");

                    // Here, you can add code for calling the hypothetical weather service, passing in any entity information that you need
                    break;
                case "none":
                // You can provide logic here to handle the known None intent (none of the above).
                // In this example we fall through to the QnA intent.
                case "q_faq":
                    await DispatchToQnAMaker(context, this.qnaEndpoint, "FAQ");
                    break;
                default:
                    // The intent didn't match any case, so just display the recognition results.
                    await context.SendActivity($"Dispatch intent: {topIntent.Value.intent} ({topIntent.Value.score}).");

                    break;
            }
        }

        private async Task DispatchToQnAMaker(ITurnContext context, QnAMakerEndpoint qnaOptions, string appName)
        {
            QnAMaker qnaMaker = new QnAMaker(qnaOptions);
            if (!string.IsNullOrEmpty(context.Activity.Text))
            {
                var results = await qnaMaker.GetAnswers(context.Activity.Text.Trim()).ConfigureAwait(false);
                if (results.Any())
                {
                    var message = results.First().Answer;
                    await SendActivity(context, message);
                }
                else
                {
                    var message = $"Couldn't find an answer in the {appName}.";
                    await SendActivity(context, message);
                }
            }
        }

        private async Task DispatchToLuisModel(ITurnContext context, LuisModel luisModel, string appName)
        {
            var message = $"Sending your request to the {appName} system ...";
            await SendActivity(context, message);

            var (intents, entities) = await RecognizeAsync(luisModel, context.Activity.Text);

            message = $"Intents detected by the {appName} app:\n\n{string.Join("\n\n", intents)}";
            await SendActivity(context, message);

            if (entities.Count() > 0)
            {
                message = $"The following entities were found in the message:\n\n{string.Join("\n\n", entities)}";
                await SendActivity(context, message);
            }
        }

        private static async Task<(IEnumerable<string> intents, IEnumerable<string> entities)> RecognizeAsync(LuisModel luisModel, string text)
        {
            var luisRecognizer = new LuisRecognizer(luisModel);
            var recognizerResult = await luisRecognizer.Recognize(text, System.Threading.CancellationToken.None);

            // list the intents
            var intents = new List<string>();
            foreach (var intent in recognizerResult.Intents)
            {
                intents.Add($"'{intent.Key}', score {intent.Value}");
            }

            // list the entities
            var entities = new List<string>();
            foreach (var entity in recognizerResult.Entities)
            {
                if (!entity.Key.ToString().Equals("$instance"))
                {
                    entities.Add($"{entity.Key}: {entity.Value.First}");
                }
            }

            return (intents, entities);
        }

        private async Task WelcomeMembersAdded(ITurnContext context, string welcomeMessage)
        {
            foreach (var newMember in context.Activity.MembersAdded)
            {
                if (newMember.Id != context.Activity.Recipient.Id)
                {
                    await SendActivity(context, welcomeMessage);
                }
            }
        }

        private async Task SendActivity(ITurnContext context, string welcomeMessage)
        {
            var activity = context.MakeMessage(welcomeMessage);
            await messageDispatcher.DispatchAsync(context, activity);
        }
    }
}