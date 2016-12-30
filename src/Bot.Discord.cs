﻿namespace UB3RB0T
{
    using Discord;
    using Discord.Audio;
    using Discord.WebSocket;
    using Flurl;
    using Flurl.Http;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;
    using UB3RIRC;

    public partial class Bot
    {
        private MessageCache BotResponsesCache = new MessageCache();

        private bool isReady;

        public async Task CreateDiscordBotAsync()
        {
            client = new DiscordSocketClient(new DiscordSocketConfig
            {
                ShardId = this.shard,
                TotalShards = this.Config.Discord.ShardCount,
                AudioMode = AudioMode.Outgoing,
                LogLevel = LogSeverity.Verbose,
                MessageCacheSize = 500,
            });

            client.MessageReceived += Discord_OnMessageReceivedAsync;
            client.Log += Discord_Log;
            client.UserJoined += Discord_UserJoinedAsync;
            client.UserLeft += Discord_UserLeftAsync;
            client.JoinedGuild += Client_JoinedGuildAsync;
            client.LeftGuild += Discord_LeftGuildAsync;
            client.MessageDeleted += Client_MessageDeletedAsync;
            client.MessageUpdated += Client_MessageUpdatedAsync;
            client.UserBanned += Client_UserBannedAsync;
            client.UserUpdated += Client_UserUpdatedAsync;
            client.GuildMemberUpdated += Client_GuildMemberUpdatedAsync;
            client.Ready += () => { this.isReady = true; return Task.CompletedTask; };

            // If user customizeable server settings are supported...support them
            // Currently discord only.
            if (this.Config.SettingsEndpoint != null)
            {
                await this.UpdateSettingsAsync();

                // set a recurring timer to refresh settings
                settingsUpdateTimer = new Timer(async (object state) =>
                {
                    await this.UpdateSettingsAsync();
                }, null, 30000, 30000);
            }

            await client.LoginAsync(TokenType.Bot, this.Config.Discord.Token);
            await client.ConnectAsync();
            await this.client.SetGame(this.Config.Discord.Status);

            if (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey) || !string.IsNullOrEmpty(this.Config.Discord.CarbonStatsKey))
            {
                statsTimer = new Timer(async (object state) =>
                {
                    if (!string.IsNullOrEmpty(this.Config.Discord.DiscordBotsKey))
                    {
                        try
                        {
                            var result = await "https://bots.discord.pw"
                                .AppendPathSegment($"api/bots/{client.CurrentUser.Id}/stats")
                                .WithHeader("Authorization", this.Config.Discord.DiscordBotsKey)
                                .PostJsonAsync(new { shard_id = client.ShardId, shard_count = this.Config.Discord.ShardCount, server_count = client.Guilds.Count() });
                        }
                        catch (Exception ex)
                        {
                            // TODO: Update to using one of the logging classes (Discord/IRC)
                            Console.WriteLine($"Failed to update bots.discord.pw stats: {ex}");
                        }
                    }

                    if (!string.IsNullOrEmpty(this.Config.Discord.CarbonStatsKey))
                    {
                        try
                        {
                            var result = await "https://www.carbonitex.net"
                                .AppendPathSegment("/discord/data/botdata.php")
                                .PostJsonAsync(new { key = this.Config.Discord.CarbonStatsKey, shard_id = client.ShardId, shard_count = this.Config.Discord.ShardCount, servercount = client.Guilds.Count() });
                        }
                        catch (Exception ex)
                        {
                            // TODO: Update to using one of the logging classes (Discord/IRC)
                            Console.WriteLine($"Failed to update carbon stats: {ex}");
                        }
                    }

                }, null, 3600000, 3600000);
            }
        }

        private async Task Client_JoinedGuildAsync(SocketGuild arg)
        {
            if (this.isReady)
            {
                this.AppInsights.TrackEvent("serverJoin");

                var defaultChannel = await arg.GetDefaultChannelAsync();

                if (arg.CurrentUser != null && arg.CurrentUser.GetPermissions(defaultChannel).SendMessages)
                {
                    var owner = await arg.GetOwnerAsync();
                    await defaultChannel.SendMessageAsync($"(HELLO, I AM UB3R-B0T! .halp for info. {owner.Mention} you're the kickass owner-- you can use .admin to configure some stuff.)");
                }
            }
        }

        private async Task Client_GuildMemberUpdatedAsync(SocketGuildUser arg1, SocketGuildUser arg2)
        {
            if (arg1 is IGuildUser guildUserBefore && arg2 is IGuildUser guildUserAfter)
            {
                var settings = SettingsConfig.GetSettings(guildUserBefore.GuildId);
                if (settings.Mod_LogId != 0)
                {
                    var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;

                    if (settings.HasFlag(ModOptions.Mod_LogUserRole))
                    {
                        var rolesAdded = new List<string>();
                        foreach (ulong roleId in guildUserAfter.RoleIds)
                        {
                            if (!guildUserBefore.RoleIds.Any(r => r == roleId))
                            {
                                rolesAdded.Add(guildUserAfter.Guild.Roles.First(g => g.Id == roleId).Name.TrimStart('@'));
                            }
                        }

                        var rolesRemoved = new List<string>();
                        foreach (ulong roleId in guildUserBefore.RoleIds)
                        {
                            if (!guildUserAfter.RoleIds.Any(r => r == roleId))
                            {
                                rolesRemoved.Add(guildUserBefore.Guild.Roles.First(g => g.Id == roleId).Name.TrimStart('@'));
                            }
                        }

                        if (rolesAdded.Count > 0)
                        {
                            string roleText = $"**{guildUserAfter.Username}#{guildUserAfter.Discriminator}** had these roles added: `{string.Join(",", rolesAdded)}`";
                            await modLogChannel?.SendMessageAsync(roleText.Substring(0, Math.Min(roleText.Length, 2000)));
                        }

                        if (rolesRemoved.Count > 0)
                        {
                            string roleText = $"**{guildUserAfter.Username}#{guildUserAfter.Discriminator}** had these roles removed: `{string.Join(",", rolesRemoved)}`";
                            await modLogChannel?.SendMessageAsync(roleText.Substring(0, Math.Min(roleText.Length, 2000)));
                        }
                    }
                }
            }
        }

        private async Task Client_UserUpdatedAsync(SocketUser arg1, SocketUser arg2)
        {
            if (arg1 is IGuildUser guildUserBefore && arg2 is IGuildUser guildUserAfter)
            {
                var settings = SettingsConfig.GetSettings(guildUserBefore.GuildId);
                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserNick))
                {
                    var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;

                    if (guildUserAfter.Nickname != guildUserBefore.Nickname)
                    {
                        if (string.IsNullOrEmpty(guildUserAfter.Nickname))
                        {
                            await modLogChannel?.SendMessageAsync($"{guildUserAfter.Username}#{guildUserAfter.Discriminator} removed their nickname (was {guildUserBefore.Nickname})");
                        }
                        else if (string.IsNullOrEmpty(guildUserBefore.Nickname))
                        {
                            await modLogChannel?.SendMessageAsync($"{guildUserAfter.Username}#{guildUserAfter.Discriminator} set a new nickname to {guildUserAfter.Nickname}");
                        }
                        else
                        {
                            await modLogChannel?.SendMessageAsync($"{guildUserAfter.Username}#{guildUserAfter.Discriminator} changed their nickname from {guildUserBefore.Nickname} to {guildUserAfter.Nickname}");
                        }
                    }
                }
            }
        }

        private async Task Client_UserBannedAsync(SocketUser arg1, SocketGuild arg2)
        {
            // mod log
            var settings = SettingsConfig.GetSettings(arg2.Id);
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserBan))
            {
                string banText = $"{arg1.Username}#{arg1.Discriminator} was banned.";
                var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;

                var botUser = (modLogChannel.Guild as SocketGuild).CurrentUser;

                if (botUser != null && botUser.GetPermissions(modLogChannel).SendMessages)
                {
                    await modLogChannel?.SendMessageAsync(banText);
                }
            }
        }

        private async Task Client_MessageUpdatedAsync(Optional<SocketMessage> arg1, SocketMessage arg2)
        {
            if (arg2 != null && arg2.Channel != null && arg2.Channel is IGuildChannel guildChannel)
            {
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.Guild.Id);

                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogEdit) && arg2.Channel.Id != settings.Mod_LogId && !arg2.Author.IsBot)
                {
                    if (arg1.IsSpecified && arg1.Value.Content != arg2.Content && !string.IsNullOrEmpty(arg1.Value.Content))
                    {
                        string editText = $"**{arg2.Author.Username}** modified in {textChannel.Mention}: `{arg1.Value.Content}` to `{arg2.Content}`";
                        var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;
                        var botUser = (modLogChannel.Guild as SocketGuild).CurrentUser;

                        if (botUser != null && botUser.GetPermissions(modLogChannel).SendMessages)
                        {
                            await modLogChannel.SendMessageAsync(editText.Substring(0, Math.Min(editText.Length, Discord.DiscordConfig.MaxMessageSize)));
                        }
                    }
                }
            }
        }

        private async Task Client_MessageDeletedAsync(ulong arg1, Optional<SocketMessage> arg2)
        {
            var msg = BotResponsesCache.Remove(arg1);
            if (msg != null)
            {
                await msg.DeleteAsync();
            }

            if (arg2.IsSpecified && arg2.Value.Channel is IGuildChannel guildChannel)
            {
                var message = arg2.Value;
                var textChannel = guildChannel as ITextChannel;
                var settings = SettingsConfig.GetSettings(guildChannel.GuildId);

                if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogDelete) && guildChannel.Id != settings.Mod_LogId && !message.Author.IsBot)
                {
                    string delText = "";

                    if (settings.WordCensors.Count() > 0)
                    {
                        var messageWords = message.Content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (var word in messageWords)
                        {
                            if (settings.WordCensors.Contains(word, StringComparer.OrdinalIgnoreCase))
                            {
                                delText = "```Word Censor Triggered```";
                            }
                        }
                    }

                    delText += $"**{message.Author.Username}#{message.Author.Discriminator}** deleted in {textChannel.Mention}: {message.Content}";

                    var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;
                    var botUser = (modLogChannel.Guild as SocketGuild).CurrentUser;

                    if (botUser != null && botUser.GetPermissions(modLogChannel).SendMessages)
                    {
                        await modLogChannel?.SendMessageAsync(delText.Substring(0, Math.Min(delText.Length, Discord.DiscordConfig.MaxMessageSize)));
                    }
                }
            }
        }

        public async Task Discord_OnMessageReceivedAsync(SocketMessage socketMessage)
        {
            // Ignore system and our own messages.
            var message = socketMessage as SocketUserMessage;
            bool isOutbound = false;
            if (message == null || (isOutbound = message.Author.Id == client.CurrentUser.Id))
            {
                if (isOutbound)
                {
                    consoleLogger.Log(LogType.Outgoing, $"\tSending to {message.Channel.Name}: {message.Content}");
                }

                return;
            }

            // grab the settings for this server
            var botGuildUser = (message.Channel is IGuildChannel guildChannel) ? await guildChannel.GetUserAsync(client.CurrentUser.Id) : null;
            var guildUser = message.Author as IGuildUser;
            var guildId = guildUser?.GuildId;
            var settings = SettingsConfig.GetSettings(guildId?.ToString());

            // if it's a globally blocked server, ignore it unless it's the owner
            if (message.Author.Id != this.Config.Discord.OwnerId && guildId != null && this.Config.Discord.BlockedServers.Contains(guildId.Value))
            {
                return;
            }

            // validate server settings don't block this channel;
            // if the ID is in there and it's block, bail. if it's not in there and it's allow mode, also bail.
            if (settings.Channels.Contains(socketMessage.Channel.Id.ToString()) && settings.IsChannelListBlock)
            {
                return;
            }
            else if (!settings.Channels.Contains(socketMessage.Channel.Id.ToString()) && !settings.IsChannelListBlock)
            {
                return;
            }

            // if the user is blocked based on role, return
            var botlessRoleId = guildUser?.Guild.Roles.FirstOrDefault(r => r.Name.ToLowerInvariant() == "botless")?.Id;
            if ((message.Author as IGuildUser)?.RoleIds.Any(r => botlessRoleId != null && r == botlessRoleId.Value) ?? false)
            {
                return;
            }

            // Bail out with help info if it's a PM
            if (message.Channel is IDMChannel && (message.Content.Contains("help") || message.Content.Contains("info") || message.Content.Contains("commands")))
            {
                await message.Channel.SendMessageAsync("Info and commands can be found at: https://ub3r-b0t.com");
                return;
            }

            var textChannel = message.Channel as ITextChannel;
            if (botGuildUser != null && !botGuildUser.GetPermissions(textChannel).SendMessages)
            {
                return;
            }

            // check for word censors
            if (settings.WordCensors.Count() > 0 && botGuildUser != null && botGuildUser.GuildPermissions.ManageMessages)
            {
                var messageWords = message.Content.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var word in messageWords)
                {
                    if (settings.WordCensors.Contains(word, StringComparer.OrdinalIgnoreCase))
                    {
                        await message.DeleteAsync();
                        var dmChannel = await message.Author.CreateDMChannelAsync();
                        await dmChannel.SendMessageAsync($"hi uh sorry but your most recent message was tripped up by the word `{word}` and thusly was deleted. complain to management, i'm just the enforcer");
                        return;
                    }
                }
            }

            // If it's a command, match that before anything else.
            string query = string.Empty;
            bool hasBotMention = message.MentionedUsers.Any(u => u.Id == client.CurrentUser.Id);

            int argPos = 0;
            if (message.HasMentionPrefix(client.CurrentUser, ref argPos))
            {
                query = message.Content.Substring(argPos);
            }
            else if (message.Content.StartsWith(settings.Prefix))
            {
                query = message.Content.Substring(settings.Prefix.Length);
            }

            string command = query.Split(new[] { ' ' }, 2)?[0];

            // if it's a blocked command, bail
            if (settings.IsCommandDisabled(CommandsConfig.Instance, command))
            {
                return;
            }

            // Check discord specific commands prior to general ones.
            if (!string.IsNullOrEmpty(command) && new DiscordCommands().Commands.ContainsKey(command))
            {
                await new DiscordCommands().Commands[command].Invoke(message);
            }
            else
            {
                IDisposable typingState = null;
                if (CommandsConfig.Instance.Commands.ContainsKey(command))
                {
                    typingState = message.Channel.EnterTypingState();
                }

                List<string> responses = await this.ProcessMessageAsync(BotMessageData.Create(message, query), settings);

                foreach (string response in responses)
                {
                    if (!string.IsNullOrEmpty(response))
                    {
                        var sentMessage = await message.Channel.SendMessageAsync(response);
                        BotResponsesCache.Add(message.Id, sentMessage);
                    }
                }

                typingState?.Dispose();
            }
        }

        private async Task Discord_LeftGuildAsync(SocketGuild arg)
        {
            this.AppInsights.TrackEvent("serverLeave");

            if (this.Config.PruneEndpoint != null)
            {
                var req = WebRequest.Create($"{this.Config.PruneEndpoint}?id={arg.Id}");
                await req.GetResponseAsync();
            }
        }

        private async Task Discord_UserLeftAsync(SocketGuildUser arg)
        {
            var settings = SettingsConfig.GetSettings(arg.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Farewell))
            {
                var farewell = settings.Farewell.Replace("%user%", arg.Mention);

                farewell = channelRegex.Replace(farewell, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Captures[0].Value;
                    var channel = arg.Guild.Channels.Where(c => c.Name == channelName).FirstOrDefault();

                    if (channel != null)
                    {
                        return ((ITextChannel)channel).Mention;
                    }

                    return channelName;
                }));

                var farewellChannel = this.client.GetChannel(settings.FarewellId) as ITextChannel ?? await arg.Guild.GetDefaultChannelAsync();
                var botUser = (farewellChannel.Guild as SocketGuild).CurrentUser;

                if (botUser != null && botUser.GetPermissions(farewellChannel).SendMessages)
                {
                    await farewellChannel.SendMessageAsync(farewell);
                }
                else
                {
                    await (await (await farewellChannel.Guild.GetOwnerAsync()).CreateDMChannelAsync()).SendMessageAsync($"Permissions error detected for {farewellChannel.Guild.Name}: Can't send messages to configured farewell channel.");
                }
            }

            // mod log
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserLeave))
            {
                string leaveText = $"{arg.Username}#{arg.Discriminator} left.";
                var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;
                await modLogChannel?.SendMessageAsync(leaveText);
            }
        }

        private async Task Discord_UserJoinedAsync(SocketGuildUser arg)
        {
            var settings = SettingsConfig.GetSettings(arg.Guild.Id);

            if (!string.IsNullOrEmpty(settings.Greeting))
            {
                var greeting = settings.Greeting.Replace("%user%", arg.Mention);

                greeting = channelRegex.Replace(greeting, new MatchEvaluator((Match chanMatch) =>
                {
                    string channelName = chanMatch.Captures[0].Value;
                    var channel = arg.Guild.Channels.Where(c => c.Name == channelName).FirstOrDefault();

                    if (channel != null)
                    {
                        return ((ITextChannel)channel).Mention;
                    }

                    return channelName;
                }));

                var greetingChannel = this.client.GetChannel(settings.GreetingId) as ITextChannel ?? await arg.Guild.GetDefaultChannelAsync();
                var botUser = (greetingChannel.Guild as SocketGuild).CurrentUser;
                if (botUser != null && botUser.GetPermissions(greetingChannel).SendMessages)
                {
                    await greetingChannel.SendMessageAsync(greeting);
                }
                else
                {
                    await (await (await greetingChannel.Guild.GetOwnerAsync()).CreateDMChannelAsync()).SendMessageAsync($"Permissions error detected for {greetingChannel.Guild.Name}: Can't send messages to configured greeting channel.");
                }
            }

            // mod log
            if (settings.Mod_LogId != 0 && settings.HasFlag(ModOptions.Mod_LogUserJoin))
            {
                string joinText = $"{arg.Username}#{arg.Discriminator} joined.";
                var modLogChannel = this.client.GetChannel(settings.Mod_LogId) as ITextChannel;
                await modLogChannel?.SendMessageAsync(joinText);
            }
        }

        private Task Discord_Log(LogMessage arg)
        {
            var logger = Logger.GetConsoleLogger();
            LogType logType = LogType.Debug;
            switch (arg.Severity)
            {
                case LogSeverity.Critical:
                    logType = LogType.Fatal;
                    break;
                case LogSeverity.Error:
                    logType = LogType.Error;
                    break;
                case LogSeverity.Warning:
                    logType = LogType.Warn;
                    break;
                case LogSeverity.Info:
                    logType = LogType.Info;
                    break;
            }

            if (arg.Exception != null)
            {
                this.AppInsights.TrackException(arg.Exception);
            }

            logger.Log(logType, arg.ToString());

            return Task.CompletedTask;
        }
    }
}