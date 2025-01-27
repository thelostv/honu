﻿using Microsoft.Extensions.Logging;
using Npgsql;
using watchtower.Code.ExtensionMethods;
using watchtower.Models.Census;

namespace watchtower.Services.Db {

    public class MetagameEventDbStore : BaseStaticDbStore<PsMetagameEvent> {

        public MetagameEventDbStore(ILoggerFactory loggerFactory,
                IDataReader<PsMetagameEvent> reader, IDbHelper helper)
            : base("metagame_event", loggerFactory, reader, helper) {
        }

        internal override void SetupUpsertCommand(NpgsqlCommand cmd, PsMetagameEvent param) {
            cmd.CommandText = @"
                INSERT INTO metagame_event (
                    id, name, description, type_id
                ) VALUES (
                    @ID, @Name, @Description, @TypeID
                ) ON CONFLICT (id) DO UPDATE
                    SET name = @Name,
                        description = @Description,
                        type_id = @TypeID;
            ";

            cmd.AddParameter("ID", param.ID);
            cmd.AddParameter("Name", param.Name);
            cmd.AddParameter("Description", param.Description);
            cmd.AddParameter("TypeID", param.TypeID);
        }

    }
}
