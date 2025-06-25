using System.Text;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;

namespace CS2_SimpleAdmin;

public partial class CS2_SimpleAdmin
{
    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminToAdminSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        Helper.LogCommand(caller, command);

        var utf8BytesString = Encoding.UTF8.GetBytes(
            command.GetCommandString[command.GetCommandString.IndexOf(' ')..]
        );
        var utf8String = Encoding.UTF8.GetString(utf8BytesString);

        foreach (
            var player in Helper
                .GetValidPlayers()
                .Where(p => AdminManager.PlayerHasPermissions(new SteamID(p.SteamID), "@css/chat"))
        )
        {
            if (_localizer != null)
                player.PrintToChat(
                    _localizer[
                        "sa_adminchat_template_admin_with_rank",
                        "[RANK]",
                        caller == null ? _localizer?["sa_console"] ?? "Console" : caller.PlayerName,
                        utf8String
                    ]
                );
        }
    }

    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminCustomSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.GetCommandString[command.GetCommandString.IndexOf(' ')..].Length == 0)
            return;

        var utf8BytesString = Encoding.UTF8.GetBytes(
            command.GetCommandString[command.GetCommandString.IndexOf(' ')..]
        );
        var utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);

        foreach (var player in Helper.GetValidPlayers())
        {
            player.PrintToChat(utf8String.ReplaceColorTags());
        }
    }

    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        if (command.GetCommandString[command.GetCommandString.IndexOf(' ')..].Length == 0)
            return;

        var utf8BytesString = Encoding.UTF8.GetBytes(
            command.GetCommandString[command.GetCommandString.IndexOf(' ')..]
        );
        var utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);

        Dictionary<string, string> ranks = new()
        {
            { "@yappershq/tmod", "[TM]" },
            { "@yappershq/tmodplus", "[TM]" },
            { "@yappershq/mod", "[M]" },
            { "@yappershq/modplus", "[M]" },
            { "@yappershq/smod", "[SM]" },
            { "@yappershq/smodplus", "[SM]" },
            { "@yappershq/admin", "[A]" },
            { "@yappershq/adminplus", "[A]" },
            { "@yappershq/sadmin", "[SA]" },
            { "@yappershq/sadminplus", "[SA]" },
            { "@yappershq/developer", "[Dev]" },
            { "@yappershq/developerplus", "[Dev]" },
            { "@yappershq/core", "[Core]" },
            { "@yappershq/coreplus", "[Core]" },
        };

        string? playerRank = null;
        foreach (string key in ranks.Keys.Reverse())
        {
            if (AdminManager.PlayerHasPermissions(caller, key))
            {
                playerRank = ranks[key];
                break;
            }
        }

        if (_localizer == null)
            return;

        var message = _localizer[
            "sa_adminsay_prefix_with_rank",
            playerRank ?? "",
            caller?.PlayerName ?? "Console",
            utf8String
        ]
            .ToString()
            .ReplaceColorTags();

        foreach (var player in Helper.GetValidPlayers())
        {
            player.PrintToChat(message);
        }
    }

    [CommandHelper(2, "<#userid or name> <message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminPrivateSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var callerName =
            caller == null ? _localizer?["sa_console"] ?? "Console" : caller.PlayerName;

        var targets = GetTarget(command);
        if (targets == null)
            return;
        var playersToTarget = targets
            .Players.Where(player => player is { IsValid: true, IsHLTV: false })
            .ToList();

        //Helper.LogCommand(caller, command);

        var range = command.GetArg(0).Length + command.GetArg(1).Length + 2;
        var message = command.GetCommandString[range..];

        var utf8BytesString = Encoding.UTF8.GetBytes(message);
        var utf8String = Encoding.UTF8.GetString(utf8BytesString);

        playersToTarget.ForEach(player =>
        {
            player.PrintToChat($"({callerName}) {utf8String}".ReplaceColorTags());
        });

        command.ReplyToCommand($" Private message sent!");
    }

    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminCenterSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var utf8BytesString = Encoding.UTF8.GetBytes(
            command.GetCommandString[command.GetCommandString.IndexOf(' ')..]
        );
        var utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);
        Helper.PrintToCenterAll(utf8String.ReplaceColorTags());
    }

    [CommandHelper(1, "<message>")]
    [RequiresPermissions("@css/chat")]
    public void OnAdminHudSayCommand(CCSPlayerController? caller, CommandInfo command)
    {
        var utf8BytesString = Encoding.UTF8.GetBytes(
            command.GetCommandString[command.GetCommandString.IndexOf(' ')..]
        );
        var utf8String = Encoding.UTF8.GetString(utf8BytesString);

        Helper.LogCommand(caller, command);

        VirtualFunctions.ClientPrintAll(
            HudDestination.Alert,
            utf8String.ReplaceColorTags(),
            0,
            0,
            0,
            0
        );
    }
}
