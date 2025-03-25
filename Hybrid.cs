using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Plugins;
using Oxide.Game.Rust;
using Oxide.Game.Rust.Cui;
using Rust;
using UnityEngine;
using VLB;
using Random = UnityEngine.Random;
using System.IO;

namespace Oxide.Plugins
{
    [Info("PvE/PvP Weekly Choice", "YourName", "1.0.0")]
    [Description("Allows players to choose between PvE or PvP mode, but can only switch once a week.")]
    public class Hybrid : RustPlugin
    {
        // Store the player settings (PvE or PvP) and the last switch time
        private Dictionary<ulong, PlayerData> playerData = new Dictionary<ulong, PlayerData>();

        // Default mode when a player joins (PvP or PvE)
        private const bool defaultPvP = true;

        // Time restriction (1 week)
        private readonly TimeSpan switchCooldown = TimeSpan.FromDays(4);

        // File path for saving data
        private string dataFilePath;
        private readonly TimeSpan offlineToPvP = TimeSpan.FromHours(25);

        // Called when the plugin is loaded
        private void Init()
        {
            dataFilePath = Path.Combine(Interface.Oxide.DataDirectory, "pve_pvp_data.json"); // Use Oxide's DataDirectory
            LoadData(); // Load the saved data
            Puts("PvE/PvP Weekly Choice plugin loaded.");
            timer.Every(100f, CheckOfflinePlayers);
        }

        private void CheckOfflinePlayers()
        {
            DateTime now = DateTime.Now;
            foreach (var entry in playerData)
            {
                if (entry.Value.IsPvE && (now - entry.Value.LastLogin) > offlineToPvP)
                {
                    entry.Value.IsPvE = false;
                    Puts($"Player {entry.Key} has been switched to PvP due to inactivity.");
                }
            }
            SaveData();
        }

        void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (playerData.ContainsKey(player.userID))
            {
                playerData[player.userID].LastLogin = DateTime.Now; // Update the LastLogin time when player disconnects
                SaveData(); // Save the data to file after updating
            }
            else
            {
                playerData[player.userID] = new PlayerData { LastLogin = DateTime.Now }; // Create new entry if it doesn't exist
                SaveData();
            }
            Puts($"Player {player.displayName} disconnected. LastLogin updated.");
        }

        // Command to switch to PvE
        [ChatCommand("pve")]
        private void CmdPvE(BasePlayer player, string command, string[] args)
        {
            player.ChatMessage("pve");
            if (CanSwitch(player))
            {
                SetPvEMode(player, true);
                player.ChatMessage("You are now in PvE mode.");
            }
            else
            {
                TimeSpan remainingTime = switchCooldown - (DateTime.Now - playerData[player.userID].LastSwitchTime);
                player.ChatMessage($"You can only switch modes once every 4 days. Try again in {remainingTime.Days} days and {remainingTime.Hours} hours.");
            }
        }

        // Command to switch to PvP
        [ChatCommand("pvp")]
        private void CmdPvP(BasePlayer player, string command, string[] args)
        {
            if (CanSwitch(player))
            {
                SetPvEMode(player, false);
                player.ChatMessage("You are now in PvP mode.");
            }
            else
            {
                TimeSpan remainingTime = switchCooldown - (DateTime.Now - playerData[player.userID].LastSwitchTime);
                player.ChatMessage($"You can only switch modes once a week. Try again in {remainingTime.Days} days and {remainingTime.Hours} hours.");
            }
        }

        // Set the PvE mode for a player
        private void SetPvEMode(BasePlayer player, bool isPvE)
        {
            if (!playerData.ContainsKey(player.userID))
            {
                playerData[player.userID] = new PlayerData();
            }

            playerData[player.userID].IsPvE = isPvE;
            playerData[player.userID].LastSwitchTime = DateTime.Now;

            // Save the data after changing the mode
            SaveData();
        }

        // Check if the player can switch their mode
        private bool CanSwitch(BasePlayer player)
        {
            // If player has never switched, allow switching
            if (!playerData.ContainsKey(player.userID)) return true;

            // If a week has passed since last switch, allow switching
            return DateTime.Now - playerData[player.userID].LastSwitchTime >= switchCooldown;
        }

        private void OnServerInitialized()
        {
            timer.Every(900f, () => BroadcastMessage("Type /help to get a list of server commands"));
        }

        [ChatCommand("help")]
        void TestCommand(BasePlayer player, string command, string[] args)
        {
            // Send a reply to the player
            player.ChatMessage("Commands: \n /pve - Sets the playmode to pve \n /pvp - Sets the playmode to pvp \n /shop - opens the shop menu \n /kit - opens the kits menu");
        }

        private void BroadcastMessage(string message)
        {
            foreach (BasePlayer player in BasePlayer.activePlayerList)
            {
                player.ChatMessage(message);
            }
        }

        void OnPlayerConnected(BasePlayer player)
        {
            player.ChatMessage("Welcome! Type /help to get a list of server commands");
        }

        // Called when a player joins
        private void OnPlayerInit(BasePlayer player)
        {
            // Set player to PvP by default when they join
            if (!playerData.ContainsKey(player.userID))
            {
                playerData[player.userID] = new PlayerData { IsPvE = true, LastSwitchTime = DateTime.MinValue, LastLogin = DateTime.Now };
            }
        }


        private bool UserIdIsPvE(ulong userID)
        {
            return playerData.ContainsKey(userID) && playerData[userID].IsPvE;
        }

        private object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo hitInfo)
        {
            // Ensure the entity being damaged is a building block
            if (entity is BuildingBlock)
            {
                ulong ownerID = entity.OwnerID;
                if (ownerID != 0 && UserIdIsPvE(ownerID))
                {
                    if (hitInfo.Initiator is BasePlayer attacker && attacker.userID != ownerID)
                    {
                        attacker.ChatMessage("This structure belongs to a PvE player and cannot be damaged.");
                        return true;
                    }

                }
            }
            // Ensure the entity being damaged is a player
            if (entity is BasePlayer targetPlayer)
            {
                // Check if the attack has an initiator and that it's a player
                if (hitInfo.Initiator is BasePlayer attackerPlayer)
                {
                    bool isTargetPvE = IsPvE(targetPlayer);
                    bool isAttackerPvE = IsPvE(attackerPlayer);

                    // Prevent PvE players from damaging PvP players
                    if (isAttackerPvE && !isTargetPvE)
                    {
                        attackerPlayer.ChatMessage("Cannot attack player");
                        return true; // Block the damage
                    }

                    // Prevent PvE players from damaging each other
                    if (isAttackerPvE && isTargetPvE)
                    {
                        attackerPlayer.ChatMessage("Cannot attack player");
                        return true; // Block the damage
                    }
                    // Prevent pvp players from damaging pve
                    if (!isAttackerPvE && isTargetPvE)
                    {
                        attackerPlayer.ChatMessage("Cannot attack player");
                        return true; // Block the damage
                    }
                }
            }

            return null; // Allow damage by default
        }

        private void PreventDamage(HitInfo hitInfo)
        {
            hitInfo.damageTypes = new DamageTypeList(); // Clears the damage types
            hitInfo.HitMaterial = 0; // Ensures no environmental damage is applied
            hitInfo.DoHitEffects = false; // Stops the hit effects
            hitInfo.Initiator = null; // Nullifies the initiator to prevent damage application
            hitInfo.HitPositionWorld = Vector3.zero; // Prevents any impact from being registered
        }

        private bool IsPvE(BasePlayer player)
        {
            return playerData.ContainsKey(player.userID) && playerData[player.userID].IsPvE;
        }


        // Save the data to a file
        private void SaveData()
        {
            try
            {
                string json = JsonConvert.SerializeObject(playerData, Formatting.Indented);
                File.WriteAllText(dataFilePath, json);
            }
            catch (Exception e)
            {
                Puts("Error saving data: " + e.Message);
            }
        }

        // Load the data from a file
        private void LoadData()
        {
            try
            {
                if (File.Exists(dataFilePath))
                {
                    string json = File.ReadAllText(dataFilePath);
                    playerData = JsonConvert.DeserializeObject<Dictionary<ulong, PlayerData>>(json);
                }
            }
            catch (Exception e)
            {
                Puts("Error loading data: " + e.Message);
            }
        }

        // Data structure to store player settings
        private class PlayerData
        {
            public bool IsPvE { get; set; }
            public DateTime LastSwitchTime { get; set; }
            public DateTime LastLogin { get; set; }
        }
    }
}
