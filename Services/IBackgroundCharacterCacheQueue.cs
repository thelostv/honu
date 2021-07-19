﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace watchtower.Services {

    public interface IBackgroundCharacterCacheQueue {

        void Queue(string charID);

        Task<string> DequeueAsync(CancellationToken cancel);

    }
}