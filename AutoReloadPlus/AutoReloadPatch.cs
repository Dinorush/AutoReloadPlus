using Gear;
using HarmonyLib;
using System.Collections;
using CollectionExtensions = BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions;
using UnityEngine;
using AutoReloadPlus.Utils;
using Player;

namespace AutoReloadPlus
{
    internal static class AutoReloadPatch
    {
        private static BulletWeaponArchetype? cachedArchetype;
        private static Coroutine? autoReloadRoutine = null;
        private static bool haltAutoReloads = false;
        private static int emptyClicks = 0;
        private static float heldEmptyTimer = 0f;
        private static float emptyTimer = 0f;
        
        // m_nextShotTimer is used on auto weapons to manage shot cooldown... but also to manage out of ammo clicks with no variable to know which!
        // Using this instead to track the next shot timer when the magazine was emptied so we can properly account for shot cooldown on autos.
        private static float emptyShotTimer = 0f;

        private static bool ShouldTriggerReload()
        {
            if (cachedArchetype == null || !cachedArchetype.m_weapon.m_inventory.CanReloadCurrent()) return false;

            if (Configuration.autoReloadAimingInstant && (PlayerLocomotion.AimToggleLock || InputMapper.GetButtonKeyMouse(InputAction.Aim, eFocusState.FPS)))
                return true;
            if (emptyClicks >= Configuration.clicksToReload && CanCountClicks())
                return true;
            
            if (haltAutoReloads) return false;

            if (CanTimeAutoReloads())
            {
                if (cachedArchetype.m_archetypeData.FireMode == eWeaponFireMode.Auto)
                {
                    if (Configuration.autoReloadDelayHoldAuto >= 0f && heldEmptyTimer >= Configuration.autoReloadDelayHoldAuto) return true;
                    if (Configuration.autoReloadDelayAuto >= 0f && emptyTimer >= Configuration.autoReloadDelayAuto) return true;
                }
                else
                {
                    if (Configuration.autoReloadDelaySemi >= 0f && emptyTimer >= Configuration.autoReloadDelaySemi) return true;
                }
            }

            return false;
        }

        private static bool CanCountClicks()
        {
            if (cachedArchetype == null) return false;

            if (cachedArchetype.m_archetypeData.FireMode == eWeaponFireMode.Auto)
                return Configuration.autoReloadBypassDelayAuto || (Clock.Time > cachedArchetype.m_nextBurstTimer && Clock.Time > emptyShotTimer);
            else
                return Configuration.clicksBypassDelay || Clock.Time > cachedArchetype.m_nextBurstTimer;
        }

        private static bool CanTimeAutoReloads()
        {
            if (cachedArchetype == null) return false;

            if (cachedArchetype.m_archetypeData.FireMode == eWeaponFireMode.Auto)
                return Configuration.autoReloadBypassDelayAuto || (Clock.Time > cachedArchetype.m_nextBurstTimer && Clock.Time > emptyShotTimer);
            else
                return Configuration.autoReloadBypassDelaySemi || Clock.Time > cachedArchetype.m_nextBurstTimer;
        }

        private static void Reset()
        {
            haltAutoReloads = false;
            heldEmptyTimer = 0f;
            emptyTimer = 0f;
            emptyClicks = 0;
            emptyShotTimer = 0f;
        }

        [HarmonyPatch(typeof(BulletWeaponArchetype), nameof(BulletWeaponArchetype.OnWield))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void StartUpdateRoutine(BulletWeaponArchetype __instance)
        {
            if (__instance.m_owner == null || !__instance.m_owner.IsLocallyOwned) return;

            cachedArchetype = __instance;
            if (autoReloadRoutine != null)
            {
                ARPLogger.Warning("OnWield called, but a coroutine still exists.");
                Reset();
            }
            else
                autoReloadRoutine = CoroutineManager.StartCoroutine(CollectionExtensions.WrapToIl2Cpp(UpdateAutoReload()));
        }

        [HarmonyPatch(typeof(BulletWeaponArchetype), nameof(BulletWeaponArchetype.OnUnWield))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void StopUpdateRoutine(BulletWeaponArchetype __instance)
        {
            if (__instance.m_owner == null || !__instance.m_owner.IsLocallyOwned) return;

            cachedArchetype = null;
            if (autoReloadRoutine != null) 
                CoroutineManager.StopCoroutine(autoReloadRoutine);
            autoReloadRoutine = null;
        }

        private static IEnumerator UpdateAutoReload()
        {
            float delta;
            float lastUpdateTime = Clock.Time;
            bool clickSoundTriggered = false; // Avoid double-playing the first "out of ammo" sound. Won't work past that, but too much effort to fix any more.
            
            heldEmptyTimer = 0f;
            emptyTimer = 0f;
            emptyClicks = 0;
            emptyShotTimer = 0f;

            while (true)
            {
                yield return null;
                delta = Clock.Time - lastUpdateTime;
                lastUpdateTime = Clock.Time;

                if (cachedArchetype == null)
                {
                    ARPLogger.Log("AutoReload coroutine has no cached archetype (OnUnWield was not called). Culling coroutine.");

                    autoReloadRoutine = null;
                    Reset();
                    yield break;
                }

                if (cachedArchetype.m_weapon.IsReloading)
                {
                    Reset();
                    continue;
                }

                if (cachedArchetype.m_clip > 0)
                    continue;

                // --------------- Gun is out of ammo ---------------

                if (emptyShotTimer == 0f)
                    emptyShotTimer = cachedArchetype.m_nextShotTimer;

                if (cachedArchetype.m_owner.Locomotion.IsRunning)
                {
                    haltAutoReloads = Configuration.sprintHaltsReload;
                    heldEmptyTimer = 0f;
                    emptyTimer = 0f;
                    continue;
                }

                if (cachedArchetype.m_weapon.FireButtonPressed && CanCountClicks())
                {
                    if ((clickSoundTriggered || !cachedArchetype.m_clickTriggered) && emptyClicks < Configuration.clicksToReload - 1)
                        cachedArchetype.m_weapon.TriggerAudio(cachedArchetype.m_weapon.AudioData.eventClick);
                    clickSoundTriggered = cachedArchetype.m_clickTriggered;
                    emptyClicks++;
                }

                if (!haltAutoReloads && CanTimeAutoReloads())
                {
                    emptyTimer += delta;

                    if (cachedArchetype.m_weapon.FireButton || InputMapper.HasGamepad && InputMapper.GetAxisKeyMouseGamepad(InputAction.GamepadFireTrigger, cachedArchetype.m_owner.InputFilter) > 0f)
                        heldEmptyTimer += delta;
                }

                if (ShouldTriggerReload())
                {
                    // Reload may be triggered manually or by in-game Auto Reload too, so just reseting when the gun is reloading by any means.
                    cachedArchetype.m_weapon.m_inventory.TriggerReload();
                    clickSoundTriggered = false;
                }
            }
        }
    }
}
