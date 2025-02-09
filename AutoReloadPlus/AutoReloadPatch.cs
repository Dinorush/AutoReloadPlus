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
        private static BulletWeaponArchetype? _cachedArchetype;
        private static IInteractable? _cachedInteract;
        private static Interact_Timed? _cachedInteractTimed;
        private static Coroutine? _autoReloadRoutine = null;
        private static bool _haltAutoReloads = false;
        private static int _emptyClicks = 0;
        private static float _heldEmptyTimer = 0f;
        private static float _emptyTimer = 0f;
        
        // m_nextShotTimer is used on auto weapons to manage shot cooldown... but also to manage out of ammo clicks with no variable to know which!
        // Using this instead to track the next shot timer when the magazine was emptied so we can properly account for shot cooldown on autos.
        private static float _emptyShotTimer = 0f;

        private static bool ShouldTriggerReload()
        {
            if (_cachedArchetype == null || !_cachedArchetype.m_weapon.m_inventory.CanReloadCurrent()) return false;

            if (Configuration.autoReloadAimingInstant && (PlayerLocomotion.AimToggleLock || InputMapper.GetButton.Invoke(InputAction.Aim, eFocusState.FPS)))
                return true;
            if (_emptyClicks >= Configuration.clicksToReload && CanCountClicks())
                return true;
            
            if (_haltAutoReloads) return false;

            if (CanTimeAutoReloads())
            {
                if (_cachedArchetype.m_archetypeData.FireMode == eWeaponFireMode.Auto)
                {
                    if (Configuration.autoReloadDelayHoldAuto >= 0f && _heldEmptyTimer >= Configuration.autoReloadDelayHoldAuto) return true;
                    if (Configuration.autoReloadDelayAuto >= 0f && _emptyTimer >= Configuration.autoReloadDelayAuto) return true;
                }
                else
                {
                    if (Configuration.autoReloadDelaySemi >= 0f && _emptyTimer >= Configuration.autoReloadDelaySemi) return true;
                }
            }

            return false;
        }

        private static bool CanCountClicks()
        {
            if (_cachedArchetype == null) return false;

            if (_cachedArchetype.m_archetypeData.FireMode == eWeaponFireMode.Auto)
                return Configuration.autoReloadBypassDelayAuto || (Clock.Time > _cachedArchetype.m_nextBurstTimer && Clock.Time > _emptyShotTimer);
            else
                return Configuration.clicksBypassDelay || Clock.Time > _cachedArchetype.m_nextBurstTimer;
        }

        private static bool CanTimeAutoReloads()
        {
            if (_cachedArchetype == null) return false;

            if (_cachedArchetype.m_archetypeData.FireMode == eWeaponFireMode.Auto)
                return Configuration.autoReloadBypassDelayAuto || (Clock.Time > _cachedArchetype.m_nextBurstTimer && Clock.Time > _emptyShotTimer);
            else
                return Configuration.autoReloadBypassDelaySemi || Clock.Time > _cachedArchetype.m_nextBurstTimer;
        }

        private static void Reset()
        {
            _haltAutoReloads = false;
            _heldEmptyTimer = 0f;
            _emptyTimer = 0f;
            _emptyClicks = 0;
            _emptyShotTimer = 0f;
        }

        [HarmonyPatch(typeof(BulletWeaponArchetype), nameof(BulletWeaponArchetype.OnWield))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void StartUpdateRoutine(BulletWeaponArchetype __instance)
        {
            if (__instance.m_owner == null || !__instance.m_owner.IsLocallyOwned) return;

            _cachedArchetype = __instance;
            if (_autoReloadRoutine != null)
            {
                ARPLogger.Warning("OnWield called, but a coroutine still exists.");
                Reset();
            }
            else
                _autoReloadRoutine = CoroutineManager.StartCoroutine(CollectionExtensions.WrapToIl2Cpp(UpdateAutoReload()));
        }

        [HarmonyPatch(typeof(BulletWeaponArchetype), nameof(BulletWeaponArchetype.OnUnWield))]
        [HarmonyWrapSafe]
        [HarmonyPostfix]
        private static void StopUpdateRoutine(BulletWeaponArchetype __instance)
        {
            if (__instance.m_owner == null || !__instance.m_owner.IsLocallyOwned) return;

            _cachedArchetype = null;
            if (_autoReloadRoutine != null) 
                CoroutineManager.StopCoroutine(_autoReloadRoutine);
            _autoReloadRoutine = null;
        }

        private static IEnumerator UpdateAutoReload()
        {
            float delta;
            float lastUpdateTime = Clock.Time;
            bool clickSoundTriggered = false; // Avoid double-playing the first "out of ammo" sound. Won't work past that, but too much effort to fix any more.
            
            _heldEmptyTimer = 0f;
            _emptyTimer = 0f;
            _emptyClicks = 0;
            _emptyShotTimer = 0f;
            uint archID = _cachedArchetype!.m_archetypeData.persistentID;

            while (true)
            {
                yield return null;
                delta = Clock.Time - lastUpdateTime;
                lastUpdateTime = Clock.Time;

                if (_cachedArchetype == null || _cachedArchetype.m_weapon == null)
                {
                    ARPLogger.Log("AutoReload coroutine has no cached archetype (OnUnWield was not called). Culling coroutine.");

                    _autoReloadRoutine = null;
                    Reset();
                    yield break;
                }

                BulletWeapon weapon = _cachedArchetype.m_weapon;
                // Compatibility with EWC changing archetype
                if (archID != weapon.ArchetypeID)
                {
                    _cachedArchetype = weapon.m_archeType;
                    archID = weapon.ArchetypeID;
                }

                // Reload may be triggered manually or by in-game Auto Reload too, so just reseting when the gun is reloading by any means.
                if (weapon.IsReloading)
                {
                    Reset();
                    continue;
                }

                if (_cachedArchetype.m_clip > 0)
                    continue;

                // --------------- Gun is out of ammo ---------------

                if (_emptyShotTimer == 0f)
                    _emptyShotTimer = _cachedArchetype.m_nextShotTimer;

                PlayerAgent owner = _cachedArchetype.m_owner;
                if (owner.Locomotion.IsRunning)
                {
                    _haltAutoReloads |= Configuration.sprintHaltsReload;
                    _heldEmptyTimer = 0f;
                    _emptyTimer = 0f;
                    continue;
                }

                if (owner.Interaction.m_bestSelectedInteract != _cachedInteract)
                {
                    _cachedInteract = _cachedArchetype.m_owner.Interaction.m_bestSelectedInteract;
                    _cachedInteractTimed = _cachedInteract?.TryCast<Interact_Timed>();
                }

                if (_cachedInteractTimed != null && _cachedInteractTimed.TimerIsActive)
                {
                    _haltAutoReloads |= Configuration.interactHaltsReload;
                    _heldEmptyTimer = 0f;
                    _emptyTimer = 0f;
                    continue;
                }

                if (weapon.FireButtonPressed && CanCountClicks())
                {
                    if ((clickSoundTriggered || !_cachedArchetype.m_clickTriggered) && _emptyClicks <= Configuration.clicksToReload)
                        weapon.TriggerAudio(weapon.AudioData.eventClick);
                    clickSoundTriggered = _cachedArchetype.m_clickTriggered;
                    _emptyClicks++;
                }

                if (!_haltAutoReloads && CanTimeAutoReloads())
                {
                    _emptyTimer += delta;

                    if (weapon.FireButton || InputMapper.HasGamepad && InputMapper.GetAxisKeyMouseGamepad(InputAction.GamepadFireTrigger, owner.InputFilter) > 0f)
                        _heldEmptyTimer += delta;
                }

                if (ShouldTriggerReload())
                {
                    weapon.m_inventory.TriggerReload();
                    clickSoundTriggered = false;
                }
            }
        }
    }
}
