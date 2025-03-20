using Oxide.Core;
using System.Collections.Generic;
using UnityEngine;
using Rust;

namespace Oxide.Plugins
{
    [Info("PvPBaseZone", "YourName", "1.0.0")]
    [Description("Enables PvP only near player bases.")]
    public class PvPBaseZone : RustPlugin
    {
        private const float BaseZoneRadius = 50f; // The radius around the base to allow PvP (in meters)

        // Dictionary to track whether a player has been notified
        private Dictionary<ulong, bool> playerPvPZoneStatus = new Dictionary<ulong, bool>();

        private bool IsNearBase(BasePlayer player)
        {
            var cupboards = new List<BaseEntity>();
            foreach (var entity in BaseEntity.serverEntities)
            {
                if (entity is BuildingPrivlidge buildingPrivlidge)
                {
                    if (Vector3.Distance(player.transform.position, buildingPrivlidge.transform.position) < BaseZoneRadius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            if (entity is BasePlayer player && hitInfo.Initiator is BasePlayer attacker)
            {
                bool isPlayerNearBase = IsNearBase(player);
                bool isAttackerNearBase = IsNearBase(attacker);

                if (isPlayerNearBase && isAttackerNearBase)
                {
                    Puts($"{attacker.displayName} is allowed to damage {player.displayName}");
                    return; // Allow PvP damage
                }

                // Cancel damage if players are not in a PvP zone
                hitInfo.damageTypes = new DamageTypeList(); // Clears the damage types
                hitInfo.HitMaterial = 0; // Optional: Ensures no environmental damage is applied
                hitInfo.DoHitEffects = false; // Stops the hit effects from happening
                hitInfo.Initiator = null; // Nullify the initiator (prevents damage application)
                hitInfo.HitPositionWorld = Vector3.zero; // Prevent any impact from being registered

                Puts($"{attacker.displayName} cannot damage {player.displayName} - not in PvP zone");
            }
        }

        // Method to check and send PvP zone entry/exit messages
        private void CheckPvPZoneStatus(BasePlayer player)
        {
            bool isNearBase = IsNearBase(player);
            bool currentlyInPvPZone = playerPvPZoneStatus.ContainsKey(player.userID) && playerPvPZoneStatus[player.userID];

            // If the player's status has changed (entered or exited the PvP zone)
            if (isNearBase && !currentlyInPvPZone)
            {
                // Player entered the PvP zone
                player.ChatMessage("You have entered a PvP zone! You can now be damaged by other players.");
                playerPvPZoneStatus[player.userID] = true;
            }
            else if (!isNearBase && currentlyInPvPZone)
            {
                // Player exited the PvP zone
                player.ChatMessage("You have left the PvP zone. You are now safe from PvP damage.");
                playerPvPZoneStatus[player.userID] = false;
            }
        }

        void OnTick()
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                CheckPvPZoneStatus(player);
            }
        }

        private void Loaded()
        {
            timer.Repeat(2f, 0, OnTick); // Check every 2 seconds for player position updates
        }
    }
}
