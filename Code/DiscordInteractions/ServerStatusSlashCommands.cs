﻿using DSharpPlus.ButtonCommands;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Code.Constants;
using watchtower.Code.ExtensionMethods;
using watchtower.Code.Tracking;
using watchtower.Constants;
using watchtower.Models;
using watchtower.Models.Census;
using watchtower.Models.Db;
using watchtower.Services;
using watchtower.Services.Db;
using watchtower.Services.Repositories;
using static watchtower.Code.DiscordInteractions.DiscordInteractionEnums;

namespace watchtower.Code.DiscordInteractions {

    public class ServerStatusSlashCommands : PermissionSlashCommand {

        public ILogger<ServerStatusSlashCommands> _Logger { set; private get; } = default!;
        public ServerStatusInteractions _Interactions { set; private get; } = default!;

        /// <summary>
        ///     Get some basic information about a server
        /// </summary>
        /// <param name="ctx">provided context</param>
        /// <param name="world">ID of the world to get the status of</param>
        [SlashCommand("server", "Get a server's status")]
        public async Task ServerStatus(InteractionContext ctx,
            [Option("server", "Server")] StatusWorlds world) {

            await ctx.CreateDeferred(false);

            DiscordWebhookBuilder builder = new();
            builder.AddEmbed(await _Interactions.GeneralStatus((short)world));

            builder.AddComponents(ServerStatusButtonCommands.REFRESH_WORLD((short)world));

            DiscordMessage msg = await ctx.EditResponseAsync(builder);
            _Logger.LogDebug($"message created: {msg.Id}");
        }

        [SlashCommand("fights", "Get what fights are currently happening in a server")]
        public async Task Fights(InteractionContext ctx,
            [Option("server", "Server")] StatusWorlds world) {

            short worldID = (short)world;

            await ctx.CreateDeferred(false);

            DiscordWebhookBuilder builder = new();
            builder.AddEmbed(await _Interactions.Fights(worldID));

            builder.AddComponents(ServerStatusButtonCommands.REFRESH_FIGHTS(worldID));

            DiscordMessage msg = await ctx.EditResponseAsync(builder);
            _Logger.LogDebug($"message created: {msg.Id}");
        }

    }

    /// <summary>
    ///     Interactions for buttons on messages
    /// </summary>
    public class ServerStatusButtonCommands : ButtonCommandModule {

        public ILogger<ServerStatusButtonCommands> _Logger { set; private get; } = default!;
        public ServerStatusInteractions _Interactions { set; private get; } = default!;

        /// <summary>
        ///     Button to refresh the general world status in a message
        /// </summary>
        /// <param name="worldID">ID of the world</param>
        public static DiscordButtonComponent REFRESH_WORLD(short worldID) => new(DSharpPlus.ButtonStyle.Secondary, $"@refresh-world.{worldID}", "Refresh");

        /// <summary>
        ///     Button to refresh the general world status in a message
        /// </summary>
        /// <param name="worldID">ID of the world</param>
        public static DiscordButtonComponent REFRESH_FIGHTS(short worldID) => new(DSharpPlus.ButtonStyle.Secondary, $"@refresh-fights.{worldID}", "Refresh");

        /// <summary>
        ///     Refresh a message with updated world information
        /// </summary>
        /// <param name="ctx">Provided context</param>
        /// <param name="worldID">ID of the world to refresh. Is an int, as shorts are not parsed</param>
        [ButtonCommand("refresh-world")]
        public async Task RefreshWorld(ButtonContext ctx, int worldID) {
            await ctx.Interaction.CreateComponentDeferred(true);

            DiscordEmbed response = await _Interactions.GeneralStatus((short)worldID);

            if (ctx.Message != null) {
                _Logger.LogDebug($"putting refreshed message into id {ctx.Message.Id}");
                await ctx.Message.ModifyAsync(Optional.FromValue(response));
            } else {
                await ctx.Interaction.EditResponseErrorEmbed($"message provided in context was null?");
            }
        }

        /// <summary>
        ///     Refresh a message with updated fights for a world
        /// </summary>
        /// <param name="ctx">Provided context</param>
        /// <param name="worldID">ID of the world to refresh. Is an int, as shorts are not parsed</param>
        [ButtonCommand("refresh-fights")]
        public async Task RefreshFights(ButtonContext ctx, int worldID) {
            await ctx.Interaction.CreateComponentDeferred(true);

            DiscordEmbed response = await _Interactions.Fights((short)worldID);

            if (ctx.Message != null) {
                _Logger.LogDebug($"putting refreshed message into id {ctx.Message.Id}");
                await ctx.Message.ModifyAsync(Optional.FromValue(response));
            } else {
                await ctx.Interaction.EditResponseErrorEmbed($"message provided in context was null?");
            }
        }

    }

    /// <summary>
    ///     Backing interactions used by both slash commands and button commands
    /// </summary>
    public class ServerStatusInteractions {

        private readonly ILogger<ServerStatusInteractions> _Logger;
        private readonly ContinentLockDbStore _ContinentLockDb;
        private readonly InstanceInfo _Instance;
        private readonly MetagameEventRepository _MetagameEventRepository;
        private readonly RealtimeMapStateRepository _RealtimeMapStateRepository;
        private readonly MapRepository _MapRepository;

        public static readonly List<uint> InterestedZoneIDs = new() {
            Zone.Indar, Zone.Hossin, Zone.Amerish, Zone.Esamir, Zone.Oshur
        };

        public ServerStatusInteractions(ILogger<ServerStatusInteractions> logger,
            ContinentLockDbStore continentLockDb, InstanceInfo instance,
            MetagameEventRepository metagameEventRepository, RealtimeMapStateRepository realtimeMapStateRepository,
            MapRepository mapRepository) {

            _Logger = logger;
            _ContinentLockDb = continentLockDb;
            _Instance = instance;
            _MetagameEventRepository = metagameEventRepository;
            _RealtimeMapStateRepository = realtimeMapStateRepository;
            _MapRepository = mapRepository;
        }

        /// <summary>
        ///     Create a Discord embed that contains the general status of a world
        /// </summary>
        /// <param name="worldID">ID of the world</param>
        public async Task<DiscordEmbed> GeneralStatus(short worldID) {
            Dictionary<string, TrackedPlayer> players;
            lock (CharacterStore.Get().Players) {
                players = new Dictionary<string, TrackedPlayer>(CharacterStore.Get().Players);
            }
            players = players.Where(iter => iter.Value.WorldID == worldID && iter.Value.Online == true)
                .ToDictionary(key => key.Key, value => value.Value);

            List<ContinentLockEntry> entries = (await _ContinentLockDb.GetAll())
                .Where(iter => iter.WorldID == worldID)
                .OrderBy(iter => iter.Timestamp)
                .ToList();

            List<PsAlert> alerts = AlertStore.Get().GetAlerts().Where(iter => iter.WorldID == worldID).ToList();

            DiscordEmbedBuilder builder = new();
            builder.Title = $"{World.GetName(worldID)}";
            builder.Url = $"https://{_Instance.GetHost()}/view/{worldID}";

            if (entries.Count == 0) {
                builder.Description = $"No entries provided?";
            }

            // entries is already ordered, the intersections gets only zones we care about
            // the union will include zones that for some reason don't have a continent lock entry
            // and they will be interested into the list last
            List<uint> orderedZones = entries.Select(iter => iter.ZoneID).Intersect(InterestedZoneIDs)
                .Union(InterestedZoneIDs).ToList();

            foreach (uint zoneID in orderedZones) {
                builder.Description += $"**{Zone.GetName(zoneID)}: **";

                ZoneState? state = ZoneStateStore.Get().GetZone(worldID, zoneID);
                if (state != null) {
                    if (state.UnstableState == UnstableState.LOCKED) {
                        builder.Description += $"Locked :lock:";
                    } else if (state.UnstableState == UnstableState.SINGLE_LANE) {
                        builder.Description += $"Single lane :regional_indicator_i:";
                    } else if (state.UnstableState == UnstableState.DOUBLE_LANE) {
                        builder.Description += $"Double lane :pause_button:";
                    }
                }

                builder.Description += "\n";

                ContinentLockEntry? entry = entries.FirstOrDefault(iter => iter.ZoneID == zoneID);
                if (entry != null) {
                    builder.Description += $"Last locked: {entry.Timestamp.GetDiscordTimestamp("t")} ({entry.Timestamp.GetDiscordRelativeTimestamp()})\n";
                } else {
                    builder.Description += $"Last locked: unknown (missing from db!)\n";
                }

                List<TrackedPlayer> inZone = players.Values.Where(iter => iter.ZoneID == zoneID).ToList();

                builder.Description += $"Players: {inZone.Count}\n";

                if (inZone.Count > 0) {
                    int vsCount = inZone.Count(iter => iter.TeamID == Faction.VS);
                    int ncCount = inZone.Count(iter => iter.TeamID == Faction.NC);
                    int trCount = inZone.Count(iter => iter.TeamID == Faction.TR);

                    builder.Description += $":purple_square: `VS: {vsCount} / {(vsCount / (decimal)inZone.Count * 100m):n2}%`\n";
                    builder.Description += $":blue_square: `NC: {ncCount} / {(ncCount / (decimal)inZone.Count * 100m):n2}%`\n";
                    builder.Description += $":red_square: `TR: {trCount} / {(trCount / (decimal)inZone.Count * 100m):n2}%`\n";

                    int nsCount = inZone.Count(iter => iter.TeamID == Faction.NS || iter.TeamID == Faction.UNKNOWN);
                    if (nsCount > 0) {
                        builder.Description += $":grey_question: `NS: {nsCount} / {(nsCount / (decimal)inZone.Count * 100m):n2}%`\n";
                    }
                }

                PsAlert? zoneAlert = alerts.FirstOrDefault(iter => iter.ZoneID == zoneID);
                if (zoneAlert != null) {
                    DateTime alertEnd = zoneAlert.Timestamp + TimeSpan.FromSeconds(zoneAlert.Duration);
                    PsMetagameEvent? eventType = await _MetagameEventRepository.GetByID(zoneAlert.AlertID);

                    builder.Description += $"Alert ({eventType?.Name ?? $"<unknown {zoneAlert.AlertID}>"}): {alertEnd.GetDiscordTimestamp("t")} ({alertEnd.GetDiscordRelativeTimestamp()})\n";
                }

                builder.Description += "\n";
            }

            builder.Timestamp = DateTimeOffset.UtcNow;

            _Logger.LogTrace($"Description for {worldID} is {builder.Description.Length} characters");

            return builder;
        }

        /// <summary>
        ///     Build the fights on a server
        /// </summary>
        /// <param name="worldID"></param>
        /// <returns></returns>
        public async Task<DiscordEmbedBuilder> Fights(short worldID) {

            bool debug = false;

            using Activity? root = HonuActivitySource.Root.StartActivity("discord - fights");
            root?.AddTag("honu.worldID", worldID);

            Stopwatch timer = Stopwatch.StartNew();

            DiscordEmbedBuilder builder = new();
            builder.Title = $"Fights on {World.GetName(worldID)}";
            builder.Description = "";
            builder.Timestamp = DateTime.UtcNow;

            List<RealtimeMapState> worldMapState = await _RealtimeMapStateRepository.GetByWorld(worldID);

            if (worldMapState.Count == 0) {
                builder.Description = "No data available. Honu has yet to fetch data for this world, or no data was provided";
                return builder;
            }

            DateTime maxUpdate = worldMapState.Select(iter => iter.Timestamp).Max();

            _Logger.LogDebug($"Max timestamp: {maxUpdate:u}");

            // don't use data that's too old, only within 5 minutes
            List<RealtimeMapState> fights = worldMapState
                .Where(iter => { maxUpdate = default; return DateTime.UtcNow - iter.Timestamp < TimeSpan.FromMinutes(5); })
                .Where(iter => IsInterestingFight(iter))
                .Where(iter => iter.GetUpperBounds() > 0)
                .OrderByDescending(iter => iter.GetUpperBounds())
                .ToList();

            Dictionary<int, PsFacility> regions = (await _MapRepository.GetFacilities()).ToDictionary(iter => iter.RegionID);

            foreach (RealtimeMapState state in fights) {
                using Activity? zoneRoot = HonuActivitySource.Root.StartActivity("discord - fights region");
                zoneRoot?.AddTag("honu.worldID", worldID);
                zoneRoot?.AddTag("honu.regionID", state.RegionID);

                _ = regions.TryGetValue(state.RegionID, out PsFacility? facility);

                // get the owner of the facility
                PsFacilityOwner? owner = null;
                if (facility != null) {
                    owner = _MapRepository.GetZone(worldID, state.ZoneID)?.GetFacilityOwner(facility.FacilityID);
                }

                // get with population was first seen in the hex
                DateTime periodStart = DateTime.UtcNow - TimeSpan.FromMinutes(60);
                DateTime periodEnd = DateTime.UtcNow;
                List<RealtimeMapState> historicalStates = (await _RealtimeMapStateRepository.GetHistoricalByWorldAndRegion(worldID, state.RegionID, periodStart, periodEnd))
                    .OrderByDescending(iter => iter.SaveTimestamp).ToList();

                string s = "Debug output:\n";

                DateTime? fightStarted = null;
                for (int i = historicalStates.Count - 1; i >= 0; --i) {
                    RealtimeMapState historicalState = historicalStates[i];

                    if (debug == true) {
                        s += $"{historicalState.ID} {historicalState.Timestamp:HH:mm:ss} "
                            + $"- {historicalState.FactionBounds.VS} {historicalState.FactionBounds.NC} {historicalState.FactionBounds.TR}"
                            + $"- {historicalState.FactionPercentage.VS} {historicalState.FactionPercentage.NC} {historicalState.FactionPercentage.TR}"
                            + $"\n";
                    }

                    if (historicalState.HasTwoFactions() == true) {
                        continue;
                    }

                    if (i < historicalStates.Count - 1) {
                        RealtimeMapState nextState = historicalStates[i + 1];
                        fightStarted = nextState.SaveTimestamp;
                    } else {
                        fightStarted = historicalStates[i].SaveTimestamp;
                    }

                    _Logger.LogDebug($"history @{historicalState.SaveTimestamp:u} at index {i} is doesn't have 2 factions, started at {fightStarted:u}");

                    break;
                }

                if (debug == true) {
                    builder.Description += s;
                    break;
                }

                if (fightStarted == null) {
                    fightStarted = periodStart;
                }

                builder.Description += "\n";

                TimeSpan diff = DateTime.UtcNow - state.Timestamp;
                string uncertainty = $"(+-{diff:mm\\:ss})";

                string fightDesc = $"**{facility?.Name ?? $"<unknown region {state.RegionID}>"}** - {(owner == null ? $"<unknown owner>" : $"Owned by {Faction.GetName(owner.Owner)}")} {uncertainty}\n";
                fightDesc += $"Start at {fightStarted.Value.GetDiscordTimestamp("t")} ({fightStarted.Value.GetDiscordRelativeTimestamp()})\n";

                if (state.FactionBounds.VS > 0) {
                    fightDesc += $":purple_square: VS: `{GetLowerBounds(state.FactionBounds.VS)} - {state.FactionBounds.VS} ({Math.Round(state.FactionPercentage.VS)}%)`\n";
                }
                if (state.FactionBounds.NC > 0) {
                    fightDesc += $":blue_square: NC: `{GetLowerBounds(state.FactionBounds.NC)} - {state.FactionBounds.NC} ({Math.Round(state.FactionPercentage.NC)}%)`\n";
                }
                if (state.FactionBounds.TR > 0) {
                    fightDesc += $":red_square: TR: `{GetLowerBounds(state.FactionBounds.TR)} - {state.FactionBounds.TR} ({Math.Round(state.FactionPercentage.TR)}%)`\n";
                }

                if (state.CaptureTimeLeftMs > 0) {
                    TimeSpan capTime = TimeSpan.FromMilliseconds(state.CaptureTimeMs);
                    TimeSpan timeLeft = TimeSpan.FromMilliseconds(state.CaptureTimeLeftMs);

                    string left = $"{(capTime - timeLeft):mm\\:ss}";
                    string action = state.ContestingFactionID == state.OwningFactionID ? "defended" : $"capture by {Faction.GetName((short)state.ContestingFactionID)}";

                    fightDesc += $"{left} till {action}\n";
                }

                if (historicalStates.Count >= 2) {
                    // 0 = most recent
                    int trend1 = state.GetLowerBounds();
                    int trend2 = historicalStates[0].GetLowerBounds();
                    int trend3 = historicalStates[1].GetLowerBounds();

                    // TODO 2023-07-25: this is kinda dumb, i'll find a better way to do this
                    fightDesc += "_Trend: ";

                    if (trend1 == trend2 && trend2 == trend3 && trend1 == trend3) {
                        fightDesc += "steady";
                    } else if (trend1 == trend2 && trend2 > trend3) {
                        fightDesc += "declining";
                    } else if (trend1 == trend2 && trend2 < trend3) {
                        fightDesc += "growing";
                    } else if (trend1 < trend2 && trend2 < trend3) {
                        fightDesc += "growing";
                    } else if (trend1 > trend2 && trend2 > trend3) {
                        fightDesc += "rapidly declining";
                    } else {
                        fightDesc += "unchecked";
                    }

                    fightDesc += $" {trend1} => {trend2} => {trend3}_";
                }

                fightDesc += "\n";

                if (builder.Description.Length + fightDesc.Length > 1500) {
                    break;
                }

                builder.Description += fightDesc;
            }

            if (fights.Count == 0) {
                builder.Description += $"No fights are currently happening, or the data Honu is using is incorrect\n\n_Most recent update was at {maxUpdate.GetDiscordFullTimestamp()}_";
            }

            builder.WithFooter($"Not 100% accurate! Generated in {timer.ElapsedMilliseconds}ms");

            return builder;
        }

        /// <summary>
        ///     A fight is interesting if there are 2 factions at it, or one faction has more than 24 (so 48-96) players at it
        /// </summary>
        private bool IsInterestingFight(RealtimeMapState state) {
            return state.HasTwoFactions()
                || state.FactionBounds.VS > 24
                || state.FactionBounds.NC > 24
                || state.FactionBounds.TR > 24;
        }

        /// <summary>
        ///     Get the lower bound of a fight size from the upper bound
        /// </summary>
        /// <param name="maxBound">Max bound of the fight. For example, a 1-12 would be 12</param>
        private int GetLowerBounds(int maxBound) {
            if (maxBound == 0) {
                return 0;
            }

            if (maxBound == 12) {
                return 1;
            }

            if (maxBound == 24) {
                return 12;
            }

            if (maxBound == 48) {
                return 24;
            }

            if (maxBound == 96) {
                return 48;
            }

            // 96+
            if (maxBound == 192) {
                return 96;
            }

            _Logger.LogWarning($"Unchecked max bound {maxBound}");

            return maxBound - 1;
        }

    }

}
