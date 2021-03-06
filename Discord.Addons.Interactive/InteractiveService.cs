﻿
namespace Discord.Addons.Interactive
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Extensions;
    using InteractiveBuilder;
    using Commands;
    using WebSocket;

    /// <summary>
    /// The interactive service.
    /// </summary>
    public class InteractiveService : IDisposable
    {
        /// <summary>
        /// The callbacks.
        /// </summary>
        private readonly Dictionary<ulong, IReactionCallback> callbacks;

        /// <summary>
        /// The default timeout.
        /// </summary>
        private readonly TimeSpan defaultTimeout;

        /// <summary>
        /// Initializes a new instance of the <see cref="InteractiveService"/> class.
        /// </summary>
        /// <param name="discord">
        /// The discord.
        /// </param>
        /// <param name="defaultTimeout">
        /// The default timeout.
        /// </param>
        public InteractiveService(DiscordSocketClient discord, TimeSpan? defaultTimeout = null)
        {
            Discord = discord;
            Discord.ReactionAdded += HandleReactionAsync;

            callbacks = new Dictionary<ulong, IReactionCallback>();
            this.defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(15);
        }

        /// <summary>
        /// Gets the client
        /// </summary>
        public DiscordSocketClient Discord { get; }

        /// <summary>
        /// waits for the next message in the channel
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="fromSourceUser">
        /// The from source user.
        /// </param>
        /// <param name="inSourceChannel">
        /// The in source channel.
        /// </param>
        /// <param name="timeout">
        /// The timeout.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public Task<SocketMessage> NextMessageAsync(SocketCommandContext context, bool fromSourceUser = true,
            bool inSourceChannel = true, TimeSpan? timeout = null)
        {
            var criterion = new Criteria<SocketMessage>();
            if (fromSourceUser)
            {
                criterion.AddCriterion(new EnsureSourceUserCriterion());
            }

            if (inSourceChannel)
            {
                criterion.AddCriterion(new EnsureSourceChannelCriterion());
            }

            return NextMessageAsync(context, criterion, timeout);
        }

        /// <summary>
        /// waits for the next message in the channel
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="criterion">
        /// The criterion.
        /// </param>
        /// <param name="timeout">
        /// The timeout.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task<SocketMessage> NextMessageAsync(SocketCommandContext context,
            ICriterion<SocketMessage> criterion, TimeSpan? timeout = null)
        {
            timeout = timeout ?? defaultTimeout;

            var eventTrigger = new TaskCompletionSource<SocketMessage>();

            Task Func(SocketMessage m) => HandlerAsync(m, context, eventTrigger, criterion);

            context.Client.MessageReceived += Func;

            var trigger = eventTrigger.Task;
            var delay = Task.Delay(timeout.Value);
            var task = await Task.WhenAny(trigger, delay).ConfigureAwait(false);

            context.Client.MessageReceived -= Func;

            if (task == trigger)
            {
                return await trigger.ConfigureAwait(false);
            }

            return null;
        }


        public async Task<InteractiveResponse> NextMessageAsync(SocketCommandContext context,
            InteractiveMessage interactiveMessage)
        {
            var eventTrigger = new TaskCompletionSource<InteractiveResponse>();

            Task Func(SocketMessage m) => HandlerAsync(m, context, eventTrigger, interactiveMessage);

            context.Client.MessageReceived += Func;

            var trigger = eventTrigger.Task;

            var delay = Task.Delay(interactiveMessage.TimeSpan);

            var task = await Task.WhenAny(trigger, delay).ConfigureAwait(false);

            context.Client.MessageReceived -= Func;

            if (task == trigger)
            {
                return await trigger.ConfigureAwait(false);
            }

            return new InteractiveResponse(CriteriaResult.Timeout, null);
        }

        /// <summary>
        /// Sends a message with reaction callbacks
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="reactionCallbackData">
        /// The callbacks.
        /// </param>
        /// <param name="fromSourceUser">
        /// The from source user.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task<IUserMessage> SendMessageWithReactionCallbacksAsync(SocketCommandContext context,
            ReactionCallbackData reactionCallbackData, bool fromSourceUser = true)
        {
            var criterion = new Criteria<SocketReaction>();
            if (fromSourceUser)
            {
                criterion.AddCriterion(new EnsureReactionFromSourceUserCriterion());
            }

            var callback = new InlineReactionCallback(this, context, reactionCallbackData, criterion);
            await callback.DisplayAsync().ConfigureAwait(false);
            return callback.Message;
        }

        /// <summary>
        /// Replies and then deletes the message after the provided time-span
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="content">
        /// The content.
        /// </param>
        /// <param name="isTTS">
        /// The is tts.
        /// </param>
        /// <param name="embed">
        /// The embed.
        /// </param>
        /// <param name="timeout">
        /// The timeout.
        /// </param>
        /// <param name="options">
        /// The options.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task<IUserMessage> ReplyAndDeleteAsync(SocketCommandContext context, string content,
            bool isTTS = false, Embed embed = null, TimeSpan? timeout = null, RequestOptions options = null)
        {
            timeout = timeout ?? defaultTimeout;
            var message = await context.Channel.SendMessageAsync(content, isTTS, embed, options).ConfigureAwait(false);
            _ = Task.Delay(timeout.Value)
                .ContinueWith(_ => message.DeleteAsync().ConfigureAwait(false))
                .ConfigureAwait(false);
            return message;
        }

        /// <summary>
        /// Sends a paginated message in the current channel
        /// </summary>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="pager">
        /// The pager.
        /// </param>
        /// <param name="reactions">
        /// The reactions.
        /// </param>
        /// <param name="criterion">
        /// The criterion.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        public async Task<IUserMessage> SendPaginatedMessageAsync(SocketCommandContext context, PaginatedMessage pager,
            ReactionList reactions, ICriterion<SocketReaction> criterion = null)
        {
            var callback = new PaginatedMessageCallback(this, context, pager, criterion);
            await callback.DisplayAsync(reactions).ConfigureAwait(false);
            return callback.Message;
        }

        /// <summary>
        /// The add reaction callback.
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="callback">
        /// The callback.
        /// </param>
        public void AddReactionCallback(IMessage message, IReactionCallback callback)
            => callbacks[message.Id] = callback;

        /// <summary>
        /// Removes a reaction callback via message
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        public void RemoveReactionCallback(IMessage message) => RemoveReactionCallback(message.Id);

        /// <summary>
        /// Removes a reaction callback via message Id
        /// </summary>
        /// <param name="id">
        /// The id.
        /// </param>
        public void RemoveReactionCallback(ulong id) => callbacks.Remove(id);

        /// <summary>
        /// Clears all reaction callbacks
        /// </summary>
        public void ClearReactionCallbacks() => callbacks.Clear();

        /// <summary>
        /// Unsubscribes from a reactionHandler event
        /// </summary>
        public void Dispose()
        {
            Discord.ReactionAdded -= HandleReactionAsync;
        }

        /// <summary>
        /// Handles messages for NextMessageAsync
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="context">
        /// The context.
        /// </param>
        /// <param name="eventTrigger">
        /// The event trigger.
        /// </param>
        /// <param name="criterion">
        /// The criterion.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        private async Task HandlerAsync(SocketMessage message, SocketCommandContext context,
            TaskCompletionSource<SocketMessage> eventTrigger, ICriterion<SocketMessage> criterion)
        {
            var result = await criterion.JudgeAsync(context, message).ConfigureAwait(false);
            if (result)
            {
                eventTrigger.SetResult(message);
            }
        }


        private async Task HandlerAsync(SocketMessage message, SocketCommandContext context,
            TaskCompletionSource<InteractiveResponse> eventTrigger, InteractiveMessage interactiveMessage)
        {
            var result = await interactiveMessage.MessageCriteria.JudgeAsync(context, message).ConfigureAwait(false);
            if (result)
            {
                eventTrigger.SetResult(await EvaluateResponse(message, interactiveMessage));
            }
        }

        private async Task<InteractiveResponse> EvaluateResponse(SocketMessage message,
            InteractiveMessage interactiveMessage)
        {
            var response = new InteractiveResponse(CriteriaResult.WrongResponse, message);

            if (interactiveMessage.CancelationWords != null)
            {
                if (message.ContainsWords(1, interactiveMessage.CaseSensitive, interactiveMessage.CancelationWords))
                {
                    response.CriteriaResult = CriteriaResult.Canceled;
                    response.Message = null;
                    return response;
                }
            }

            response = EvaluateResponseType(message, interactiveMessage, response);

            if (response.CriteriaResult != CriteriaResult.Success &&
                interactiveMessage.ResponseType != InteractiveTextResponseType.Any)
                await interactiveMessage.SendWrongResponseMessages();
            return response;
        }

        private InteractiveResponse EvaluateResponseType(SocketMessage message, InteractiveMessage interactiveMessage,
            InteractiveResponse response)
        {
            switch (interactiveMessage.ResponseType)
            {
                case InteractiveTextResponseType.Channel:
                    if (message.ContainsChannel())
                        response.CriteriaResult = CriteriaResult.Success;
                    break;
                case InteractiveTextResponseType.User:
                    if (message.ContainsUser())
                        response.CriteriaResult = CriteriaResult.Success;
                    break;
                case InteractiveTextResponseType.Role:
                    if (message.ContainsRole())
                        response.CriteriaResult = CriteriaResult.Success;
                    break;
                case InteractiveTextResponseType.Options:
                    if (message.ContainsWords(1, interactiveMessage.CaseSensitive, interactiveMessage.Options))
                        response.CriteriaResult = CriteriaResult.Success;
                    break;
                case InteractiveTextResponseType.Any:
                    response.CriteriaResult = CriteriaResult.Success;
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return response;
        }

        /// <summary>
        /// Handles a message reaction
        /// </summary>
        /// <param name="message">
        /// The message.
        /// </param>
        /// <param name="channel">
        /// The channel.
        /// </param>
        /// <param name="reaction">
        /// The reaction.
        /// </param>
        /// <returns>
        /// The <see cref="Task"/>.
        /// </returns>
        private async Task HandleReactionAsync(Cacheable<IUserMessage, ulong> message, ISocketMessageChannel channel,
            SocketReaction reaction)
        {
            if (reaction.UserId == Discord.CurrentUser.Id)
            {
                return;
            }

            if (!callbacks.TryGetValue(message.Id, out var callback))
            {
                return;
            }

            if (!(await callback.Criterion.JudgeAsync(callback.Context, reaction).ConfigureAwait(false)))
            {
                return;
            }

            switch (callback.RunMode)
            {
                case RunMode.Async:
                    _ = Task.Run(async () =>
                    {
                        if (await callback.HandleCallbackAsync(reaction).ConfigureAwait(false))
                        {
                            RemoveReactionCallback(message.Id);
                        }
                    });
                    break;
                default:
                    if (await callback.HandleCallbackAsync(reaction).ConfigureAwait(false))
                    {
                        RemoveReactionCallback(message.Id);
                    }

                    break;
            }
        }
        
        public async Task<SocketMessage> StartInteractiveMessage(SocketCommandContext context, InteractiveMessage interactiveMessage)
        {
            //TODO: refactor...
            
            interactiveMessage.Channel = interactiveMessage.Channel ?? context.Channel;

            await interactiveMessage.SendFirstMessages();
            
            var response = await InteractiveResponseResult(context, interactiveMessage);

            
            if (response.CriteriaResult == CriteriaResult.Success) return response.Message;

            await SendCriteriaErrorMessages(interactiveMessage, response);

            return response.Message;
        }

        private async Task<InteractiveResponse> InteractiveResponseResult(SocketCommandContext context, InteractiveMessage interactiveMessage)
        {
            InteractiveResponse response;
            if (interactiveMessage.Repeat == LoopEnabled.True)
                do
                {
                    response = await NextMessageAsync(context, interactiveMessage);
                } while (response.CriteriaResult == CriteriaResult.WrongResponse);
            
            else
                response = await NextMessageAsync(context, interactiveMessage);


            return response;
        }

        private async Task SendCriteriaErrorMessages(InteractiveMessage interactiveMessage, InteractiveResponse response)
        {
            switch (response.CriteriaResult)
            {
                case CriteriaResult.Timeout:
                    await interactiveMessage.SendTimeoutMessages();
                    break;
                case CriteriaResult.Canceled:
                    await interactiveMessage.SendCancellationMessages();
                    break;
                case CriteriaResult.Success:
                    
                    break;
                case CriteriaResult.WrongResponse:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }
}