// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.SemanticFunctions;

namespace Microsoft.BotBuilderSamples.Bots
{
    public class EchoBot : ActivityHandler
    {
        protected readonly BotState ConversationState;
        protected readonly BotState UserState;
        protected readonly ILogger Logger;

        public EchoBot(ConversationState conversationState, UserState userState, ILogger<EchoBot> logger)
        {
            ConversationState = conversationState;
            UserState = userState;
            Logger = logger;
        }

        public override async Task OnTurnAsync(ITurnContext turnContext, CancellationToken cancellationToken = default)
        {
            await base.OnTurnAsync(turnContext, cancellationToken);

            // Save any state changes that might have occurred during the turn.
            await ConversationState.SaveChangesAsync(turnContext, false, cancellationToken);
            await UserState.SaveChangesAsync(turnContext, false, cancellationToken);
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var conversationStateAccessors = ConversationState.CreateProperty<SemanticKernelContext>(nameof(SemanticKernelContext));
            var semanticKernelContext = await conversationStateAccessors.GetAsync(turnContext, () => new SemanticKernelContext());

            IKernel kernel = new KernelBuilder()
                    .WithAzureChatCompletionService(
                        "gpt-35-turbo",                   // Azure OpenAI *Deployment Name*
                        "https://lightspeed-team-shared-openai.openai.azure.com/",    // Azure OpenAI *Endpoint*
                        "asdf"
                    )
                    .Build();

            string skPrompt = """
{{$history}}
User: {{$input}}
Bot:
""";
            var promptConfig = new PromptTemplateConfig
            {
                Completion =
                {
                    MaxTokens = 2000,
                    Temperature = 0.2,
                    TopP = 0.5,
                }
            };

            var promptTemplate = new PromptTemplate(
                skPrompt,                        // Prompt template defined in natural language
                promptConfig,                    // Prompt configuration
                kernel                           // SK instance
            );

            var context = kernel.CreateNewContext();
            var historyString = string.Join("\n", semanticKernelContext.History);
            context.Variables["input"] = turnContext.Activity.Text;
            context.Variables["history"] = historyString;

            var functionConfig = new SemanticFunctionConfig(promptConfig, promptTemplate);
            var responseFunction = kernel.RegisterSemanticFunction("ChatPlugin", "chat", functionConfig);
            var replyText = (await responseFunction.InvokeAsync(context)).Result;

            semanticKernelContext.History.Add("User: " + turnContext.Activity.Text);
            semanticKernelContext.History.Add("Bot: " + replyText);

            await turnContext.SendActivityAsync(MessageFactory.Text(replyText, replyText), cancellationToken);
        }

        protected override async Task OnMembersAddedAsync(IList<ChannelAccount> membersAdded, ITurnContext<IConversationUpdateActivity> turnContext, CancellationToken cancellationToken)
        {
            var welcomeText = "Hello and welcome!";
            foreach (var member in membersAdded)
            {
                if (member.Id != turnContext.Activity.Recipient.Id)
                {
                    await turnContext.SendActivityAsync(MessageFactory.Text(welcomeText, welcomeText), cancellationToken);
                }
            }
        }
    }
}
