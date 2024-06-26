﻿using EventTracker.Objects;
using System;
using System.Reflection;
using UnityEngine;
using I2.Loc;
using MelonLoader;
using HarmonyLib;

namespace EventTracker
{
    [HarmonyPatch]
    static public class Hooks
    {
        public static int bossWave = 0;
        public static bool isSwap = true;
        public static bool isJump = true;

        static bool boof;
        static bool dnf;

        public static float dnfTimer;
        public static int redwood = 0;

        static TrackerItem burst = null;

        static TrackerItem parry = null;
        static bool parryGround = false;
        public static float parryTimer;
        static int parryCount;

        static string GetCardName(PlayerCardData data)
        {
            try
            {
                // idk where this breaks but i think it does
                var translation = LocalizationManager.GetTranslation(UICard.GetAbilityNameFormatted(data));
                if (translation.Length > 0)
                {
                    return translation;
                }
            }
            catch { }
            return LocalizationManager.GetTranslation(data.cardName);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechController), "SetPlayerCards")]
        static void SetPlayerCards(ref MechController __instance, bool onPickup)
        {
            if (!EventTracker.holder || boof) return;
            if (!isSwap)
            {
                isSwap = true;
                return;
            }
            var card = __instance.GetPlayerCardDeck().GetCardInHand(0);
            var name = GetCardName(card.data);
            if (onPickup)
                EventTracker.holder.PushText($"Pick up {name}", card.data.cardColor, !EventTracker.JSON.GetSetting("pickup"));
            else
                EventTracker.holder.PushText($"Swap to {name}", card.data.cardColor, !EventTracker.JSON.GetSetting("swap"));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MechController), "FireCard", [typeof(int)])]
        static void PreFireCardInt()
        {
            isSwap = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechController), "FireCard", [typeof(int)])]
        static void PostFireCardInt()
        {
            isSwap = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechController), "FireCard", [typeof(PlayerCard)])]
        static void FireCard(ref MechController __instance, ref bool __result, ref PlayerCard card)
        {
            if (!EventTracker.holder) return;
            if (card.data.discardAbility == PlayerCardData.DiscardAbility.Consumable)
            {
                // this says "Fire Ammo" for some reason so we'll just
                EventTracker.holder.PushText($"Pick up {GetCardName(card.data)}", card.data.cardColor, !EventTracker.JSON.GetSetting("pickup"));
                return;
            }
            if (card.data.cardType != PlayerCardData.Type.WeaponProjectile && card.data.cardType != PlayerCardData.Type.WeaponHitscan)
                return;
            if (__result)
            {
                if (__instance._bulletsFiredThisBurst == 0)
                    burst = EventTracker.holder.PushText($"Fire {GetCardName(card.data)}", card.data.cardColor, !EventTracker.JSON.GetSetting("fire"));
                else if (burst != null)
                    burst.text = $"Fire {GetCardName(card.data)} ({__instance._bulletsFiredThisBurst + 1})";
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(FirstPersonDrifter), "OnParry")]
        static void OnParry(ref FirstPersonDrifter __instance)
        {
            if (!EventTracker.holder) return;
            bool diff = __instance.GetIsGrounded() != parryGround;
            parryGround = __instance.GetIsGrounded();

            if (parry == null || diff || parryTimer <= 0)
            {
                parryCount = 1;
                parryTimer = 0.3f;
                parry = EventTracker.holder.PushText("Parry " + (parryGround ? "(Grounded)" : ""), !EventTracker.JSON.GetSetting("parry"));
            }
            else
            {
                parryTimer = 0.3f;
                parry.text = "Parry " + (parryGround ? "(Ground) " : "") + $"({++parryCount})";
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MechController), "UseDiscardAbility", [typeof(PlayerCardData), typeof(int), typeof(bool), typeof(bool)])]
        static void PreUseDiscardAbility(ref PlayerCardData data)
        {
            isSwap = false;
            isJump = false;
            if (data.discardAbility == PlayerCardData.DiscardAbility.Telefrag)
                boof = true;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(MechController), "UseDiscardAbility", [typeof(PlayerCardData), typeof(int), typeof(bool), typeof(bool)])]
        static void PostUseDiscardAbility(bool __result, ref PlayerCardData data)
        {
            if (!EventTracker.holder) return;
            if (__result)
                EventTracker.holder.PushText($"Discard {GetCardName(data)}", data.cardColor, !EventTracker.JSON.GetSetting("discard"));
            isSwap = true;
            if (!boof)
                isJump = true;
            boof = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FirstPersonDrifter), "ForceJump")]
        static void PreForceJump(ref FirstPersonDrifter __instance, ref bool isCappable, ref bool cancelDurationalMovementAbilities)
        {
            if (!EventTracker.holder) return;
            isSwap = true;

            if (!isJump)
            {
                isJump = true;
                return;
            }
            if (!isCappable)
                EventTracker.holder.PushText("Boost", !EventTracker.JSON.GetSetting("boost"));
            else if (!cancelDurationalMovementAbilities)
            {
                if (__instance.GetIsGrounded())
                    EventTracker.holder.PushText($"Jump", !EventTracker.JSON.GetSetting("jump"));
                else
                    EventTracker.holder.PushText($"Coyote Jump", !EventTracker.JSON.GetSetting("jump"));
            }
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(BossEncounter), "OnExitState")]
        static void OnBossExitState(ref BossEncounter.State s)
        {
            if (!EventTracker.holder) return;

            // TODO: maybe try a diff state
            if (s == BossEncounter.State.Vulnerable && EventTracker.JSON.GetSetting("bossWaves"))
                EventTracker.holder.PushText($"End Wave {bossWave++}", new Color32(6, 183, 0, 255), true);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Enemy), "Die")]
        static void EnemyDie(ref Enemy __instance)
        {
            if (!EventTracker.holder) return;

            // if we're a boss i don't think we EVER care abt enemies
            if (bossWave != 0) return;

            bool matter = true;
            if (!RM.ui.demonCounterHolder.activeSelf)
                matter = false;

            string name = "";
            switch (__instance.GetEnemyType())
            {
                case Enemy.Type.jock: name = "Jock"; break;
                case Enemy.Type.jumper: name = "Jumper"; break;
                case Enemy.Type.roller: name = "Roller"; break;
                case Enemy.Type.frog: name = "Frog"; break;
                case Enemy.Type.guardian: name = "Guardian"; break;
                case Enemy.Type.barnacle: name = "Barnacle"; break;
                case Enemy.Type.balloon: name = "Balloon"; break;
                case Enemy.Type.ringer: name = "Ringer"; break;
                case Enemy.Type.boxer: name = "Boxer"; break;
                case Enemy.Type.bossBasic: name = "Boss"; break;
                case Enemy.Type.mimic: name = "Mimic"; break;
                case Enemy.Type.shocker: name = "Shocker"; break;
                case Enemy.Type.tripwire: name = "Tripwire"; break;
                case Enemy.Type.forcefield: name = "Bubble"; break;
                case Enemy.Type.shockerAndForcefield: name = "Shocker + Bubble"; break;
                case Enemy.Type.demonBall: name = "Demon Ball"; break;
                case Enemy.Type.ufo: name = "UFO"; break;
            }

            if (matter)
            {
                // "why don't u override EnemyWave::OnEnemyDeath"
                // i think it looks nicer if it's b4 pickup :3
                int count = (typeof(Enemy).GetField("_enemyWave", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance) as EnemyWave).GetEnemiesRemaining() - 1;

                string demons = "Demons";
                if (count == 1) demons = "Demon";
                EventTracker.holder.PushText($"{count} {demons} ({name})", Color.clear, !EventTracker.JSON.GetSetting("enemyDeath"), true);
            }
            else EventTracker.holder.PushText($"Kill {name}", Color.clear, !EventTracker.JSON.GetSetting("trivialDeath"));
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Enemy), "ForceDie")]
        static void EnemyForceDie(ref Enemy __instance)
        {
            EnemyDie(ref __instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(BaseDamageable), "Die")]
        static void Breakable(ref BaseDamageable __instance)
        {
            if (!EventTracker.holder) return;

            if (__instance._damageableType == BaseDamageable.DamageableType.Wall)
                EventTracker.holder.PushText($"Break red-wood {++redwood}", new Color32(0xE1, 0x60, 0x58, 255), !EventTracker.JSON.GetSetting("redDestruct"), true);
            else
            {
                if (__instance._damageableType == BaseDamageable.DamageableType.Enemy)
                    return; // we don't do that here

                string damageable = ((BaseDamageable.DamageableType)((int)__instance._damageableType / 10 * 10)).ToString(); // handle crystal types and cast to string
                if (damageable == "EnvironmentPortal")
                    return; // wtf *is* that
                if (damageable == "Platform")
                    damageable = "Glass";

                EventTracker.holder.PushText($"Break {damageable}", !EventTracker.JSON.GetSetting("otherDestruct"));
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(LevelGate), "OnTriggerStay")]
        private static void OnTriggerStay(ref LevelGate __instance)
        {
            dnf = !__instance.Unlocked;
            if (!dnf || dnfTimer <= 0)
                OnLevelWin();
            if (dnf)
                dnfTimer = 3;
            dnf = false;
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(Game), "OnLevelWin")]
        private static void OnLevelWin()
        {
            if (!EventTracker.holder) return;

            // sometimes this mysteriously errors
            // better safe than sorry
            // edit: this no longer "mysteriously" errors
            try
            {
                if (EventTracker.holder.revealed ||
                    (LevelRush.IsLevelRush() &&
                    LevelRush.GetCurrentLevelRush().randomizedIndex.Length - 1 != LevelRush.GetCurrentLevelRush().currentLevelIndex)) return;

                Game game = Singleton<Game>.Instance;
                long best = GameDataManager.levelStats[game.GetCurrentLevel().levelID].GetTimeBestMicroseconds();
                EventTracker.holder.Reveal(best > game.GetCurrentLevelTimerMicroseconds(), !dnf);
            }
            catch (Exception e)
            {
                MelonLogger.Error($"error occured when winning :( {e}");
            }
        }
        [HarmonyPrefix]
        [HarmonyPatch(typeof(MechController), "Die")]
        private static void OnPlayerDie(ref bool restartImmediately, ref bool playRestartSound)
        {
            if (!EventTracker.holder && EventTracker.holder.revealed) return;
            if (restartImmediately)
                EventTracker.holder.Reveal(false, true, true);
            else
                EventTracker.holder.PushText("Death", new Color32(235, 23, 66, 255), !EventTracker.JSON.GetSetting("death"), true);
        }

        [HarmonyPrefix]
        [HarmonyPatch(typeof(MainMenu), "SetState")]
        private static void MenuState(ref MainMenu.State newState)
        {
            if (!EventTracker.holder) return;
            if (newState == MainMenu.State.Loading)
                EventTracker.holder.forceHide = true;
            else
            {
                EventTracker.holder.forceHide = false;
                if (newState == MainMenu.State.Staging)
                {
                    EventTracker.holder.LoadComparison();
                    if (EventTracker.Settings.UseJSON.Value)
                        EventTracker.holder.LoadJSON();
                    else
                    {
                        EventTracker.JSON.json = null; // rip :(
                        TrackerHolder.regexFilter = null;
                    }
                }
            }
        }
    }
}
