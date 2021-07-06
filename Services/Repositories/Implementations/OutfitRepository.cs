﻿using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using watchtower.Models.Census;
using watchtower.Services.Census;
using watchtower.Services.Db;

namespace watchtower.Services.Repositories.Implementations {

    public class OutfitRepository : IOutfitRepository {

        private readonly ILogger<OutfitRepository> _Logger;
        private readonly IMemoryCache _Cache;

        private const string _CacheKeyID = "Outfit.ID.{0}"; // {0} => Outfit ID

        private readonly IOutfitDbStore _Db;
        private readonly IOutfitCollection _Census;

        public OutfitRepository(ILogger<OutfitRepository> logger, IMemoryCache cache,
            IOutfitDbStore db, IOutfitCollection coll) {

            _Logger = logger;
            _Cache = cache;

            _Db = db ?? throw new ArgumentNullException(nameof(db));
            _Census = coll ?? throw new ArgumentNullException(nameof(coll));
        }

        public async Task<PsOutfit?> GetByID(string outfitID) {
            if (outfitID == "") {
                return null;
            }

            string key = string.Format(_CacheKeyID, outfitID);

            if (_Cache.TryGetValue(key, out PsOutfit? outfit) == false) {
                outfit = await _Db.GetByID(outfitID);

                if (outfit == null || HasExpired(outfit) == true) {
                    outfit = await _Census.GetByID(outfitID);

                    if (outfit != null) {
                        await _Db.Upsert(outfit);
                    }
                }

                if (outfit != null) {
                    _Cache.Set(key, outfit, new MemoryCacheEntryOptions() {
                        SlidingExpiration = TimeSpan.FromMinutes(20)
                    });
                }
            }

            return outfit;
        }

        public Task Upsert(PsOutfit outfit) {
            throw new NotImplementedException();
        }

        private bool HasExpired(PsOutfit outfit) {
            return false;
        }

    }
}
