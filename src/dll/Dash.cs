using System;
using HarmonyLib;
using InControl;
using UnityEngine;

namespace SevenDashesToDie
{
    // ---------------------------------------------------------------------------------
    // The ability itself.
    //
    // The local player is driven by UFPS' vp_FPController, not by the generic
    // Entity.motion path (EntityPlayerLocal.MoveEntityHeaded branches into the controller;
    // the motion arithmetic in that same method belongs to the ladder branch). The
    // controller already exposes exactly what a dash needs:
    //
    //   AddSoftForce(force, frames)  spreads one impulse over N frames -> a burst, not a jolt
    //   ScaleFallSpeed(f)            scales the accumulated fall speed -> a flat air dash
    //   Grounded                     -> when to refill air charges
    //
    // Because the controller performs the movement, its own collision sweep applies: a dash
    // cannot push the player through geometry the way a raw motion write could.
    //
    // The dash force is derived from the controller's own MotorJumpForce rather than from a
    // hardcoded constant, so it stays in the engine's force units even if a game update
    // retunes them.
    // ---------------------------------------------------------------------------------
    public static class Dash
    {
        public const string PerkName = "perkRuleTwoDoubleTap";

        /// <summary>Dash impulse as a multiple of the controller's own jump force, at rank 1.</summary>
        const float BaseForceFactor = 2.2f;

        /// <summary>Frames the impulse is spread over. Higher = smoother and longer.</summary>
        const float SoftForceFrames = 6f;

        /// <summary>Rank 1..5 -> force multiplier.</summary>
        static readonly float[] RankForce = { 1.00f, 1.12f, 1.25f, 1.35f, 1.50f };

        /// <summary>Rank 1..5 -> cooldown multiplier.</summary>
        static readonly float[] RankCooldown = { 1.00f, 0.90f, 0.80f, 0.70f, 0.60f };

        /// <summary>Air dashing unlocks at this rank.</summary>
        const int AirDashRank = 3;

        /// <summary>A second air charge (the "double" in Double Tap) unlocks at this rank.</summary>
        const int DoubleAirDashRank = 5;

        // Per-player state. There is one local player, but it is recreated on respawn and
        // relog, so the state is tied to the instance it belongs to.
        static EntityPlayerLocal owner;
        static float nextDashTime;
        static int airCharges;
        static bool wasGrounded;

        // Debug measurement (Gears switch "DebugLog").
        static bool measuring;
        static float measureUntil;
        static Vector3 measureFrom;
        static bool measuredInAir;

        [HarmonyPatch(typeof(EntityPlayerLocal), "Update")]
        static class Patch_EntityPlayerLocal_Update
        {
            static void Postfix(EntityPlayerLocal __instance)
            {
                try { Tick(__instance); }
                catch (Exception e)
                {
                    Log.Error(SevenDashesMod.LogPrefix + "dash update failed: " + e);
                }
            }
        }

        static void Tick(EntityPlayerLocal player)
        {
            if (player == null) return;
            if (!ReferenceEquals(player, owner)) Reset(player);

            vp_FPController fpc = player.vp_FPController;
            if (fpc == null) return;

            // Refill air charges the moment the player touches down.
            bool grounded = fpc.enabled ? fpc.Grounded : player.onGround;
            if (grounded && !wasGrounded) airCharges = MaxAirCharges(GetRank(player));
            wasGrounded = grounded;

            ReportMeasurement(player);

            // Check the key before touching Settings: this runs every frame, and every
            // settings read goes through reflection into Gears.
            if (!WasDashPressed(player)) return;
            if (!Settings.Enabled) return;
            TryDash(player, fpc, grounded);
        }

        static void Reset(EntityPlayerLocal player)
        {
            owner = player;
            nextDashTime = 0f;
            airCharges = 0;
            wasGrounded = false;
            measuring = false;
        }

        static bool WasDashPressed(EntityPlayerLocal player)
        {
            PlayerActionsLocal input = player.playerInput;
            if (input == null) return false;

            PlayerAction action = DashInput.Get(input);
            return action != null && action.WasPressed;
        }

        // -----------------------------------------------------------------------------

        static int GetRank(EntityPlayerLocal player)
        {
            if (!Settings.RequirePerk) return RankForce.Length;
            if (player.Progression == null) return 0;

            ProgressionValue pv = player.Progression.GetProgressionValue(PerkName);
            if (pv == null) return 0;
            return Mathf.Clamp(pv.Level, 0, RankForce.Length);
        }

        static int MaxAirCharges(int rank)
        {
            if (rank >= DoubleAirDashRank) return 2;
            if (rank >= AirDashRank) return 1;
            return 0;
        }

        static void TryDash(EntityPlayerLocal player, vp_FPController fpc, bool grounded)
        {
            int rank = GetRank(player);
            if (rank <= 0) return;

            if (!CanAct(player, fpc)) return;
            if (Time.time < nextDashTime) return;
            if (!grounded && airCharges <= 0) return;

            float cost = Settings.StaminaCost;
            Stat stamina = player.Stats != null ? player.Stats.Stamina : null;
            if (cost > 0f && (stamina == null || stamina.Value < cost)) return;

            Vector3 dir = DashDirection(player);
            if (dir.sqrMagnitude < 0.001f) return;

            float force = fpc.MotorJumpForce * BaseForceFactor
                          * RankForce[rank - 1] * Settings.ForceScale;

            // In the air, cancel the accumulated fall so the dash reads as a flat glide
            // instead of a diagonal dive.
            if (!grounded)
            {
                fpc.ScaleFallSpeed(0f);
                airCharges--;
            }

            fpc.AddSoftForce(dir * force, SoftForceFrames);

            if (cost > 0f && stamina != null) stamina.Value = stamina.Value - cost;
            nextDashTime = Time.time + Settings.CooldownSeconds * RankCooldown[rank - 1];

            DashSound.Play(Settings.Volume);

            if (Settings.DebugLog) StartMeasurement(player, grounded, force, rank);
        }

        /// <summary>WASD relative to where the player is facing; no input dashes forward.</summary>
        static Vector3 DashDirection(EntityPlayerLocal player)
        {
            // ⚠ Read the axes from InControl, NOT from EntityPlayerLocal.movementInput.
            // movementInput is filled by PlayerMoveController.Update and consumed by
            // EntityPlayerLocal.MoveByInput; Unity does not order those against our postfix
            // on EntityPlayerLocal.Update, and in practice it reads back as zero there - so
            // every dash fell through to the "no input" case and went straight ahead,
            // whichever way the player was actually moving. The action set is live state and
            // has no such ordering dependency.
            //
            // PlayerActionsLocal.Move was built as
            // CreateTwoAxisPlayerAction(negativeX: MoveLeft, positiveX: MoveRight,
            //                           negativeY: MoveBack, positiveY: MoveForward)
            // so X is strafe (+right) and Y is forward (+forward).
            Vector2 move = Vector2.zero;
            PlayerActionsLocal input = player.playerInput;
            if (input != null && input.Move != null) move = new Vector2(input.Move.X, input.Move.Y);

            // MoveByInput negates both axes under the FlipControls effect. Mirror that, or a
            // flipped player would dash away from the direction they are walking.
            if (Effect(player, PassiveEffects.FlipControls) > 0f) move = -move;

            Vector3 local = new Vector3(move.x, 0f, move.y);
            if (local.sqrMagnitude < 0.04f) local = Vector3.forward; // below deadzone: straight ahead
            local.Normalize();

            // Entity.rotation is euler degrees; only the yaw matters for a flat dash.
            Vector3 dir = Quaternion.Euler(0f, player.rotation.y, 0f) * local;
            dir.y = 0f;
            return dir.sqrMagnitude < 0.001f ? Vector3.zero : dir.normalized;
        }

        /// <summary>
        /// One passive effect for this player, with the same argument set MoveByInput uses.
        /// </summary>
        static float Effect(EntityAlive player, PassiveEffects effect)
        {
            return EffectManager.GetValue(effect, null, 0f, player, null,
                                          default(FastTags<TagGroup.Global>),
                                          true, true, true, true, true, 1, true, false);
        }

        /// <summary>States in which a dash must not fire.</summary>
        static bool CanAct(EntityPlayerLocal player, vp_FPController fpc)
        {
            if (player.IsDead() || !player.IsAlive()) return false;
            if (player.AttachedToEntity != null) return false;   // vehicle / turret
            if (player.isLadderAttached) return false;
            if (player.IsSwimming()) return false;
            if (player.IsFlyMode != null && player.IsFlyMode.Value) return false;
            if (!fpc.enabled) return false;
            // MoveByInput clears all movement input under this effect; a dash must obey it too.
            if (Effect(player, PassiveEffects.DisableMovement) > 0f) return false;

            LocalPlayerUI ui = LocalPlayerUI.GetUIForPlayer(player);
            if (ui != null && ui.windowManager != null && ui.windowManager.IsInputActive()) return false;

            return true;
        }

        // -----------------------------------------------------------------------------
        // Tuning aid: measures how far a dash actually carried and logs it, so the force
        // can be set from a number instead of from a feeling. Off unless the Gears
        // "DebugLog" switch is on.
        // -----------------------------------------------------------------------------

        static float pendingForce;
        static int pendingRank;

        static void StartMeasurement(EntityPlayerLocal player, bool grounded, float force, int rank)
        {
            measuring = true;
            measureFrom = player.position;
            measureUntil = Time.time + 0.8f;
            measuredInAir = !grounded;
            pendingForce = force;
            pendingRank = rank;
        }

        static void ReportMeasurement(EntityPlayerLocal player)
        {
            if (!measuring || Time.time < measureUntil) return;
            measuring = false;

            Vector3 delta = player.position - measureFrom;
            delta.y = 0f;
            Log.Out(string.Format(SevenDashesMod.LogPrefix +
                "dash rank {0} {1}: force {2:0.###}, travelled {3:0.00} m in 0.8 s, air charges left {4}",
                pendingRank, measuredInAir ? "(air)" : "(ground)", pendingForce, delta.magnitude, airCharges));
        }
    }
}
