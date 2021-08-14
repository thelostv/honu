﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Code;
using watchtower.Code.Constants;
using watchtower.Code.ExtensionMethods;
using watchtower.Constants;
using watchtower.Models;
using watchtower.Models.Census;
using watchtower.Models.Events;
using watchtower.Services;
using watchtower.Services.Db;
using watchtower.Services.Repositories;

namespace watchtower.Realtime {

    public class EventHandler : IEventHandler {

        private readonly ILogger<EventHandler> _Logger;

        private readonly IKillEventDbStore _KillEventDb;
        private readonly IExpEventDbStore _ExpEventDb;
        private readonly ISessionDbStore _SessionDb;

        private readonly IBackgroundCharacterCacheQueue _CacheQueue;
        private readonly IBackgroundSessionStarterQueue _SessionQueue;
        private readonly ICharacterRepository _CharacterRepository;

        private readonly List<JToken> _Recent;

        public EventHandler(ILogger<EventHandler> logger,
            IKillEventDbStore killEventDb, IExpEventDbStore expDb,
            IBackgroundCharacterCacheQueue cacheQueue, ICharacterRepository charRepo,
            ISessionDbStore sessionDb, IBackgroundSessionStarterQueue sessionQueue) {

            _Logger = logger;

            _Recent = new List<JToken>();

            _KillEventDb = killEventDb ?? throw new ArgumentNullException(nameof(killEventDb));
            _ExpEventDb = expDb ?? throw new ArgumentNullException(nameof(expDb));
            _SessionDb = sessionDb ?? throw new ArgumentNullException(nameof(sessionDb));

            _CacheQueue = cacheQueue ?? throw new ArgumentNullException(nameof(cacheQueue));
            _SessionQueue = sessionQueue ?? throw new ArgumentNullException(nameof(sessionQueue));
            _CharacterRepository = charRepo ?? throw new ArgumentNullException(nameof(charRepo));
        }

        public async Task Process(JToken ev) {
            if (_Recent.Contains(ev)) {
                _Logger.LogInformation($"Skipping duplicate event {ev}");
                return;
            }

            _Recent.Add(ev);
            if (_Recent.Count > 10) {
                _Recent.RemoveAt(0);
            }

            string? type = ev.Value<string?>("type");

            if (type == "serviceMessage") {
                JToken? payloadToken = ev.SelectToken("payload");
                if (payloadToken == null) {
                    _Logger.LogWarning($"Missing 'payload' from {ev}");
                    return;
                }

                string? eventName = payloadToken.Value<string?>("event_name");

                if (eventName == null) {
                    _Logger.LogWarning($"Missing 'event_name' from {ev}");
                } else if (eventName == "PlayerLogin") {
                    await _ProcessPlayerLogin(payloadToken);
                } else if (eventName == "PlayerLogout") {
                    await _ProcessPlayerLogout(payloadToken);
                } else if (eventName == "GainExperience") {
                    await _ProcessExperience(payloadToken);
                } else if (eventName == "Death") {
                    await _ProcessDeath(payloadToken);
                } else if (eventName == "ContinentUnlock") {
                    _ProcessContinentUnlock(payloadToken);
                } else if (eventName == "ContinentLock") {
                    _ProcessContinentLock(payloadToken);
                } else if (eventName == "MetagameEvent") {
                    _ProcessMetagameEvent(payloadToken);
                } else {
                    _Logger.LogWarning($"Untracked event_name: '{eventName}': {payloadToken}");
                }
            }
        }

        private async Task _ProcessPlayerLogin(JToken payload) {
            //_Logger.LogTrace($"Processing login: {payload}");

            string? charID = payload.Value<string?>("character_id");
            if (charID != null) {
                _CacheQueue.Queue(charID);

                TrackedPlayer p;

                lock (CharacterStore.Get().Players) {
                    // The FactionID and TeamID are updated as part of caching the character
                    p = CharacterStore.Get().Players.GetOrAdd(charID, new TrackedPlayer() {
                        ID = charID,
                        WorldID = payload.GetWorldID(),
                        ZoneID = -1,
                        FactionID = Faction.UNKNOWN,
                        TeamID = Faction.UNKNOWN,
                        Online = false
                    });
                }

                await _SessionDb.Start(p);

                p.LatestEventTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }
        }

        private async Task _ProcessPlayerLogout(JToken payload) {
            string? charID = payload.Value<string?>("character_id");
            if (charID != null) {
                _CacheQueue.Queue(charID);

                TrackedPlayer? p;
                lock (CharacterStore.Get().Players) {
                    _ = CharacterStore.Get().Players.TryGetValue(charID, out p);
                }

                if (p != null) {
                    await _SessionDb.End(p);

                    // Reset team of the NSO player as they're now offline
                    if (p.FactionID == Faction.NS) {
                        p.TeamID = Faction.NS;
                    }
                }
            }
        }

        private void _ProcessMetagameEvent(JToken payload) {
            short worldID = payload.GetWorldID();
            int zoneID = payload.GetZoneID();
            string metagameEventName = payload.GetString("metagame_event_state_name", "missing");
            int metagameEventID = payload.GetInt32("metagame_event_id", 0);

            lock (ZoneStateStore.Get().Zones) {
                ZoneState? state = ZoneStateStore.Get().GetZone(worldID, zoneID);

                if (state == null) {
                    state = new() {
                        ZoneID = zoneID,
                        WorldID = worldID,
                        IsOpened = true
                    };
                }

                if (metagameEventName == "started") {
                    state.AlertStart = DateTime.UtcNow;

                    TimeSpan? duration = MetagameEvent.GetDuration(metagameEventID);
                    if (duration == null) {
                        _Logger.LogWarning($"Failed to find duration of metagame event {metagameEventID}\n{payload}");
                    } else {
                        state.AlertEnd = state.AlertStart + duration;
                    }
                } else if (metagameEventName == "ended") {
                    state.AlertStart = null;
                }

                ZoneStateStore.Get().SetZone(worldID, zoneID, state);
            }

            _Logger.LogInformation($"METAGAME in world {worldID} zone {zoneID} metagame: {metagameEventName}");
        }

        private void _ProcessContinentUnlock(JToken payload) {
            short worldID = payload.GetWorldID();
            int zoneID = payload.GetZoneID();

            lock (ZoneStateStore.Get().Zones) {
                ZoneState? state = ZoneStateStore.Get().GetZone(worldID, zoneID);

                if (state == null) {
                    state = new() {
                        ZoneID = zoneID,
                        WorldID = worldID,
                    };
                }

                state.IsOpened = true;

                ZoneStateStore.Get().SetZone(worldID, zoneID, state);
            }

            _Logger.LogDebug($"OPENED In world {worldID} zone {zoneID} was opened");
        }

        private void _ProcessContinentLock(JToken payload) {
            short worldID = payload.GetWorldID();
            int zoneID = payload.GetZoneID();

            lock (ZoneStateStore.Get().Zones) {
                ZoneState? state = ZoneStateStore.Get().GetZone(worldID, zoneID);

                if (state == null) {
                    state = new() {
                        ZoneID = zoneID,
                        WorldID = worldID,
                    };
                }

                state.IsOpened = false;

                ZoneStateStore.Get().SetZone(worldID, zoneID, state);
            }

            _Logger.LogDebug($" CLOSE In world {worldID} zone {zoneID} was closed");
        }

        private async Task _ProcessDeath(JToken payload) {
            int timestamp = payload.Value<int?>("timestamp") ?? 0;

            int zoneID = payload.GetZoneID();
            string attackerID = payload.Value<string?>("attacker_character_id") ?? "0";
            short attackerLoadoutID = payload.Value<short?>("attacker_loadout_id") ?? -1;
            string charID = payload.Value<string?>("character_id") ?? "0";
            short loadoutID = payload.Value<short?>("character_loadout_id") ?? -1;

            short attackerFactionID = Loadout.GetFaction(attackerLoadoutID);
            short factionID = Loadout.GetFaction(loadoutID);

            _CacheQueue.Queue(charID);
            _CacheQueue.Queue(attackerID);

            KillEvent ev = new KillEvent() {
                AttackerCharacterID = attackerID,
                AttackerLoadoutID = attackerLoadoutID,
                AttackerTeamID = attackerFactionID,
                KilledCharacterID = charID,
                KilledLoadoutID = loadoutID,
                KilledTeamID = factionID,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime,
                WeaponID = payload.GetString("attacker_weapon_id", "0"),
                WorldID = payload.GetWorldID(),
                ZoneID = payload.GetZoneID(),
                AttackerFireModeID = payload.GetInt32("attacker_fire_mode_id", 0),
                AttackerVehicleID = payload.GetInt32("attacker_vehicle_id", 0),
                IsHeadshot = (payload.Value<string?>("is_headshot") ?? "0") != "0"
            };

            //_Logger.LogTrace($"Processing death: {payload}");

            lock (CharacterStore.Get().Players) {
                // The default value for Online must be false, else when a new TrackedPlayer is constructed,
                //      the session will never start, as the handler already sees the character online,
                //      so no need to start a new session
                TrackedPlayer attacker = CharacterStore.Get().Players.GetOrAdd(attackerID, new TrackedPlayer() {
                    ID = attackerID,
                    FactionID = attackerFactionID,
                    TeamID = ev.AttackerTeamID,
                    Online = false,
                    WorldID = ev.WorldID
                });

                if (attacker.Online == false) {
                    _SessionQueue.Queue(attacker);
                }

                _CacheQueue.Queue(attacker.ID);

                attacker.ZoneID = zoneID;

                if (attacker.FactionID == Faction.UNKNOWN) {
                    attacker.FactionID = attackerFactionID; // If a tracked player was made from a login, no faction ID is given
                    attacker.TeamID = ev.AttackerTeamID;
                }

                ev.AttackerTeamID = attacker.TeamID;

                // See above for why false is used for the Online value, instead of true
                TrackedPlayer killed = CharacterStore.Get().Players.GetOrAdd(charID, new TrackedPlayer() {
                    ID = charID,
                    FactionID = factionID,
                    TeamID = ev.KilledTeamID,
                    Online = false,
                    WorldID = ev.WorldID
                });

                _CacheQueue.Queue(killed.ID);

                // Ensure that 2 sessions aren't started if the attacker and killed are the same
                if (killed.Online == false && attacker.ID != killed.ID) {
                    _SessionQueue.Queue(attacker);
                }

                killed.ZoneID = zoneID;
                if (killed.FactionID == Faction.UNKNOWN) {
                    killed.FactionID = factionID;
                    killed.TeamID = ev.KilledTeamID;
                }

                ev.KilledTeamID = killed.TeamID;

                long nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                attacker.LatestEventTimestamp = nowSeconds;
                killed.LatestEventTimestamp = nowSeconds;
            }

            await _KillEventDb.Insert(ev);
        }

        private async Task _ProcessExperience(JToken payload) {
            //_Logger.LogInformation($"Processing exp: {payload}");

            string? charID = payload.Value<string?>("character_id");
            if (charID == null) {
                return;
            }

            _CacheQueue.Queue(charID);

            int expId = payload.GetInt32("experience_id", -1);
            short loadoutId = payload.GetInt16("loadout_id", -1);
            short worldID = payload.GetWorldID();
            int timestamp = payload.Value<int?>("timestamp") ?? 0;
            int zoneID = payload.GetZoneID();
            string otherID = payload.GetString("other_id", "0");

            short factionID = Loadout.GetFaction(loadoutId);

            ExpEvent ev = new ExpEvent() {
                SourceID = charID,
                LoadoutID = loadoutId,
                TeamID = factionID,
                Amount = payload.Value<int?>("amount") ?? 0,
                ExperienceID = expId,
                OtherID = otherID,
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime,
                WorldID = worldID,
                ZoneID = zoneID
            };

            lock (CharacterStore.Get().Players) {
                // Default false for |Online| to ensure a session is started
                TrackedPlayer p = CharacterStore.Get().Players.GetOrAdd(charID, new TrackedPlayer() {
                    ID = charID,
                    FactionID = factionID,
                    TeamID = factionID,
                    Online = false,
                    WorldID = worldID
                });

                if (p.Online == false) {
                    _SessionQueue.Queue(p);
                }

                p.LatestEventTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                p.ZoneID = zoneID;

                if (p.FactionID == Faction.UNKNOWN) {
                    p.FactionID = factionID;
                    p.TeamID = factionID;
                }

                // Update the team_id field if needed
                if (Experience.IsRevive(expId) || Experience.IsHeal(expId) || Experience.IsResupply(expId)) {
                    // If either character was not NSO, update the team_id of the character
                    // If both are NSO, this field is not updated, as one bad team_id could then spread to other NSOs, messing up tracking
                    if (CharacterStore.Get().Players.TryGetValue(otherID, out TrackedPlayer? otherPlayer)) {
                        if (p.FactionID == Faction.NS && otherPlayer.FactionID != Faction.NS
                            && otherPlayer.FactionID != Faction.UNKNOWN && p.TeamID != otherPlayer.FactionID) {

                            //_Logger.LogDebug($"Robot {p.ID} supported (exp {expId}, loadout {loadoutId}, faction {factionID}) non-robot {otherPlayer.ID}, setting robot team ID to {otherPlayer.FactionID} from {p.TeamID}");
                            p.TeamID = otherPlayer.FactionID;
                        }

                        if (p.FactionID != Faction.NS && p.FactionID != Faction.UNKNOWN
                            && otherPlayer.FactionID == Faction.NS && otherPlayer.TeamID != p.FactionID) {

                            //_Logger.LogDebug($"Non-robot {p.ID} supported (exp {expId}, loadout {loadoutId}, faction {factionID}) robot {otherPlayer.ID}, setting robot team ID to {p.FactionID}, from {otherPlayer.TeamID}");
                            otherPlayer.TeamID = p.FactionID;
                        }
                    }
                }

                ev.TeamID = p.TeamID;
            }

            long ID = await _ExpEventDb.Insert(ev);

            if (ev.ExperienceID == Experience.REVIVE || ev.ExperienceID == Experience.SQUAD_REVIVE) {
                await _KillEventDb.SetRevivedID(ev.OtherID, ID);
            }

            // Track the sundy and how many spawns it has
            if (expId == Experience.SUNDERER_SPAWN_BONUS && otherID != null && otherID != "0") {
                lock (NpcStore.Get().Npcs) {
                    TrackedNpc npc = NpcStore.Get().Npcs.GetOrAdd(otherID, new TrackedNpc() {
                        OwnerID = charID,
                        FirstSeenAt = DateTime.UtcNow,
                        NpcID = otherID,
                        SpawnCount = 0,
                        Type = NpcType.Sunderer,
                        WorldID = worldID
                    });

                    ++npc.SpawnCount;
                    npc.LatestEventAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                };
            } else if (expId == Experience.GENERIC_NPC_SPAWN && otherID != null && otherID != "0") {
                lock (NpcStore.Get().Npcs) {
                    TrackedNpc npc = NpcStore.Get().Npcs.GetOrAdd(otherID, new TrackedNpc() {
                        OwnerID = charID,
                        FirstSeenAt = DateTime.UtcNow,
                        NpcID = otherID,
                        SpawnCount = 0,
                        Type = NpcType.Router,
                        WorldID = worldID
                    });

                    ++npc.SpawnCount;
                    npc.LatestEventAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                }
            }

        }

    }
}
