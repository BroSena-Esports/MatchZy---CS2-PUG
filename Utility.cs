using System;
using System.IO;
using System.Text.Json;
using System.Linq;
using System.Threading.Tasks;
using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using CounterStrikeSharp.API.Modules.Entities;
using CounterStrikeSharp.API.Modules.Events;
using CounterStrikeSharp.API.Modules.Memory;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Timers;


namespace MatchZy
{
    public partial class MatchZy
    {
        public const string warmupCfgPath = "MatchZy/warmup.cfg";
        public const string knifeCfgPath = "MatchZy/knife.cfg";
        public const string liveCfgPath = "MatchZy/live.cfg";

        private void LoadAdmins() {
            string fileName = "MatchZy/admins.json";
            string filePath = Path.Join(Server.GameDirectory + "/csgo/cfg", fileName);

            if (File.Exists(filePath)) {
                try {
                    using (StreamReader fileReader = File.OpenText(filePath)) {
                        string jsonContent = fileReader.ReadToEnd();
                        if (!string.IsNullOrEmpty(jsonContent)) {
                            JsonSerializerOptions options = new()
                            {
                                AllowTrailingCommas = true,
                            };
                            loadedAdmins = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonContent, options) ?? new Dictionary<string, string>();
                        }
                        else {
                            // Handle the case where the JSON content is empty or null
                            loadedAdmins = new Dictionary<string, string>();
                        }
                    }
                    foreach (var kvp in loadedAdmins) {
                        Log($"[ADMIN] Username: {kvp.Key}, Role: {kvp.Value}");
                    }
                }
                catch (Exception e) {
                    Log($"[LoadAdmins FATAL] An error occurred: {e.Message}");
                }
            }
            else {
                Log("[LoadAdmins] The JSON file does not exist. Creating one with default content");
                Dictionary<string, string> defaultAdmins = new()
                {
                    { "steamid", "" }
                };

                try {
                    JsonSerializerOptions options = new()
                    {
                        WriteIndented = true,
                    };
                    string defaultJson = JsonSerializer.Serialize(defaultAdmins, options);
                    string? directoryPath = Path.GetDirectoryName(filePath);
                    if (directoryPath != null)
                    {
                        if (!Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }
                    }
                    File.WriteAllText(filePath, defaultJson);

                    Log("[LoadAdmins] Created a new JSON file with default content.");
                }
                catch (Exception e) {
                    Log($"[LoadAdmins FATAL] Error creating the JSON file: {e.Message}");
                }
            }
        }

        private bool IsPlayerAdmin(CCSPlayerController? player) {
            if (player == null) return true; // Sent via server, hence should be treated as an admin.
            if (loadedAdmins.ContainsKey(player.SteamID.ToString())) {
                return true;
            }
            return false;
        }
        
        private int GetRealPlayersCount() {
            return playerData.Count;
        }

        private void SendUnreadyPlayersMessage() {
            Log($"[SendUnreadyPlayersMessage] isWarmup: {isWarmup}, matchStarted: {matchStarted}");
            if (isWarmup && !matchStarted) {
                List<string> unreadyPlayers = new List<string>();

                foreach (var key in playerReadyStatus.Keys) {
                    Log($"[SendUnreadyPlayersMessage Player found] key: {key}, playerReadyStatus: {playerReadyStatus[key]}");
                    if (playerReadyStatus[key] == false) {
                        unreadyPlayers.Add(playerData[key].PlayerName);
                    }
                }
                if (unreadyPlayers.Count > 0) {
                    string unreadyPlayerList = string.Join(", ", unreadyPlayers);
                    Server.PrintToChatAll($"{chatPrefix} Unready players: {unreadyPlayerList}. Please type .ready to ready up! [Minimum ready players required: {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}]");
                } else {
                    int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
                    Server.PrintToChatAll($"{chatPrefix} Minimum ready players required {ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}, current ready players: {ChatColors.Green}{countOfReadyPlayers}{ChatColors.Default}");
                }
            }
        }

        private void SendPausedStateMessage() {
            if (isPaused && matchStarted) {
                var pauseTeamName = unpauseData["pauseTeam"];
                if ((string)pauseTeamName == "Admin") {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
                } else if ((string)pauseTeamName == "RoundRestore" && !(bool)unpauseData["t"] && !(bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} Match has been paused because of Round Restore. Both teams need to type {ChatColors.Green}.unpause{ChatColors.Default} to unpause the match");
                } else if ((bool)unpauseData["t"] && !(bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{T_TEAM_NAME}{ChatColors.Default} wants to unpause the match. {ChatColors.Green}{CT_TEAM_NAME}{ChatColors.Default}, please write !unpause to confirm.");
                } else if (!(bool)unpauseData["t"] && (bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{CT_TEAM_NAME}{ChatColors.Default} wants to unpause the match. {ChatColors.Green}{T_TEAM_NAME}{ChatColors.Default}, please write !unpause to confirm.");
                } else if (!(bool)unpauseData["t"] && !(bool)unpauseData["ct"]) {
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{pauseTeamName}{ChatColors.Default} has paused the match. Type .unpause to unpause the match");
                }
            }
        }

        private void ExecWarmupCfg() {
            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", warmupCfgPath))) {
                Log($"[StartWarmup] Starting warmup! Executing Warmup CFG from {warmupCfgPath}");
                Server.ExecuteCommand($"exec {warmupCfgPath}");
            } else {
                Log($"[StartWarmup] Starting warmup! Warmup CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("bot_kick;bot_quota 0;mp_autokick 0;mp_autoteambalance 0;mp_buy_anywhere 0;mp_buytime 15;mp_death_drop_gun 0;mp_free_armor 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_radar_showall 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_solid_teammates 0;mp_spectators_max 20;mp_maxmoney 16000;mp_startmoney 16000;mp_timelimit 0;sv_alltalk 0;sv_auto_full_alltalk_during_warmup_half_end 0;sv_coaching_enabled 1;sv_competitive_official_5v5 1;sv_deadtalk 1;sv_full_alltalk 0;sv_grenade_trajectory 0;sv_hibernate_when_empty 0;mp_weapons_allow_typecount -1;sv_infinite_ammo 0;sv_showimpacts 0;sv_voiceenable 1;sm_cvar sv_mute_players_with_social_penalties 0;sv_mute_players_with_social_penalties 0;tv_relayvoice 1;sv_cheats 0;mp_ct_default_melee weapon_knife;mp_ct_default_secondary weapon_hkp2000;mp_ct_default_primary \"\";mp_t_default_melee weapon_knife;mp_t_default_secondary weapon_glock;mp_t_default_primary;mp_maxrounds 24;mp_warmup_start;mp_warmup_pausetimer 1;mp_warmuptime 9999;cash_team_bonus_shorthanded 0;cash_team_loser_bonus_shorthanded 0;");
            }
        }

        private void StartWarmup() {
            if (unreadyPlayerMessageTimer == null) {
                unreadyPlayerMessageTimer = AddTimer(defaultChatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);
            }
            isWarmup = true;
            ExecWarmupCfg();
        }

        private void StartKnifeRound() {
            // Kills unready players message timer
            if (unreadyPlayerMessageTimer != null) {
                unreadyPlayerMessageTimer.Kill();
                unreadyPlayerMessageTimer = null;
            }
            
            // Setting match phases bools
            isKnifeRound = true;
            matchStarted = true;
            readyAvailable = false;
            isWarmup = false;

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath);

            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", knifeCfgPath))) {
                Log($"[StartKnifeRound] Starting Knife! Executing Knife CFG from {knifeCfgPath}");
                Server.ExecuteCommand($"exec {knifeCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Log($"[StartKnifeRound] Starting Knife! Knife CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("mp_ct_default_secondary \"\";mp_free_armor 1;mp_freezetime 10;mp_give_player_c4 0;mp_maxmoney 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_t_default_secondary \"\";mp_round_restart_delay 3;mp_team_intro_time 0;mp_restartgame 1;mp_warmup_end;");
            }
            
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}KNIFE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}KNIFE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}KNIFE!");
        }

        private void SendSideSelectionMessage() {
            if (isSideSelectionPhase) {
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Please type .stay/.switch");
            }
        }

        private void StartAfterKnifeWarmup() {
            isWarmup = true;
            ExecWarmupCfg();
            knifeWinnerName = knifeWinner == 3 ? CT_TEAM_NAME : T_TEAM_NAME;
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{knifeWinnerName}{ChatColors.Default} Won the knife. Please type .stay/.switch");
            if (sideSelectionMessageTimer == null) {
                sideSelectionMessageTimer = AddTimer(defaultChatTimerDelay, SendSideSelectionMessage, TimerFlags.REPEAT);
            }
        }

        private void StartLive() {

            // Setting match phases bools
            isWarmup = false;
            isSideSelectionPhase = false;
            matchStarted = true;
            isMatchLive = true;
            readyAvailable = false;
            isKnifeRound = false;

            KillPhaseTimers();

            var absolutePath = Path.Join(Server.GameDirectory + "/csgo/cfg", liveCfgPath);

            // We try to find the CFG in the cfg folder, if it is not there then we execute the default CFG.
            if (File.Exists(Path.Join(Server.GameDirectory + "/csgo/cfg", liveCfgPath))) {
                Log($"[StartLive] Starting Live! Executing Live CFG from {liveCfgPath}");
                Server.ExecuteCommand($"exec {liveCfgPath}");
                Server.ExecuteCommand("mp_restartgame 1;mp_warmup_end;");
            } else {
                Log($"[StartLive] Starting Live! Live CFG not found in {absolutePath}, using default CFG!");
                Server.ExecuteCommand("ammo_grenade_limit_default 1;ammo_grenade_limit_flashbang 2;ammo_grenade_limit_total 4;bot_quota 0;cash_player_bomb_defused 300;cash_player_bomb_planted 300;cash_player_damage_hostage -30;cash_player_interact_with_hostage 300;cash_player_killed_enemy_default 300;cash_player_killed_enemy_factor 1;cash_player_killed_hostage -1000;cash_player_killed_teammate -300;cash_player_rescued_hostage 1000;cash_team_elimination_bomb_map 3250;cash_team_elimination_hostage_map_ct 3000;cash_team_elimination_hostage_map_t 3000;cash_team_hostage_alive 0;cash_team_hostage_interaction 600;cash_team_loser_bonus 1400;cash_team_loser_bonus_consecutive_rounds 500;cash_team_planted_bomb_but_defused 800;cash_team_rescued_hostage 600;cash_team_terrorist_win_bomb 3500;cash_team_win_by_defusing_bomb 3500;");
                Server.ExecuteCommand("cash_team_win_by_hostage_rescue 2900;cash_team_win_by_time_running_out_bomb 3250;cash_team_win_by_time_running_out_hostage 3250;ff_damage_reduction_bullets 0.33;ff_damage_reduction_grenade 0.85;ff_damage_reduction_grenade_self 1;ff_damage_reduction_other 0.4;mp_afterroundmoney 0;mp_autokick 0;mp_autoteambalance 0;mp_backup_restore_load_autopause 1;mp_backup_round_auto 1;mp_buy_anywhere 0;mp_buy_during_immunity 0;mp_buytime 20;mp_c4timer 40;mp_ct_default_melee weapon_knife;mp_ct_default_primary \"\";mp_ct_default_secondary weapon_hkp2000;mp_death_drop_defuser 1;mp_death_drop_grenade 2;mp_death_drop_gun 1;mp_defuser_allocation 0;mp_display_kill_assists 1;mp_endmatch_votenextmap 0;mp_forcecamera 1;mp_free_armor 0;mp_freezetime 18;mp_friendlyfire 1;mp_give_player_c4 1;mp_halftime 1;mp_halftime_duration 15;mp_halftime_pausetimer 0;mp_ignore_round_win_conditions 0;mp_limitteams 0;mp_match_can_clinch 1;mp_match_end_restart 0;mp_maxmoney 16000;mp_maxrounds 24;mp_molotovusedelay 0;mp_overtime_enable 1;mp_overtime_halftime_pausetimer 0;mp_overtime_maxrounds 6;mp_overtime_startmoney 10000;mp_playercashawards 1;mp_randomspawn 0;mp_respawn_immunitytime 0;mp_respawn_on_death_ct 0;mp_respawn_on_death_t 0;mp_round_restart_delay 5;mp_roundtime 1.92;mp_roundtime_defuse 1.92;mp_roundtime_hostage 1.92;mp_solid_teammates 1;mp_starting_losses 1;mp_startmoney 800;mp_t_default_melee weapon_knife;mp_t_default_primary \"\";mp_t_default_secondary weapon_glock;mp_teamcashawards 1;mp_timelimit 0;mp_weapons_allow_map_placed 1;mp_weapons_allow_zeus 1;mp_weapons_glow_on_ground 0;mp_win_panel_display_time 3;occlusion_test_async 0;spec_freeze_deathanim_time 0;spec_freeze_panel_extended_time 0;spec_freeze_time 2;spec_freeze_time_lock 2;spec_replay_enable 0;sv_allow_votes 1;sv_auto_full_alltalk_during_warmup_half_end 0;sv_coaching_enabled 1;sv_competitive_official_5v5 1;sv_damage_print_enable 0;sv_deadtalk 1;sv_hibernate_postgame_delay 300;sv_holiday_mode 0;sv_ignoregrenaderadio 0;sv_infinite_ammo 0;sv_occlude_players 1;sv_talk_enemy_dead 0;sv_talk_enemy_living 0;sv_voiceenable 1;tv_relayvoice 1;mp_team_timeout_max 4;mp_team_timeout_time 30;sv_vote_command_delay 0;cash_team_bonus_shorthanded 0;cash_team_loser_bonus_shorthanded 0;mp_spectators_max 20;mp_team_intro_time 0;mp_restartgame 3;mp_warmup_end;");
            }
            
            // This is to reload the map once it is over so that all flags are reset accordingly
            Server.ExecuteCommand("mp_match_end_restart true");
            
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}LIVE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}LIVE!");
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}LIVE!");
        }

        private void KillPhaseTimers() {
            if (unreadyPlayerMessageTimer != null) {
                unreadyPlayerMessageTimer.Kill();
            }
            if (sideSelectionMessageTimer != null) {
                sideSelectionMessageTimer.Kill();
            }
            if (pausedStateTimer != null) {
                pausedStateTimer.Kill();
            }
            unreadyPlayerMessageTimer = null;
            sideSelectionMessageTimer = null;
            pausedStateTimer = null;
        }


        private (int alivePlayers, int totalHealth) GetAlivePlayers(int team) {
            int count = 0;
            int totalHealth = 0;
            foreach (var key in playerData.Keys) {
                if (playerData[key].TeamNum == team) {
                    count++;
                    totalHealth += playerData[key].PlayerPawn.Value.Health;
                }
            }
            return (count, totalHealth);
        }

        private void ResetMatch(bool warmupCfgRequired = true) {

            // Reset match data
            matchStarted = false;
            readyAvailable = true;

            isWarmup = true;
            isKnifeRound = false;
            isSideSelectionPhase = false;
            isMatchLive = false;    
            liveMatchId = -1; 

            lastBackupFileName = "";

            // Unready all players
            foreach (var key in playerReadyStatus.Keys) {
                playerReadyStatus[key] = false;
            }

            // Reset unpause data
            Dictionary<string, object> unpauseData = new()
            {
                { "ct", false },
                { "t", false },
                { "pauseTeam", "" }
            };

            // Reset stop data
            Dictionary<string, object> stopData = new()
            {
                { "ct", false },
                { "t", false }
            };
            Server.ExecuteCommand("mp_unpause_match");
            
            KillPhaseTimers();
            UpdatePlayersMap();
            if (warmupCfgRequired) {
                StartWarmup();
            } else {
                // Since we should be already in warmup phase by this point, we are juts setting up the SendUnreadyPlayersMessage timer
                if (unreadyPlayerMessageTimer == null) {
                    unreadyPlayerMessageTimer = AddTimer(defaultChatTimerDelay, SendUnreadyPlayersMessage, TimerFlags.REPEAT);
                }
            }
        }

        private void UpdatePlayersMap() {
            var playerEntities = Utilities.FindAllEntitiesByDesignerName<CCSPlayerController>("cs_player_controller");
            Log($"[UpdatePlayersMap] CCSPlayerController count: {playerEntities.Count<CCSPlayerController>()}");
            connectedPlayers = 0;

            // Clear the playerData dictionary by creating a new instance to add fresh data.
            playerData = new Dictionary<int, CCSPlayerController>();
            foreach (var player in playerEntities) {
                if (player == null) continue;
                if (player.SteamID == 0) continue; // Player is a bot

                if (loadedAdmins.ContainsKey(player.SteamID.ToString())) {
                    Log($"[ADMIN FOUND] ADMIN STEAM: {player.SteamID}");
                }

                // A player controller still exists after a player disconnects
                // Hence checking whether the player is actually in the server or not
                var iConnectedValue = Schema.GetRef<UInt32>(player.Handle, "CBasePlayerController", "m_iConnected");
                if (iConnectedValue != 0) continue; // 0: Connected, 1: Connecting, 2: Reconnecting, 3: Disconnecting, 4: Disconnected, 5: Reserved

                if (player.UserId.HasValue) {

                    // Updating playerData and playerReadyStatus
                    playerData[player.UserId.Value] = player;

                    // Adding missing player in playerReadyStatus
                    if (!playerReadyStatus.ContainsKey(player.UserId.Value)) {
                        playerReadyStatus[player.UserId.Value] = false;
                    }
                }
                connectedPlayers++;
            }

            // Removing disconnected players from playerReadyStatus
            foreach (var key in playerReadyStatus.Keys.ToList()) {
                if (!playerData.ContainsKey(key)) {
                    // Key is not present in playerData, so remove it from playerReadyStatus
                    playerReadyStatus.Remove(key);
                }
            }
            Log($"[UpdatePlayersMap] CCSPlayerController count: {playerEntities.Count<CCSPlayerController>()}, RealPlayersCount: {GetRealPlayersCount()}");
        }

        private void HandleKnifeWinner(EventCsWinPanelRound @event) {
            // Knife Round code referred from Get5, thanks to the Get5 team for their amazing job!
            (int tAlive, int tHealth) = GetAlivePlayers(2);
            (int ctAlive, int ctHealth) = GetAlivePlayers(3);
            Log($"[KNIFE OVER] CT Alive: {ctAlive} with Total Health: {ctHealth}, T Alive: {tAlive} with Total Health: {tHealth}");
            if (ctAlive > tAlive) {
                knifeWinner = 3;
            } else if (tAlive > ctAlive) {
                knifeWinner = 2;
            } else if (ctHealth > tHealth) {
                knifeWinner = 3;
            } else if (tHealth > ctHealth) {
                knifeWinner = 2;
            } else {
                // Choosing a winner randomly
                Random random = new Random();
                knifeWinner = random.Next(2, 4);
            }

            // Below code is working partially (Winner audio plays correctly for knife winner team, but may display round winner incorrectly)
            // Hence we restart the game with StartAfterKnifeWarmup and allow the winning team to choose side

            @event.FunfactToken = "";

            // Commenting these assignments as they were crashing the server.
            // long empty = 0;
            // @event.FunfactPlayer = null;
            // @event.FunfactData1 = empty;
            // @event.FunfactData2 = empty;
            // @event.FunfactData3 = empty;
            int finalEvent = 10;
            if (knifeWinner == 3) {
                finalEvent = 8;
            } else if (knifeWinner == 2) {
                finalEvent = 9;
            }
            Log($"[KNIFE WINNER] Won by: {knifeWinner}, finalEvent: {@event.FinalEvent}, newFinalEvent: {finalEvent}");
            @event.FinalEvent = finalEvent;
            Log($"[KNIFE WINNER] Won by: {knifeWinner}, Updated finalEvent: {@event.FinalEvent}");
        }

        private void HandleMapChangeCommand(CCSPlayerController? player, string mapName) {
            if (player == null) return;
            if (!IsPlayerAdmin(player)) return;

            if (Server.IsMapValid(mapName)) {
                Server.ExecuteCommand($"changelevel \"{mapName}\"");
            } else {
                player.PrintToChat($"{chatPrefix} Invalid map name!");
            }
        }

        private void HandleReadyRequiredCommand(CCSPlayerController? player, string commandArg) {
            if (!IsPlayerAdmin(player)) return;
            
            if (!string.IsNullOrWhiteSpace(commandArg)) {
                if (int.TryParse(commandArg, out int readyRequired) && readyRequired >= 0 && readyRequired <= 32) {
                    minimumReadyRequired = readyRequired;
                    string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                    ReplyToUserCommand(player, $"Minimum ready players required to start the match are now set to: {minimumReadyRequiredFormatted}");
                    CheckLiveRequired();
                }
                else {
                    ReplyToUserCommand(player, $"Invalid value for readyrequired. Please specify a valid non-negative number. Usage: !readyrequired <number_of_ready_players_required>");
                }
            }
            else {
                string minimumReadyRequiredFormatted = (player == null) ? $"{minimumReadyRequired}" : $"{ChatColors.Green}{minimumReadyRequired}{ChatColors.Default}";
                ReplyToUserCommand(player, $"Current Ready Required: {minimumReadyRequiredFormatted} .Usage: !readyrequired <number_of_ready_players_required>");
            }
        }

        private void CheckLiveRequired() {
            if (!readyAvailable || matchStarted) return;

            int countOfReadyPlayers = playerReadyStatus.Count(kv => kv.Value == true);
            bool liveRequired = false;
            if (minimumReadyRequired == 0) {
                if (countOfReadyPlayers >= connectedPlayers) {
                    liveRequired = true;
                }
            } else if (countOfReadyPlayers >= minimumReadyRequired) {
                liveRequired = true;
            }
            if (liveRequired) {
                HandleMatchStart();
            }
        }

        private void HandleMatchStart() {
            liveMatchId = database.InitMatch(CT_TEAM_NAME, T_TEAM_NAME, "-");
            SetupRoundBackupFile();
            StartDemoRecording();
            if (isKnifeRequired) {
                StartKnifeRound();  
            } else {
                StartLive();
            }
            Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}MatchZy{ChatColors.Default} Plugin by {ChatColors.Green}WD-{ChatColors.Default}");
        }

        private void HandleMatchEnd() {
            if (isMatchLive) {
                string winnerName = GetMatchWinnerName();
                (int ctScore, int tScore) = GetTeamsScore();
                database.SetMatchEndData(liveMatchId, winnerName, ctScore, tScore);
                StopDemoRecording();
                database.WritePlayerStatsToCsv(Server.GameDirectory + "/csgo/MatchZy_Stats", liveMatchId);

                // After a match is ended, warmup is automatically started, hence we do not execute the cfg
                // Also, there is no way to get CVars in CSSharp, which is required to get values like mp_match_restart_delay and other CVars
                // Hence this is a workaround for now.
                ResetMatch(false);
            }
        }

        private string GetMatchWinnerName() {
            (int ctScore, int tScore) = GetTeamsScore();
            if (ctScore > tScore) {
                return CT_TEAM_NAME;
            } else if (tScore > ctScore) {
                return T_TEAM_NAME;
            } else {
                return "Draw";
            }
        }

        private (int ctScore, int tScore) GetTeamsScore() {
            var teamEntities = Utilities.FindAllEntitiesByDesignerName<CCSTeam>("cs_team_manager");
            int ctScore = 0;
            int tScore = 0;
            foreach (var team in teamEntities) {
                if (team.Teamname == "CT") {
                    ctScore = team.Score;
                } else if (team.Teamname == "TERRORIST") {
                    tScore = team.Score;
                }
            }
            return (ctScore, tScore);
        }

        private void HandlePostRoundEndEvent(EventRoundEnd @event) {
            if (isMatchLive) {
                (int ctScore, int tScore) = GetTeamsScore();
                Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{CT_TEAM_NAME}: {ctScore}{ChatColors.Default} - {ChatColors.Green}{T_TEAM_NAME}: {tScore}");
                database.UpdatePlayerStats(liveMatchId, CT_TEAM_NAME, T_TEAM_NAME, playerData);
                string round = (ctScore + tScore).ToString("D2");
                lastBackupFileName = $"matchzy_{liveMatchId}_round{round}.txt";
                Log($"[HandlePostRoundEndEvent] Setting lastBackupFileName to {lastBackupFileName}");

                // One of the team did not use .stop command hence display the proper message after the round has ended.
                if (stopData["ct"] && !stopData["t"]) {
                    Server.PrintToChatAll($"{chatPrefix} The round restore request by {ChatColors.Green}{CT_TEAM_NAME}{ChatColors.Default} was cancelled as the round ended");
                } else if (!stopData["ct"] && stopData["t"]) {
                    Server.PrintToChatAll($"{chatPrefix} The round restore request by {ChatColors.Green}{T_TEAM_NAME}{ChatColors.Default} was cancelled as the round ended");
                }

                // Invalidate .stop requests after a round is completed.
                stopData["ct"] = false;
                stopData["t"] = false;
            }
        }

        private void ReplyToUserCommand(CCSPlayerController? player, string message, bool console = false)
        {
            if (player == null) {
                Server.PrintToConsole($"[MatchZy] {message}");
            } else {
                if (console) {
                    player.PrintToConsole($"[MatchZy] {message}");
                } else {
                    player.PrintToChat($"{chatPrefix} {message}");
                }
            }
        }

        private void PauseMatch(CCSPlayerController? player, CommandInfo? command) {
            if (isMatchLive && isPaused) {
                ReplyToUserCommand(player, "Match is already paused!");
                return;
            }
            if (isMatchLive && !isPaused) {
                if (IsPlayerAdmin(player)) {
                    unpauseData["pauseTeam"] = "Admin";
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}Admin{ChatColors.Default} has paused the match.");
                    if (player == null) {
                        Server.PrintToConsole($"[MatchZy] Admin has paused the match.");
                    } 
                } else {
                    string pauseTeamName = "Admin";
                    unpauseData["pauseTeam"] = "Admin";
                    if (player?.TeamNum == 2) {
                        pauseTeamName = T_TEAM_NAME;
                        unpauseData["pauseTeam"] = T_TEAM_NAME;
                    } else if (player?.TeamNum == 3) {
                        pauseTeamName = CT_TEAM_NAME;
                        unpauseData["pauseTeam"] = CT_TEAM_NAME;
                    }          
                    Server.PrintToChatAll($"{chatPrefix} {ChatColors.Green}{pauseTeamName}{ChatColors.Default} has paused the match. Type .unpause to unpause the match");
                }

                Server.ExecuteCommand("mp_pause_match;");
                isPaused = true;

                if (pausedStateTimer == null) {
                    pausedStateTimer = AddTimer(defaultChatTimerDelay, SendPausedStateMessage, TimerFlags.REPEAT);
                }
            }
        }

        private void Log(string message) {
            Console.WriteLine("[MatchZy] " + message);
        }
    }
}
