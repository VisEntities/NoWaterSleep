/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("No Water Sleep", "VisEntities", "1.0.0")]
    [Description("Kills players if they go to sleep while underwater.")]
    public class NoWaterSleep : RustPlugin
    {
        #region Fields

        private static NoWaterSleep _plugin;

        #endregion Fields

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            _plugin = null;
        }

        private void OnServerInitialized(bool isStartup)
        {
            int sleepersKilled = 0;
            foreach (BasePlayer sleeper in BasePlayer.sleepingPlayerList)
            {
                if (sleeper == null)
                    continue;

                if (PermissionUtil.HasPermission(sleeper, PermissionUtil.IGNORE))
                    continue;

                if (UnderWater(sleeper))
                {
                    sleeper.Kill();
                    sleepersKilled++;
                }
            }

            if (sleepersKilled > 0)
            {
                Puts($"Killed {sleepersKilled} underwater sleepers on server init.");
            }
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            NextTick(() =>
            {
                if (player == null)
                    return;

                if (PermissionUtil.HasPermission(player, PermissionUtil.IGNORE))
                    return;

                if (UnderWater(player))
                {
                    player.Kill();
                    Puts($"Killed underwater sleeper: {player.displayName} ({player.UserIDString}).");
                }
            });
        }

        #endregion Oxide Hooks

        #region Helper Functions
        
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