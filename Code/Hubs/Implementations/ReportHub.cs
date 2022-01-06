﻿using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Constants;
using watchtower.Models.Census;
using watchtower.Models.Db;
using watchtower.Models.Events;
using watchtower.Models.Report;
using watchtower.Services.Census;
using watchtower.Services.Db;
using watchtower.Services.Repositories;

namespace watchtower.Code.Hubs.Implementations {

    public class ReportHub : Hub<IReportHub> {

        private readonly ILogger<ReportHub> _Logger;

        private readonly IMemoryCache _Cache;
        private const string CACHE_KEY = "Report.{0}"; // {0} => Generator hash

        private readonly OutfitRepository _OutfitRepository;
        private readonly OutfitCollection _OutfitCensus;
        private readonly ICharacterRepository _CharacterRepository;
        private readonly CharacterDbStore _CharacterDb;
        private readonly ItemRepository _ItemRepository;
        private readonly IKillEventDbStore _KillDb;
        private readonly IExpEventDbStore _ExpDb;
        private readonly ISessionDbStore _SessionDb;
        private readonly ReportDbStore _ReportDb;
        private readonly FacilityControlDbStore _ControlDb;
        private readonly FacilityPlayerControlDbStore _PlayerControlDb;
        private readonly IFacilityDbStore _FacilityDb;

        private readonly ReportRepository _ReportRepository;

        public ReportHub(ILogger<ReportHub> logger, IMemoryCache cache,
            ICharacterRepository charRepo, OutfitRepository outfitRepo,
            OutfitCollection outfitCensus, ISessionDbStore sessionDb,
            IKillEventDbStore killDb, IExpEventDbStore expDb,
            ItemRepository itemRepo, CharacterDbStore charDb,
            ReportDbStore reportDb, FacilityControlDbStore controlDb,
            FacilityPlayerControlDbStore playerControlDb, IFacilityDbStore facDb,
            ReportRepository reportRepo) {

            _Logger = logger;
            _Cache = cache;

            _OutfitRepository = outfitRepo;
            _OutfitCensus = outfitCensus;
            _CharacterRepository = charRepo;
            _CharacterDb = charDb;
            _ItemRepository = itemRepo;
            _KillDb = killDb;
            _ExpDb = expDb;
            _SessionDb = sessionDb;
            _ReportDb = reportDb;
            _ControlDb = controlDb;
            _PlayerControlDb = playerControlDb;
            _FacilityDb = facDb;
            _ReportRepository = reportRepo;
        }

        public async Task GenerateReport(string generator) {
            OutfitReport report = new OutfitReport();

            try {
                report = await _ReportRepository.ParseGenerator(generator);
            } catch (Exception ex) {
                _Logger.LogError(ex, $"Failed to parse generator '{generator}'");
                await Clients.Caller.SendError(ex.ToString());
                return;
            }

            report.Timestamp = DateTime.UtcNow;

            _Logger.LogDebug($"ID {report.ID} from {generator}");

            if (report.ID != Guid.Empty) {
                _Logger.LogDebug($"Loading previous report {report.ID}");
                OutfitReport? dbReport = await _ReportDb.GetByID(report.ID);

                if (dbReport != null) {
                    _Logger.LogDebug($"Overriding generator string '{generator}' into '{dbReport.Generator}'");
                    generator = dbReport.Generator;
                }
            }

            string cacheKey = string.Format(CACHE_KEY, generator);

            if (_Cache.TryGetValue(cacheKey, out report) == true) {
                _Logger.LogDebug($"OutfitReport '{cacheKey}' is cached");
                await Clients.Caller.SendReport(report);
                await Clients.Caller.UpdateCharacterIDs(report.CharacterIDs);
                await Clients.Caller.UpdateSessions(report.Sessions);
                await Clients.Caller.UpdateKills(report.Kills);
                await Clients.Caller.UpdateDeaths(report.Deaths);
                await Clients.Caller.UpdateExp(report.Experience);
                await Clients.Caller.UpdateItems(report.Items);
                await Clients.Caller.UpdateCharacters(report.Characters);
                await Clients.Caller.UpdateOutfits(report.Outfits);
                await Clients.Caller.UpdateControls(report.Control);
                await Clients.Caller.UpdatePlayerControls(report.PlayerControl);
                await Clients.Caller.UpdateFacilities(report.Facilities);

                return;
            }

            try {
                try {
                    report = await _ReportRepository.ParseGenerator(generator);
                    report.ID = Guid.NewGuid();
                    report.Generator = generator;
                    report.Timestamp = DateTime.UtcNow;
                } catch (Exception ex) {
                    _Logger.LogError(ex, $"error in parsing the generator string '{generator}'");
                    await Clients.Caller.SendError(ex.ToString());
                    return;
                }

                await Clients.Caller.SendReport(report);

                if (report.PeriodStart >= report.PeriodEnd) {
                    await Clients.Caller.SendError($"The start period at {report.PeriodStart:u} cannot be after the end {report.PeriodEnd:u}");
                    return;
                }

                if (report.PeriodEnd - report.PeriodStart >= TimeSpan.FromHours(8)) {
                    await Clients.Caller.SendError($"A report cannot span more than 8 hours");
                    return;
                }

                if (report.TeamID <= 0) {
                    await Clients.Caller.SendError($"The TeamID of the report is currently {report.TeamID}, it must be above 0. Try setting the faction");
                    return;
                }

                await _ReportDb.Insert(report);
                report.Players = report.CharacterIDs;

                await Clients.Caller.SendReport(report);
                await Clients.Caller.UpdateCharacterIDs(report.CharacterIDs);
                await Clients.Caller.UpdateSessions(report.Sessions);

                HashSet<string> outfits = new();
                HashSet<string> chars = new();
                HashSet<int> items = new();
                HashSet<int> facilities = new();

                foreach (string id in report.CharacterIDs) {
                    chars.Add(id);
                }

                List<KillEvent> killDeaths = await _KillDb.GetKillsByCharacterIDs(report.CharacterIDs, report.PeriodStart, report.PeriodEnd);
                foreach (KillEvent ev in killDeaths) {
                    chars.Add(ev.KilledCharacterID);
                    chars.Add(ev.AttackerCharacterID);
                    items.Add(int.Parse(ev.WeaponID));
                }

                report.Kills = killDeaths.Where(iter => iter.AttackerTeamID == report.TeamID && iter.AttackerTeamID != iter.KilledTeamID).ToList();
                await Clients.Caller.UpdateKills(report.Kills);

                report.Deaths = killDeaths.Where(iter => iter.KilledTeamID == report.TeamID && iter.KilledTeamID != iter.AttackerTeamID && iter.RevivedEventID == null).ToList();
                await Clients.Caller.UpdateDeaths(report.Deaths);

                // Get all the control events the players participated in
                List<PlayerControlEvent> particpatedCaptures = await _PlayerControlDb.GetByCharacterIDsPeriod(report.CharacterIDs, report.PeriodStart, report.PeriodEnd);

                // Then get the control event for each one, and all the players that participated in that control
                List<FacilityControlEvent> control = new List<FacilityControlEvent>();
                foreach (PlayerControlEvent ev in particpatedCaptures) {
                    if (control.FirstOrDefault(iter => iter.ID == ev.ControlID) != null) {
                        continue;
                    }

                    facilities.Add(ev.FacilityID);

                    FacilityControlEvent? controlEvent = await _ControlDb.GetByID(ev.ControlID);
                    if (controlEvent != null) {
                        if (controlEvent.OutfitID != null && controlEvent.OutfitID != "0") {
                            outfits.Add(controlEvent.OutfitID);
                        }
                        control.Add(controlEvent);
                    }

                    List<PlayerControlEvent> playerEvents = await _PlayerControlDb.GetByEventID(ev.ControlID);
                    report.PlayerControl.AddRange(playerEvents);
                    foreach (PlayerControlEvent pev in playerEvents) {
                        chars.Add(pev.CharacterID);
                    }
                }
                report.Control = control;
                await Clients.Caller.UpdateControls(report.Control);
                await Clients.Caller.UpdatePlayerControls(report.PlayerControl);

                List<ExpEvent> expEvents = await _ExpDb.GetByCharacterIDs(report.CharacterIDs, report.PeriodStart, report.PeriodEnd);
                foreach (ExpEvent ev in expEvents) {
                    chars.Add(ev.SourceID);

                    // some of the exp events have a character id in the other_id field, add those to the character set to load
                    if (Experience.OtherIDIsCharacterID(ev.ExperienceID)) {
                        chars.Add(ev.OtherID);
                    }
                }

                report.Experience = expEvents.Where(iter => iter.TeamID == report.TeamID).ToList();
                await Clients.Caller.UpdateExp(report.Experience);

                foreach (int itemID in items) {
                    PsItem? item = await _ItemRepository.GetByID(itemID);
                    if (item != null) {
                        report.Items.Add(item);
                    }
                }

                await Clients.Caller.UpdateItems(report.Items);

                report.Characters = await GetCharacters(chars.ToList());
                await Clients.Caller.UpdateCharacters(report.Characters);

                foreach (PsCharacter c in report.Characters) {
                    if (c.OutfitID != null) {
                        outfits.Add(c.OutfitID);
                    }
                }

                report.Outfits = await _OutfitRepository.GetByIDs(outfits.ToList());
                await Clients.Caller.UpdateOutfits(report.Outfits);

                foreach (int facID in facilities) {
                    PsFacility? fac = await _FacilityDb.GetByFacilityID(facID);
                    if (fac != null) {
                        report.Facilities.Add(fac);
                    }
                }
                await Clients.Caller.UpdateFacilities(report.Facilities);

                _Cache.Set(cacheKey, report, new MemoryCacheEntryOptions() {
                    SlidingExpiration = TimeSpan.FromMinutes(30)
                });
            } catch (Exception ex) {
                _Logger.LogError(ex, $"generic error in report generation. using generator string '{generator}'");
                await Clients.Caller.SendError(ex.Message);
            }
        }

        private async Task<List<PsCharacter>> GetCharacters(List<string> IDs) {
            List<PsCharacter> chars =  await _CharacterDb.GetByIDs(IDs);

            HashSet<string> found = new HashSet<string>();
            foreach (PsCharacter c in chars) {
                found.Add(c.ID);
            }

            List<string> left = new List<string>();
            foreach (string charID in IDs) {
                if (found.Contains(charID) == false) {
                    left.Add(charID);
                }
            }
            _Logger.LogTrace($"Found {found.Count}/{chars.Count} characters from DB, getting {left.Count} from repo");

            foreach (string charID in left) {
                PsCharacter? c = await _CharacterRepository.GetByID(charID);
                if (c != null) {
                    chars.Add(c);
                }
            }

            return chars;
        }

    }

}
