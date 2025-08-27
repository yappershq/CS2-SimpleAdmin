using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.ValveConstants.Protobuf;
using CS2_SimpleAdmin.Managers;
using CS2_SimpleAdmin.Menus;
using CS2_SimpleAdminApi;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBanCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var callerName = caller == null ? _localizer?["sa_console"] ?? "Console" : caller.PlayerName;
        if (command.ArgCount < 2)
            return;
        
        var targets = GetTarget(command);
        if (targets == null) return;
        var playersToTarget = targets.Players.Where(player => player is { IsValid: true, Connected: PlayerConnectedState.PlayerConnected, IsHLTV: false }).ToList();

        if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
        {
            return;
        }
        
        var reason = command.ArgCount >= 3
            ? string.Join(" ", Enumerable.Range(3, command.ArgCount - 3).Select(command.GetArg)).Trim()
            : _localizer?["sa_unknown"] ?? "Unknown";

        reason = string.IsNullOrWhiteSpace(reason) ? _localizer?["sa_unknown"] ?? "Unknown" : reason;
        var time = Helper.ParsePenaltyTime(command.GetArg(2));

        playersToTarget.ForEach(player =>
        {
            if (!caller.CanTarget(player)) return;
            
            if (time < 0 && caller != null && caller.IsValid && Config.OtherSettings.ShowBanMenuIfNoTime)
            {
                DurationMenu.OpenMenu(caller, $"{_localizer?["sa_ban"] ?? "Ban"}: {player.PlayerName}", player,
                    ManagePlayersMenu.BanMenu);
                return;
            }

            Ban(caller, player, time, reason, callerName, BanManager, command);
        });
    }

    internal void Ban(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, BanManager? banManager = null, CommandInfo? command = null, bool silent = false)
    {
        if (Database == null || !player.IsValid || !player.UserId.HasValue) return;
        if (!caller.CanTarget(player)) return;
        if (!CheckValidBan(caller, time)) return;

        // Set default caller name if not provided
        callerName = !string.IsNullOrEmpty(caller?.PlayerName) 
            ? caller.PlayerName 
            : (_localizer?["sa_console"] ?? "Console");
        
        // Get player and admin information
        var playerInfo = PlayersInfo[player.UserId.Value];
        var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

        // Asynchronously handle banning logic
        Task.Run(async () =>
        {
            int? penaltyId = await BanManager.BanPlayer(playerInfo, adminInfo, reason, time);
            SimpleAdminApi?.OnPlayerPenaltiedEvent(playerInfo, adminInfo, PenaltyType.Ban, reason, time, penaltyId);
        });

        // Determine message keys and arguments based on ban time
        var (messageKey, activityMessageKey, centerArgs, adminActivityArgs) = time == 0
            ? ("sa_player_ban_message_perm", "sa_admin_ban_message_perm",
                [reason, "CALLER"],
                ["CALLER", player.PlayerName, reason])
            : ("sa_player_ban_message_time", "sa_admin_ban_message_time",
                new object[] { reason, time, "CALLER" },
                new object[] { "CALLER", player.PlayerName, reason, time });

        // Display center message to the player
        Helper.DisplayCenterMessage(player, messageKey, callerName, centerArgs);

        // Display admin activity message if necessary
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        // Schedule a kick timer
        if (player.UserId.HasValue)
        {
            Helper.KickPlayer(player.UserId.Value, NetworkDisconnectionReason.NETWORK_DISCONNECT_KICKBANADDED, Config.OtherSettings.KickTime);
        }

        // Execute ban command if necessary
        if (UnlockedCommands)
        {
            Server.ExecuteCommand($"banid 1 {new SteamID(player.SteamID).SteamId3}");
        }

        if (!silent)
        {
            if (command == null)
                Helper.LogCommand(caller, $"css_ban {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)} {time} {reason}");
            else
                Helper.LogCommand(caller, command);
        }
        
        Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Ban, _localizer);
    }

    internal void AddBan(CCSPlayerController? caller, SteamID steamid, int time, string reason, BanManager? banManager = null)
    {
        // Set default caller name if not provided
        var callerName = !string.IsNullOrEmpty(caller?.PlayerName) 
            ? caller.PlayerName 
            : (_localizer?["sa_console"] ?? "Console");
        
        var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

        var player = Helper.GetPlayerFromSteamid64(steamid.SteamId64.ToString());

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
                return;

            Ban(caller, player, time, reason, callerName, silent: true);
            //command.ReplyToCommand($"Banned player {player.PlayerName}.");
        }
        else
        {
            if (!caller.CanTarget(steamid))
                return;
            
            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                int? penaltyId = await BanManager.AddBanBySteamid(steamid.SteamId64.ToString(), adminInfo, reason, time);
                SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamid, adminInfo, PenaltyType.Ban, reason, time, penaltyId);
            });
            
            Helper.SendDiscordPenaltyMessage(caller, steamid.SteamId64.ToString(), reason, time, PenaltyType.Ban, _localizer);
        }
    }

    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 1, usage: "<steamid> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnAddBanCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Database == null) return;
        var callerName = caller?.PlayerName ?? _localizer?["sa_console"] ?? "Console";
        if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1))) return;
        if (!Helper.ValidateSteamId(command.GetArg(1), out var steamId) || steamId == null)
        {
            command.ReplyToCommand("Invalid SteamID64.");
            return;
        }

        var steamid = steamId.SteamId64.ToString();
        var reason = command.ArgCount >= 3
            ? string.Join(" ", Enumerable.Range(3, command.ArgCount - 3).Select(command.GetArg)).Trim()
            : _localizer?["sa_unknown"] ?? "Unknown";
        
        reason = string.IsNullOrWhiteSpace(reason) ? _localizer?["sa_unknown"] ?? "Unknown" : reason;

        var time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(2)));
        
        if (!CheckValidBan(caller, time)) return;

        var adminInfo = caller != null && caller.UserId.HasValue
            ? PlayersInfo[caller.UserId.Value]
            : null;

        var player = Helper.GetPlayerFromSteamid64(steamid);

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
                return;

            Ban(caller, player, time, reason, callerName, silent: true);
            //command.ReplyToCommand($"Banned player {player.PlayerName}.");
        }
        else
        {
            if (!caller.CanTarget(new SteamID(steamId.SteamId64)))
                return;
            
            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                int? penaltyId = await BanManager.AddBanBySteamid(steamid, adminInfo, reason, time);
                SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamId, adminInfo, PenaltyType.Ban, reason, time, penaltyId);
            });
            
            Helper.SendDiscordPenaltyMessage(caller, steamid, reason, time, PenaltyType.Ban, _localizer);

            command.ReplyToCommand($"Player with steamid {steamid} is not online. Ban has been added offline.");
        }

        Helper.LogCommand(caller, command);

        if (UnlockedCommands)
            Server.ExecuteCommand($"banid 1 {steamId.SteamId3}");
    }

    [RequiresPermissions("@css/ban")]
    [CommandHelper(minArgs: 1, usage: "<ip> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnBanIpCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Database == null) return;
        var callerName = caller?.PlayerName ?? _localizer?["sa_console"] ?? "Console";
        if (command.ArgCount < 2 || string.IsNullOrEmpty(command.GetArg(1))) return;
        var ipAddress = command.GetArg(1);

        if (!Helper.IsValidIp(ipAddress))
        {
            command.ReplyToCommand($"Invalid IP address.");
            return;
        }

        var reason = command.ArgCount >= 3
            ? string.Join(" ", Enumerable.Range(3, command.ArgCount - 3).Select(command.GetArg)).Trim()
            : _localizer?["sa_unknown"] ?? "Unknown";
        
        reason = string.IsNullOrWhiteSpace(reason) ? _localizer?["sa_unknown"] ?? "Unknown" : reason;

        var time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(2)));

        if (!CheckValidBan(caller, time)) return;

        var adminInfo = caller != null && caller.UserId.HasValue
            ? PlayersInfo[caller.UserId.Value]
            : null;

        var player = Helper.GetPlayerFromIp(ipAddress);

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
                return;

            Ban(caller, player, time, reason, callerName, silent: true);
        }
        else
        {
            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                await BanManager.AddBanByIp(ipAddress, adminInfo, reason, time);
            });

            command.ReplyToCommand($"Player with ip {ipAddress} is not online. Ban has been added offline.");
        }

        Helper.LogCommand(caller, command);
    }

    private bool CheckValidBan(CCSPlayerController? caller, int duration)
    {
        if (caller == null) return true;

        var canPermBan = AdminManager.PlayerHasPermissions(new SteamID(caller.SteamID), "@css/permban");

        if (duration <= 0 && canPermBan == false)
        {
            caller.PrintToChat($"{_localizer!["sa_prefix"]} {_localizer["sa_ban_perm_restricted"]}");
            return false;
        }

        if (duration <= Config.OtherSettings.MaxBanDuration || canPermBan) return true;

        caller.PrintToChat($"{_localizer!["sa_prefix"]} {_localizer["sa_ban_max_duration_exceeded", Config.OtherSettings.MaxBanDuration]}");
        return false;
    }

    [RequiresPermissions("@css/unban")]
    [CommandHelper(minArgs: 1, usage: "<steamid or name or ip> [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnUnbanCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Database == null) return;

        var callerSteamId = caller?.SteamID.ToString() ?? _localizer?["sa_console"] ?? "Console";

        if (command.GetArg(1).Length <= 1)
        {
            command.ReplyToCommand($"Too short pattern to search.");
            return;
        }

        var pattern = command.GetArg(1);
        var reason = command.ArgCount >= 2
            ? string.Join(" ", Enumerable.Range(2, command.ArgCount - 2).Select(command.GetArg)).Trim()
            : _localizer?["sa_unknown"] ?? "Unknown";
        
        reason = string.IsNullOrWhiteSpace(reason) ? _localizer?["sa_unknown"] ?? "Unknown" : reason;

        Task.Run(async () => await BanManager.UnbanPlayer(pattern, callerSteamId, reason));

        Helper.LogCommand(caller, command);

        command.ReplyToCommand($"Unbanned player with pattern {pattern}.");
    }

    [RequiresPermissions("@css/kick")]
    [CommandHelper(minArgs: 1, usage: "<#userid or name> [time in minutes/0 perm] [reason]", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnWarnCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Database == null)
            return;
        var callerName = caller == null ? _localizer?["sa_console"] ?? "Console" : caller.PlayerName;
        if (command.ArgCount < 2)
            return;
        
        var targets = GetTarget(command);
        if (targets == null) return;
        var playersToTarget = targets.Players.Where(player => player.IsValid && player.Connected == PlayerConnectedState.PlayerConnected && !player.IsHLTV).ToList();

        if (playersToTarget.Count > 1 && Config.OtherSettings.DisableDangerousCommands || playersToTarget.Count == 0)
        {
            return;
        }

        WarnManager warnManager = new(Database);

        var time = Math.Max(0, Helper.ParsePenaltyTime(command.GetArg(2)));
        var reason = command.ArgCount >= 3
            ? string.Join(" ", Enumerable.Range(3, command.ArgCount - 3).Select(command.GetArg)).Trim()
            : _localizer?["sa_unknown"] ?? "Unknown";
        
        reason = string.IsNullOrWhiteSpace(reason) ? _localizer?["sa_unknown"] ?? "Unknown" : reason;

        playersToTarget.ForEach(player =>
        {
            if (caller!.CanTarget(player))
            {
                Warn(caller, player, time, reason, callerName, warnManager, command);
            }
        });
    }

    internal void Warn(CCSPlayerController? caller, CCSPlayerController player, int time, string reason, string? callerName = null, WarnManager? warnManager = null, CommandInfo? command = null)
    {
        if (Database == null || !player.IsValid || !player.UserId.HasValue) return;
        if (!caller.CanTarget(player)) return;
        if (!CheckValidBan(caller, time)) return;

        // Set default caller name if not provided
        callerName = !string.IsNullOrEmpty(caller?.PlayerName) 
            ? caller.PlayerName 
            : (_localizer?["sa_console"] ?? "Console");

        // Freeze player pawn if alive
        if (player.PlayerPawn?.Value?.LifeState == (int)LifeState_t.LIFE_ALIVE)
        {
            player.PlayerPawn?.Value?.Freeze();
            AddTimer(5.0f, () => player.PlayerPawn?.Value?.Unfreeze(), CounterStrikeSharp.API.Modules.Timers.TimerFlags.STOP_ON_MAPCHANGE);
        }

        // Get player and admin information
        var playerInfo = PlayersInfo[player.UserId.Value];
        var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

        // Asynchronously handle warning logic
        Task.Run(async () =>
        {
            warnManager ??= new WarnManager(Database);
            int? penaltyId = await warnManager.WarnPlayer(playerInfo, adminInfo, reason, time);
            SimpleAdminApi?.OnPlayerPenaltiedEvent(playerInfo, adminInfo, PenaltyType.Warn, reason, time, penaltyId);

            // Check for warn thresholds and execute punish command if applicable
            var totalWarns = await warnManager.GetPlayerWarnsCount(player.SteamID.ToString());
            if (Config.WarnThreshold.Count > 0)
            {
                string? punishCommand = null;
                var lastKey = Config.WarnThreshold.Keys.Max();

                if (totalWarns >= lastKey)
                    punishCommand = Config.WarnThreshold[lastKey];
                else if (Config.WarnThreshold.TryGetValue(totalWarns, out var value))
                    punishCommand = value;

                if (!string.IsNullOrEmpty(punishCommand))
                {
                    await Server.NextWorldUpdateAsync(() =>
                    {
                        Server.ExecuteCommand(punishCommand.Replace("USERID", playerInfo.UserId.ToString()).Replace("STEAMID64", playerInfo.SteamId?.ToString()));
                    });
                }
            }
        });

        // Determine message keys and arguments based on warning time
        var (messageKey, activityMessageKey, centerArgs, adminActivityArgs) = time == 0
            ? ("sa_player_warn_message_perm", "sa_admin_warn_message_perm",
                new object[] { reason, "CALLER" },
                new object[] { "CALLER", player.PlayerName, reason })
            : ("sa_player_warn_message_time", "sa_admin_warn_message_time",
                [reason, time, "CALLER"],
                ["CALLER", player.PlayerName, reason, time]);

        // Display center message to the playser
        Helper.DisplayCenterMessage(player, messageKey, callerName, centerArgs);

        // Display admin activity message if necessary
        if (caller == null || !SilentPlayers.Contains(caller.Slot))
        {
            Helper.ShowAdminActivity(activityMessageKey, callerName, false, adminActivityArgs);
        }

        // Log the warning command
        if (command == null)
            Helper.LogCommand(caller, $"css_warn {(string.IsNullOrEmpty(player.PlayerName) ? player.SteamID.ToString() : player.PlayerName)} {time} {reason}");
        else
            Helper.LogCommand(caller, command);

        // Send Discord notification for the warning
        Helper.SendDiscordPenaltyMessage(caller, player, reason, time, PenaltyType.Warn, _localizer);
    }
    
    internal void AddWarn(CCSPlayerController? caller, SteamID steamid, int time, string reason, WarnManager? warnManager = null)
    {
        // Set default caller name if not provided
        var callerName = !string.IsNullOrEmpty(caller?.PlayerName) 
            ? caller.PlayerName 
            : (_localizer?["sa_console"] ?? "Console");
        
        var adminInfo = caller != null && caller.UserId.HasValue ? PlayersInfo[caller.UserId.Value] : null;

        var player = Helper.GetPlayerFromSteamid64(steamid.SteamId64.ToString());

        if (player != null && player.IsValid)
        {
            if (!caller.CanTarget(player))
                return;

            Warn(caller, player, time, reason, callerName);
            //command.ReplyToCommand($"Banned player {player.PlayerName}.");
        }
        else
        {
            if (!caller.CanTarget(steamid))
                return;
            
            // Asynchronous ban operation if player is not online or not found
            Task.Run(async () =>
            {
                int? penaltyId = await WarnManager.AddWarnBySteamid(steamid.SteamId64.ToString(), adminInfo, reason, time);
                SimpleAdminApi?.OnPlayerPenaltiedAddedEvent(steamid, adminInfo, PenaltyType.Warn, reason, time, penaltyId);

                // Check for warn thresholds and execute punish command if applicable
                var totalWarns = await WarnManager.GetPlayerWarnsCount(steamid.SteamId64.ToString());
                if (Config.WarnThreshold.Count > 0)
                {
                    string? punishCommand = null;
                    var lastKey = Config.WarnThreshold.Keys.Max();

                    if (totalWarns >= lastKey)
                        punishCommand = Config.WarnThreshold[lastKey];
                    else if (Config.WarnThreshold.TryGetValue(totalWarns, out var value))
                        punishCommand = value;

                    if (!string.IsNullOrEmpty(punishCommand))
                    {
                        await Server.NextWorldUpdateAsync(() =>
                        {
                            Server.ExecuteCommand(punishCommand.Replace("STEAMID64", steamid.SteamId64.ToString()));
                        });
                    }
                }
            });
            
            Helper.SendDiscordPenaltyMessage(caller, steamid.SteamId64.ToString(), reason, time, PenaltyType.Warn, _localizer);
        }
    }
    
    [RequiresPermissions("@css/kick")]
    [CommandHelper(minArgs: 1, usage: "<steamid or name or ip>", whoCanExecute: CommandUsage.CLIENT_AND_SERVER)]
    public void OnUnwarnCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (Database == null) return;

        if (command.GetArg(1).Length <= 1)
        {
            command.ReplyToCommand($"Too short pattern to search.");
            return;
        }

        var pattern = command.GetArg(1);

        Task.Run(async () => await WarnManager.UnwarnPlayer(pattern));

        Helper.LogCommand(caller, command);
        command.ReplyToCommand($"Unwarned player with pattern {pattern}.");
    }
}