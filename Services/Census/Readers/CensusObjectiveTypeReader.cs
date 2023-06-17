using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using watchtower.Code.ExtensionMethods;
using watchtower.Models.Census;

namespace watchtower.Services.Census.Readers {

    public class CensusObjectiveTypeReader : ICensusReader<ObjectiveType> {

        public override ObjectiveType? ReadEntry(JsonElement token) {
            ObjectiveType type = new ObjectiveType();

            type.ID = token.GetRequiredInt32("objective_type_id");
            type.Description = token.GetString("description", "<missing description>");
            type.Param1 = token.GetValue<string?>("param1");
            type.Param2 = token.GetValue<string?>("param2");
            type.Param3 = token.GetValue<string?>("param3");
            type.Param4 = token.GetValue<string?>("param4");
            type.Param5 = token.GetValue<string?>("param5");
            type.Param6 = token.GetValue<string?>("param6");
            type.Param7 = token.GetValue<string?>("param7");
            type.Param8 = token.GetValue<string?>("param8");
            type.Param9 = token.GetValue<string?>("param9");
            type.Param10 = token.GetValue<string?>("param10");

            return type;
        }

    }
}
