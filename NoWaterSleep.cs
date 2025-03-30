/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Rust;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("No Water Sleep", "VisEntities", "1.1.0")]
    [Description("Kills players if they go to sleep while underwater.")]
    public class NoWaterSleep : RustPlugin
    {
        #region Fields

        private static NoWaterSleep _plugin;
        private static Configuration _config;
        private Dictionary<ulong, Timer> _pendingStartTimers = new Dictionary<ulong, Timer>();
        private Dictionary<ulong, Timer> _damageTimers = new Dictionary<ulong, Timer>();

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Delay Before Damage Seconds")]
            public float DelayBeforeDamage { get; set; }

            [JsonProperty("Damage Amount Per Tick")]
            public float DamageAmountPerTick { get; set; }

            [JsonProperty("Damage Interval Seconds")]
            public float DamageIntervalSeconds { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                DelayBeforeDamage = 30f,
                DamageAmountPerTick = 1f,
                DamageIntervalSeconds = 1f,
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            foreach (var timer in _pendingStartTimers.Values)
            {
                if (timer != null)
                    timer.Destroy();
            }
            foreach (var timer in _damageTimers.Values)
            {
                if (timer != null)
                    timer.Destroy();
            }
            _pendingStartTimers.Clear();
            _damageTimers.Clear();

            _config = null;
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            int sleepersScheduled = 0;
            foreach (BasePlayer sleeper in BasePlayer.sleepingPlayerList)
            {
                if (sleeper == null)
                    continue;

                if (PermissionUtil.HasPermission(sleeper, PermissionUtil.IGNORE))
                    continue;

                if (UnderWater(sleeper))
                {
                    DelayedDrown(sleeper.userID);
                    sleepersScheduled++;
                }
            }

            if (sleepersScheduled > 0)
            {
                Puts($"Scheduled {sleepersScheduled} underwater sleepers to begin drowning soon.");
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            NextTick(() =>
            {
                if (player == null) return;

                if (PermissionUtil.HasPermission(player, PermissionUtil.IGNORE))
                    return;

                if (!UnderWater(player))
                    return;

                DelayedDrown(player.userID);
            });
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            StopDrowning(player.userID);
        }

        #endregion Oxide Hooks

        #region Drowning

        private void DelayedDrown(ulong playerId)
        {
            if (_pendingStartTimers.ContainsKey(playerId) || _damageTimers.ContainsKey(playerId))
                return;

            var pendingTimer = timer.Once(_config.DelayBeforeDamage, () =>
            {
                BasePlayer sleeper = FindPlayerById(playerId);
                if (sleeper == null)
                {
                    StopDrowning(playerId);
                    return;
                }

                if (!UnderWater(sleeper))
                {
                    StopDrowning(playerId);
                    return;
                }

                DoDrown(playerId);
            });

            _pendingStartTimers[playerId] = pendingTimer;
            Puts($"{playerId} will begin drowning in {_config.DelayBeforeDamage}s if still underwater...");
        }

        private void DoDrown(ulong playerId)
        {
            if (_pendingStartTimers.TryGetValue(playerId, out var startTimer))
            {
                startTimer.Destroy();
                _pendingStartTimers.Remove(playerId);
            }

            if (_damageTimers.ContainsKey(playerId))
                return;

            var damageTimer = timer.Repeat(_config.DamageIntervalSeconds, 0, () =>
            {
                BasePlayer sleeper = FindPlayerById(playerId);
                if (sleeper == null)
                {
                    StopDrowning(playerId);
                    return;
                }

                if (!UnderWater(sleeper))
                {
                    StopDrowning(playerId);
                    return;
                }

                sleeper.Hurt(_config.DamageAmountPerTick, DamageType.Drowned, useProtection: false);
            });

            _damageTimers[playerId] = damageTimer;
            Puts($"Drowning {playerId} for sleeping underwater.");
        }

        private void StopDrowning(ulong playerId)
        {
            if (_pendingStartTimers.TryGetValue(playerId, out var pendingTimer))
            {
                pendingTimer.Destroy();
                _pendingStartTimers.Remove(playerId);
            }

            if (_damageTimers.TryGetValue(playerId, out var dmgTimer))
            {
                dmgTimer.Destroy();
                _damageTimers.Remove(playerId);
            }
        }

        #endregion Drowning

        #region Helper Functions

        public static BasePlayer FindPlayerById(ulong playerId)
        {
            return RelationshipManager.FindByID(playerId);
        }

        private bool UnderWater(BasePlayer player)
        {
            WaterLevel.WaterInfo info = WaterLevel.GetWaterInfo(player.transform.position, true, true, player);
            if (!info.isValid)
                return false;

            return info.currentDepth > 1f;
         }

        #endregion Helper Functions

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "nowatersleep.ignore";
            private static readonly List<string> _permissions = new List<string>
            {
                IGNORE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions
    }
}