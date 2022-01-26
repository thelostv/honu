﻿using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Services.Repositories;

namespace watchtower.Code.Hubs.Implementations {

    public class WorldOverviewHub : Hub<IWorldOverviewHub> {

        private readonly ILogger<WorldOverviewHub> _Logger;
        private readonly WorldOverviewRepository _WorldOverviewRepository;

        public WorldOverviewHub(ILogger<WorldOverviewHub> logger,
            WorldOverviewRepository worldRepo) {

            _Logger = logger;
            _WorldOverviewRepository = worldRepo;
        }

        public override async Task OnConnectedAsync() {
            //_Logger.LogInformation($"New connection: {Context.ConnectionId}, count: {++_ConnectionCount}");
            await base.OnConnectedAsync();
            await Clients.Caller.UpdateData(_WorldOverviewRepository.Build());
        }

    }
}
