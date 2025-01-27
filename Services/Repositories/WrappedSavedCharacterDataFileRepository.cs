﻿using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Threading.Tasks;
using watchtower.Models.Wrapped;

namespace watchtower.Services.Repositories {

    public class WrappedSavedCharacterDataFileRepository {

        private readonly ILogger<WrappedSavedCharacterDataFileRepository> _Logger;

        public WrappedSavedCharacterDataFileRepository(ILogger<WrappedSavedCharacterDataFileRepository> logger) {
            _Logger = logger;
        }

        /// <summary>
        ///     Get the saved character data if it already exists
        /// </summary>
        /// <param name="year">Year the wrapped entry is being generated for</param>
        /// <param name="charID">ID of the character</param>
        /// <returns>
        ///     The <see cref="WrappedSavedCharacterData"/> saved to the disk if it exists,
        ///     or null if it doesn't exist
        /// </returns>
        public async Task<WrappedSavedCharacterData?> Get(DateTime year, string charID) {
            string filename = $"HonuWrapped.{year.Year}.{charID}";
            string filepath = $"./wrapped/{filename}.json";

            if (Directory.Exists("./wrapped/") == false) {
                Directory.CreateDirectory("./wrapped/");
            }

            if (File.Exists(filepath) == false) {
                _Logger.LogDebug($"missing {filepath}");
                return null;
            }

            string json;
            try {
                json = await File.ReadAllTextAsync(filepath);
            } catch (Exception ex) {
                _Logger.LogError(ex, $"Failed to read file '{filepath}'");
                return null;
            }

            JToken j = JToken.Parse(json);

            WrappedSavedCharacterData? data = j.ToObject<WrappedSavedCharacterData>();

            if (data == null) {
                _Logger.LogDebug($"Failed to find saved JSON for {charID}");
            } else {
                _Logger.LogDebug($"Found saved JSON for {charID}");
            }

            return data;
        }

        /// <summary>
        ///     Save wrapped character 
        /// </summary>
        /// <param name="charID">ID of the character</param>
        /// <param name="year">Year of the wrapped</param>
        /// <param name="data">Data to save</param>
        public async Task Save(string charID, DateTime year, WrappedSavedCharacterData data) {
            string filename = $"HonuWrapped.{year.Year}.{charID}";
            string filepath = $"./wrapped/{filename}.json";

            if (Directory.Exists("./wrapped/") == false) {
                Directory.CreateDirectory("./wrapped/");
            }

            if (File.Exists(filepath) == true) {
                _Logger.LogWarning($"Saved JSON for {charID} already exist! Overwritting");
            }

            JToken json = JToken.FromObject(data);

            await File.WriteAllTextAsync(filepath, $"{json}", encoding: System.Text.Encoding.UTF8);
        }

    }
}
