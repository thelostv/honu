﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace watchtower.Code.Constants {

    /// <summary>
    ///     metagame event constants
    /// </summary>
    public class MetagameEvent {

        public const int AERIAL_ANOMALY_INDAR = 228;
        public const int AERIAL_ANOMALY_HOSSIN = 229;
        public const int AERIAL_ANOMALY_AMERISH = 230;
        public const int AERIAL_ANOMALY_ESAMIR = 231;
        public const int AERIAL_ANOMALY_OSHUR = 232;

        public const int GHOST_BASTION_INDAR = 242;
        public const int GHOST_BASTION_HOSSIN = 243;
        public const int GHOST_BASTION_AMERISH = 244;
        public const int GHOST_BASTION_ESAMIR = 245;
        public const int GHOST_BASTION_OSHUR = 246; // probably

        public const int SUDDEN_DEATH_INDAR = 236;
        public const int SUDDEN_DEATH_HOSSIN = 237;
        public const int SUDDEN_DEATH_AMERISH = 238;
        public const int SUDDEN_DEATH_ESAMIR = 239;
        public const int SUDDEN_DEATH_OSHUR = 240;
        public const int SUDDEN_DEATH_UNKNOWN = 241;

        /// <summary>
        ///     Get how long a metagame event will last
        /// </summary>
        /// <param name="metagameEventID">ID of the metagame event</param>
        /// <returns>
        ///     A <c>TimeSpan</c> representing how long the metagame event (alert) will last,
        ///     or <c>null</c> if it's unknown
        /// </returns>
        public static TimeSpan? GetDuration(int metagameEventID) {
            if (IsGhostBastion(metagameEventID)) {
                return TimeSpan.FromMinutes(15);
            }

            if (IsAerialAnomaly(metagameEventID)) {
                return TimeSpan.FromMinutes(30);
            }

            if (IsSuddentDeath(metagameEventID)) {
                return TimeSpan.FromMinutes(15);
            }

            return metagameEventID switch {
                147 or 148 or 149 // Indar
                    or 150 or 151 or 152 // Esamir
                    or 153 or 154 or 155 // Hossin
                    or 156 or 157 or 158 // Amerish
                    or 211 or 212 or 213 or 214
                    or 222 or 223 or 224 // Oshur
                    => TimeSpan.FromMinutes(90),

                176 or 177 or 178 or 179 or 186 or 187 or 188 or 189 or 190 or 191 or 192 or 193 => TimeSpan.FromMinutes(45),
                208 or 209 or 210 => TimeSpan.FromMinutes(1),

                _ => null,
            };
        }

        /// <summary>
        ///     Is this metagame event for an aerial anomaly?
        /// </summary>
        /// <param name="metagameEventID">ID of the metagame event</param>
        public static bool IsAerialAnomaly(int metagameEventID) {
            return metagameEventID == AERIAL_ANOMALY_INDAR
                || metagameEventID == AERIAL_ANOMALY_HOSSIN
                || metagameEventID == AERIAL_ANOMALY_AMERISH
                || metagameEventID == AERIAL_ANOMALY_ESAMIR
                || metagameEventID == AERIAL_ANOMALY_OSHUR;
        }

        /// <summary>
        ///     Is this metagame event for a ghost bastion?
        /// </summary>
        /// <param name="metagameEventID"></param>
        /// <returns></returns>
        public static bool IsGhostBastion(int metagameEventID) {
            return metagameEventID == GHOST_BASTION_INDAR
                || metagameEventID == GHOST_BASTION_HOSSIN
                || metagameEventID == GHOST_BASTION_AMERISH
                || metagameEventID == GHOST_BASTION_ESAMIR
                || metagameEventID == GHOST_BASTION_OSHUR;
        }

        /// <summary>
        ///     Is this metagame event for a suddent death?
        /// </summary>
        /// <param name="metagameEventID"></param>
        /// <returns></returns>
        public static bool IsSuddentDeath(int metagameEventID) {
            return metagameEventID == SUDDEN_DEATH_INDAR
                || metagameEventID == SUDDEN_DEATH_HOSSIN
                || metagameEventID == SUDDEN_DEATH_AMERISH
                || metagameEventID == SUDDEN_DEATH_ESAMIR
                || metagameEventID == SUDDEN_DEATH_OSHUR
                || metagameEventID == SUDDEN_DEATH_UNKNOWN;
        }

    }

}
