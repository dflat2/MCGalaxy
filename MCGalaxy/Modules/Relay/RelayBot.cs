﻿/*
    Copyright 2011 MCForge
        
    Dual-licensed under the    Educational Community License, Version 2.0 and
    the GNU General Public License, Version 3 (the "Licenses"); you may
    not use this file except in compliance with the Licenses. You may
    obtain a copy of the Licenses at
    
    http://www.opensource.org/licenses/ecl2.php
    http://www.gnu.org/licenses/gpl-3.0.html
    
    Unless required by applicable law or agreed to in writing,
    software distributed under the Licenses are distributed on an "AS IS"
    BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
    or implied. See the Licenses for the specific language governing
    permissions and limitations under the Licenses.
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MCGalaxy.Commands;
using MCGalaxy.DB;
using MCGalaxy.Events.ServerEvents;

namespace MCGalaxy.Modules.Relay {
    
    public class RelayUser { public string ID, Nick; }
    
    /// <summary> Manages a connection to an external communication service </summary>
    public abstract class RelayBot {
        
        /// <summary> List of commands that cannot be used by relay bot controllers. </summary>
        public List<string> BannedCommands;
        
        /// <summary> List of channels to send public chat messages to </summary>
        public string[] Channels;
        
        /// <summary> List of channels to send staff only messages to </summary>
        public string[] OpChannels;
        
        readonly Player fakeGuest = new Player("RelayBot");
        readonly Player fakeStaff = new Player("RelayBot");
        DateTime lastWho, lastOpWho;
        protected bool resetting;
        protected byte retries;
        
        
        /// <summary> The name of service this relay bot communicates with </summary>
        /// <remarks> IRC, Discord, etc </remarks>
        public abstract string RelayName { get; }
        
        /// <summary> Whether this relay bot is currently enabled </summary>
        public abstract bool Enabled { get; }
        
        /// <summary> Wehther this relay bot is connected to the external communication service </summary>
        public abstract bool Connected { get; }

        /// <summary> List of users allowed to run in-game commands from the external communication service </summary>
        public PlayerList Controllers;
        
        /// <summary> Loads the list of controller users from disc </summary>
        public abstract void LoadControllers();
        
        
        /// <summary> Sends a message to all channels setup for general public chat </summary>
        public void SendPublicMessage(string message) {
            foreach (string chan in Channels) {
                MessageChannel(chan, message);
            }
        }
        
        /// <summary> Sends a message to all channels setup for staff chat only </summary>
        public void SendStaffMessage(string message) {
            foreach (string chan in OpChannels) {
                MessageChannel(chan, message);
            }
        }
        
        /// <summary> Sends a message to the given channel </summary>
        public void MessageChannel(string channel, string message) {
            if (!Enabled || !Connected) return;
            DoMessageChannel(channel, ConvertMessage(message));
        }
        
        /// <summary> Sends a direct message to the given user </summary>
        public void MessageUser(RelayUser user, string message) {
            if (!Enabled || !Connected) return;
            DoMessageUser(user, ConvertMessage(message));
        }
        
        protected abstract void DoMessageChannel(string channel, string message);
        protected abstract void DoMessageUser(RelayUser user, string message);        
        protected abstract string ConvertMessage(string message);
        
        
        /// <summary> Attempts to connect to the external communication service </summary>
        /// <remarks> Does nothing if disabled, already connected, or the server is shutting down </remarks>
        public void Connect() {
            if (!Enabled || Connected || Server.shuttingDown) return;
            Logger.Log(LogType.RelayActivity, "Connecting to {0}...", RelayName);
            
            try {
                LoadBannedCommands();
                DoConnect();
            } catch (Exception e) {
                Logger.Log(LogType.RelayActivity, "Failed to connect to {0}!", RelayName);
                Logger.LogError(e);
            }
        }
        
        /// <summary> Attempts to disconnect from the external communication service </summary>
        /// <remarks> Does nothing if not connected </remarks>
        public void Disconnect(string reason) {
            if (!Connected) return;
            DoDisconnect(reason);
            Logger.Log(LogType.RelayActivity, "Disconnected from {0}!", RelayName);
        }
        
        public void Reset() {
            resetting = true;
            retries   = 0;
            Disconnect(RelayName + " Bot resetting...");
            if (Enabled) Connect();
        }
        
        protected void AutoReconnect() {
            if (resetting || retries >= 3) return;
            
            retries++;
            Connect();
        }
        
        protected abstract void DoConnect();
        protected abstract void DoDisconnect(string reason);

        void LoadBannedCommands() {
            BannedCommands = new List<string>() { "IRCBot", "DiscordBot", "OpRules", "IRCControllers" };
            
            if (!File.Exists("text/irccmdblacklist.txt")) {
                File.WriteAllLines("text/irccmdblacklist.txt", new string[] {
                                       "#Here you can put commands that cannot be used from the IRC bot.",
                                       "#Lines starting with \"#\" are ignored." });
            }
            
            foreach (string line in File.ReadAllLines("text/irccmdblacklist.txt")) {
                if (!line.IsCommentLine()) BannedCommands.Add(line);
            }
        }
        
        
        protected void HookEvents() {
            OnChatEvent.Register(HandleChat, Priority.Low);
            OnChatSysEvent.Register(HandleChatSys, Priority.Low);
            OnChatFromEvent.Register(HandleChatFrom, Priority.Low);
            OnShuttingDownEvent.Register(HandleShutdown, Priority.Low);
        }
        
        protected void UnhookEvents() {
            OnChatEvent.Unregister(HandleChat);
            OnChatSysEvent.Unregister(HandleChatSys);
            OnChatFromEvent.Unregister(HandleChatFrom);
            OnShuttingDownEvent.Unregister(HandleShutdown);
        }
        
        
        static bool FilterIRC(Player pl, object arg) {
            return !pl.Ignores.IRC && !pl.Ignores.IRCNicks.Contains((string)arg);
        } static ChatMessageFilter filterIRC = FilterIRC;
        
        public static void MessageInGame(string srcNick, string message) {
            Chat.Message(ChatScope.Global, message, srcNick, filterIRC);
        }
        
        string Unescape(Player p, string msg) {
            return msg
                .Replace("λFULL", UnescapeFull(p))
                .Replace("λNICK", UnescapeNick(p));
        }
        
        protected virtual string UnescapeFull(Player p) {
            return Server.Config.IRCShowPlayerTitles ? p.FullName : p.group.Prefix + p.ColoredName;
        }
        
        protected virtual string UnescapeNick(Player p) { return p.ColoredName; }
        
        
        void MessageToRelay(ChatScope scope, string msg, object arg, ChatMessageFilter filter) {
            ChatMessageFilter scopeFilter = Chat.scopeFilters[(int)scope];
            fakeGuest.group = Group.DefaultRank;
            
            if (scopeFilter(fakeGuest, arg) && (filter == null || filter(fakeGuest, arg))) {
                SendPublicMessage(msg); return;
            }
            
            fakeStaff.group = Group.Find(Server.Config.IRCControllerRank);
            if (fakeStaff.group == null) fakeStaff.group = Group.NobodyRank;
            
            if (scopeFilter(fakeStaff, arg) && (filter == null || filter(fakeStaff, arg))) {
                SendStaffMessage(msg);
            }
        }

        void HandleChatSys(ChatScope scope, string msg, object arg,
                           ref ChatMessageFilter filter, bool relay) {
            if (relay) MessageToRelay(scope, msg, arg, filter);
        }
        
        void HandleChatFrom(ChatScope scope, Player source, string msg,
                            object arg, ref ChatMessageFilter filter, bool relay) {
            if (relay) MessageToRelay(scope, Unescape(source, msg), arg, filter);
        }
        
        void HandleChat(ChatScope scope, Player source, string msg,
                        object arg, ref ChatMessageFilter filter, bool relay) {
            if (relay) MessageToRelay(scope, Unescape(source, msg), arg, filter);
        }
        
        void HandleShutdown(bool restarting, string message) {
            Disconnect(restarting ? "Server is restarting" : "Server is shutting down");
        }
        
        
        /// <summary> Simplifies some fancy characters (e.g. simplifies ” to ") </summary>
        protected void SimplifyCharacters(StringBuilder sb) {
            // simplify fancy quotes
            sb.Replace("“", "\"");
            sb.Replace("”", "\"");
            sb.Replace("‘", "'");
            sb.Replace("’", "'");
        }
        
        /// <summary> Handles a direct message written by the given user </summary>
        protected void HandleUserMessage(RelayUser user, string message) {
            string[] parts = message.SplitSpaces(2);
            string cmdName = parts[0].ToLower();
            string cmdArgs = parts.Length > 1 ? parts[1] : "";
            
            if (HandleWhoCommand(user, null, cmdName, false)) return;
            Command.Search(ref cmdName, ref cmdArgs);
            
            string error;
            if (!CanUseCommand(user, cmdName, out error)) {
                if (error != null) MessageUser(user, error);
                return;
            }
            
            HandleRelayCommand(user, null, cmdName, cmdArgs);
        }

        /// <summary> Handles a message written by the given user on the given channel </summary>
        protected void HandleChannelMessage(RelayUser user, string channel, string message) {
            message = message.TrimEnd();
            if (message.Length == 0) return;
            
            string[] parts = message.SplitSpaces(3);
            string rawCmd  = parts[0].ToLower();
            bool opchat    = OpChannels.CaselessContains(channel);
            
            if (HandleWhoCommand(user, channel, rawCmd, opchat)) return;
            if (rawCmd.CaselessEq(Server.Config.IRCCommandPrefix)) {
                if (!HandleChannelCommand(user, channel, message, parts)) return;
            }

            if (opchat) {
                Logger.Log(LogType.RelayChat, "(OPs): ({0}) {1}: {2}", RelayName, user.Nick, message);
                Chat.MessageOps(string.Format("To Ops &f-&I({0}) {1}&f- {2}", RelayName, user.Nick,
                                              Server.Config.ProfanityFiltering ? ProfanityFilter.Parse(message) : message));
            } else {
                Logger.Log(LogType.RelayChat, "({0}) {1}: {2}", RelayName, user.Nick, message);
                MessageInGame(user.Nick, string.Format("&I({0}) {1}: &f{2}", RelayName, user.Nick,
                                                       Server.Config.ProfanityFiltering ? ProfanityFilter.Parse(message) : message));
            }
        }
        
        bool HandleChannelCommand(RelayUser user, string channel, string message, string[] parts) {
            string cmdName = parts.Length > 1 ? parts[1].ToLower() : "";
            string cmdArgs = parts.Length > 2 ? parts[2] : "";
            Command.Search(ref cmdName, ref cmdArgs);
            
            string error;
            if (!CanUseCommand(user, cmdName, out error)) {
                if (error != null) MessageChannel(channel, error);
                return false;
            }
            
            return HandleRelayCommand(user, channel, cmdName, cmdArgs);
        }
        
        bool HandleWhoCommand(RelayUser user, string channel, string cmd, bool opchat) {
            bool isWho = cmd == ".who" || cmd == ".players" || cmd == "!players";
            DateTime last = opchat ? lastOpWho : lastWho;
            if (!isWho || (DateTime.UtcNow - last).TotalSeconds <= 5) return false;
            
            try {
                RelayPlayer p = new RelayPlayer(channel, user, this);
                p.group = Group.DefaultRank;
                MessagePlayers(p);
            } catch (Exception e) {
                Logger.LogError(e);
            }
            
            if (opchat) lastOpWho = DateTime.UtcNow;
            else lastWho = DateTime.UtcNow;
            return true;
        }
        
        /// <summary> Outputs the list of online players to the given user </summary>
        protected virtual void MessagePlayers(RelayPlayer p) {
            Command.Find("Players").Use(p, "", p.DefaultCmdData);
        }
        
        bool HandleRelayCommand(RelayUser user, string channel, string cmdName, string cmdArgs) {
            Command cmd = Command.Find(cmdName);
            Player p = new RelayPlayer(channel, user, this);
            if (cmd == null) { p.Message("Unknown command!"); return false; }

            string logCmd = cmdArgs.Length == 0 ? cmdName : cmdName + " " + cmdArgs;
            Logger.Log(LogType.CommandUsage, "/{0} (by {1} from {2})", logCmd, user.Nick, RelayName);
            
            try {
                if (!p.CanUse(cmd)) {
                    CommandPerms.Find(cmd.name).MessageCannotUse(p);
                    return false;
                }
                if (!cmd.SuperUseable) {
                    p.Message(cmd.name + " can only be used in-game.");
                    return false;
                }
                cmd.Use(p, cmdArgs);
            } catch (Exception ex) {
                p.Message("CMD Error: " + ex);
                Logger.LogError(ex);
            }
            return true;
        }

        /// <summary> Returns whether the given relay user is allowed to execute the given command </summary>
        protected bool CanUseCommand(RelayUser user, string cmd, out string error) {
            error = null;
            // Intentionally show no message to non-controller users to avoid spam
            if (!Controllers.Contains(user.ID)) return false;
            // Make sure controller is actually allowed to execute commands right now
            if (!CheckController(user.ID, ref error)) return false;

            if (BannedCommands.CaselessContains(cmd)) {
                error = "You are not allowed to use this command from " + RelayName + ".";
                return false;
            }
            return true;
        }
        
        /// <summary> Returns whether the given controller is currently allowed to execute commands </summary>
        /// <remarks> e.g. a user may have to login before they are allowed to execute commands </remarks>
        protected abstract bool CheckController(string userID, ref string error);
        
        protected sealed class RelayPlayer : Player {
            public readonly string ChannelID;
            public readonly RelayUser User;
            public readonly RelayBot Bot;
            
            public RelayPlayer(string channel, RelayUser user, RelayBot bot) : base(bot.RelayName) {
                group = Group.Find(Server.Config.IRCControllerRank);
                if (group == null) group = Group.NobodyRank;
                
                ChannelID = channel;
                User    = user;
                color   = "&a";
                Bot     = bot;
                
                if (user != null) {
                    string nick = "(" + bot.RelayName + user.Nick + ")";
                    DatabaseID = NameConverter.InvalidNameID(nick);
                }
                SuperName = bot.RelayName;
            }
            
            public override void Message(byte type, string message) {
                if (ChannelID != null) {
                    Bot.MessageChannel(ChannelID, message);
                } else {
                    Bot.MessageUser(User, message);
                }
            }
        }
    }
}