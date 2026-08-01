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

        /// <summary>
        /// Dash impulse as a multiple of the controller's own jump force, at rank 1.
        ///
        /// Play-tested value, not an estimate: 2.2 carried ~25 m from a standstill in 0.8 s,
        /// which was far too much. Dialling the in-game Force slider found 33% short and 40%
        /// right, so 2.2 x 0.40 = 0.88 is now the default and the slider sits at 100% again.
        /// That is roughly 10 m on the flat at rank 1-3.
        /// </summary>
        const float BaseForceFactor = 0.88f;

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

        // --- "Momentum Lite" ----------------------------------------------------------
        // vp_FPController.FixedMove starts with
        //     m_MoveDirection = m_MoveDirection + m_ExternalForce
        // where m_MoveDirection is the motor (your walk/sprint) and m_ExternalForce is the
        // pot AddSoftForce pays into. So a raw impulse stacks fully on top of sprinting, and
        // a sprint dash was far stronger than a standing one - by much more than a rank
        // step, which made the 1.00 -> 1.12 progression invisible.
        //
        // Instead of deleting that momentum (which reads as hitting a wall), only a slice of
        // it counts, and only the slice already travelling the way you dash:
        //     along  = max(0, dot(velocity, dashDir))
        //     target = min(dashSpeed + along * Share, dashSpeed * Cap)
        //     gain   = max(0, target - along)
        // Sprinting forward and dashing forward is still the fastest option; sprinting
        // forward and dashing sideways gives a clean sideways dodge instead of a diagonal
        // drift. `along` is clamped at zero on purpose: a backdash while sprinting forward is
        // the panic button, and must not be the weakest dash in the game.

        /// <summary>How much of the momentum already going the dash way is kept.</summary>
        const float MomentumShare = 0.25f;

        /// <summary>Ceiling on the momentum bonus, as a multiple of the plain dash speed.</summary>
        const float MomentumCap = 1.2f;

        /// <summary>
        /// Floor for the momentum reduction, as a fraction of the unmodified impulse. This is
        /// a safety net, not a tuning knob: SpeedPerImpulse below is a MODEL of UFPS'
        /// internals, and if it is wrong the worst case has to stay playable rather than
        /// turning the dash into a twitch or a catapult.
        /// </summary>
        const float MinImpulseFraction = 0.3f;

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

            // The double-tap tracker needs to see every frame, so it runs before the
            // early-out. It is built to read no setting until a genuine second press lands.
            int tapped = DashDoubleTap.Poll(player);
            bool keyed = WasDashPressed(player);

            // Check the input before touching Settings: this runs every frame, and every
            // settings read goes through reflection into Gears.
            if (!keyed && tapped < 0) return;
            if (!Settings.Enabled) return;

            // A dash from the dash key follows wherever the player is currently steering; a
            // dash from a double tap follows the key that was tapped, so that tapping A twice
            // dodges left even while W is held.
            Vector3 dir = keyed ? DashDirection(player) : DashDoubleTap.Direction(player, tapped);
            TryDash(player, fpc, grounded, dir);
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

        static void TryDash(EntityPlayerLocal player, vp_FPController fpc, bool grounded, Vector3 dir)
        {
            if (dir.sqrMagnitude < 0.001f) return;

            int rank = GetRank(player);
            if (rank <= 0) return;

            if (!CanAct(player, fpc)) return;
            if (Time.time < nextDashTime) return;
            if (!grounded && airCharges <= 0) return;

            float cost = Settings.StaminaCost;
            Stat stamina = player.Stats != null ? player.Stats.Stamina : null;
            if (cost > 0f && (stamina == null || stamina.Value < cost)) return;

            // The plain dash, unchanged: this is the impulse that play-tested well from a
            // standstill, and Momentum Lite only ever reduces it.
            float impulse = fpc.MotorJumpForce * BaseForceFactor
                            * RankForce[rank - 1] * Settings.ForceScale;

            float perImpulse = SpeedPerImpulse(fpc);
            float dashSpeed = impulse * perImpulse;

            // Only momentum already heading the dash way counts, and never negatively.
            float along = Mathf.Max(0f, Vector3.Dot(HorizontalVelocity(fpc), dir));
            float target = Mathf.Min(dashSpeed + along * MomentumShare, dashSpeed * MomentumCap);
            float gain = Mathf.Max(0f, target - along);

            float force = perImpulse > 0.0001f ? gain / perImpulse : impulse;
            force = Mathf.Clamp(force, impulse * MinImpulseFraction, impulse);

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

            if (Settings.DebugLog) StartMeasurement(player, grounded, force, rank, along, target);
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
            return WorldDirection(player, local);

            // ⚠ The flip is applied to the INPUT above, not to `local` here, and the two are
            // not the same thing: with no input at all the fallback is "where you look",
            // which must stay unflipped. Mirroring it would send a standing player backwards.
        }

        /// <summary>
        /// A player-local direction (x = right, z = forward) turned into a flat world
        /// direction. Entity.rotation is euler degrees; only the yaw matters for a flat dash.
        /// </summary>
        internal static Vector3 WorldDirection(EntityPlayerLocal player, Vector3 local)
        {
            local.y = 0f;
            if (local.sqrMagnitude < 0.001f) return Vector3.zero;
            local.Normalize();

            Vector3 dir = Quaternion.Euler(0f, player.rotation.y, 0f) * local;
            dir.y = 0f;
            return dir.sqrMagnitude < 0.001f ? Vector3.zero : dir.normalized;
        }

        /// <summary>The player's speed across the ground, in m/s.</summary>
        static Vector3 HorizontalVelocity(vp_FPController fpc)
        {
            Vector3 v = fpc.Velocity; // = CharacterController.velocity, i.e. real m/s
            v.y = 0f;
            return v;
        }

        /// <summary>
        /// Correction applied to the analytic model below, from 15 logged dashes: the model
        /// under-predicted the peak speed by a median factor of 1.74 (min 1.43, max 2.09,
        /// sigma 0.16 - the scatter is sampling noise, since the peak lasts a tick or two and
        /// is read once per frame). The measurement is the credible side of that gap: a peak
        /// of ~34 m/s could not have produced the 25 m covered in 0.8 s, ~60 m/s can.
        /// </summary>
        const float ModelCorrection = 1.74f;

        /// <summary>
        /// Metres per second gained per unit of AddSoftForce impulse.
        ///
        /// ⚠ THIS IS A MODEL, not a measurement. The impulse lands in m_ExternalForce, which
        /// vp_FPController.FixedMove adds to the per-tick move delta and UpdateForces then
        /// shrinks each tick with
        ///     m_ExternalForce /= 1 + PhysicsForceDamping * AdjustedTimeScale
        /// (a division, not a multiplication - so the per-tick retention is 1/(1+damping)).
        /// AddSoftForce feeds impulse/frames in per tick, so the force builds as a truncated
        /// geometric series before it decays. Speed is that peak divided by the tick length.
        ///
        /// What the model cannot see is what SmoothMove does afterwards: it hands
        /// m_MoveDirection to a vp_PlayerEventHandler.Move message and rescales by
        /// Time.deltaTime, and the chain past that point is not worth reverse-engineering
        /// blind. So the result is treated as an estimate: the caller clamps the computed
        /// impulse into [MinImpulseFraction, 1] x the plain impulse, and the DebugLog switch
        /// prints predicted against measured speed so one test run can correct it.
        /// </summary>
        static float SpeedPerImpulse(vp_FPController fpc)
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 0.0001f) dt = 0.02f;

            float damping = Mathf.Max(0f, fpc.PhysicsForceDamping);
            float retain = 1f / (1f + damping);
            float n = Mathf.Max(1f, SoftForceFrames);

            // Sum of retain^1 .. retain^n; degenerates to n when there is no damping.
            float buildup = damping <= 0.0001f
                ? n
                : retain * (1f - Mathf.Pow(retain, n)) / (1f - retain);

            return ModelCorrection * buildup / (n * dt);
        }

        /// <summary>
        /// One passive effect for this player, with the same argument set MoveByInput uses.
        /// </summary>
        internal static float Effect(EntityAlive player, PassiveEffects effect)
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
        // Tuning aid. Off unless the Gears "DebugLog" switch is on.
        //
        // Two jobs. It reports how far a dash actually carried, so the force can be set from
        // a number instead of a feeling - and it prints the SpeedPerImpulse model's predicted
        // speed next to the peak speed actually reached, so one test run says whether that
        // model holds. If predicted and measured disagree, the ratio between them is the
        // correction factor for SpeedPerImpulse; until then the impulse clamp keeps the
        // damage contained.
        // -----------------------------------------------------------------------------

        static float pendingForce;
        static int pendingRank;
        static float pendingPredicted;
        static float pendingAlong;
        static float pendingPeak;

        static void StartMeasurement(EntityPlayerLocal player, bool grounded, float force, int rank,
                                     float along, float predictedTarget)
        {
            measuring = true;
            measureFrom = player.position;
            measureUntil = Time.time + 0.8f;
            measuredInAir = !grounded;
            pendingForce = force;
            pendingRank = rank;
            pendingAlong = along;
            pendingPredicted = predictedTarget;
            pendingPeak = 0f;
        }

        static void ReportMeasurement(EntityPlayerLocal player)
        {
            if (!measuring) return;

            // Sample every frame: the peak lands within a few ticks of the impulse, so
            // reading only at the end of the window would miss it entirely.
            vp_FPController fpc = player.vp_FPController;
            if (fpc != null)
            {
                float speed = HorizontalVelocity(fpc).magnitude;
                if (speed > pendingPeak) pendingPeak = speed;
            }

            if (Time.time < measureUntil) return;
            measuring = false;

            Vector3 delta = player.position - measureFrom;
            delta.y = 0f;

            string model = pendingPredicted > 0.01f
                ? string.Format("predicted {0:0.0} -> measured {1:0.0} m/s (model x{2:0.00})",
                                pendingPredicted, pendingPeak, pendingPeak / pendingPredicted)
                : string.Format("peak {0:0.0} m/s", pendingPeak);

            Log.Out(string.Format(SevenDashesMod.LogPrefix +
                "dash rank {0} {1}: entry speed {2:0.0} m/s, force {3:0.###}, {4}, " +
                "travelled {5:0.00} m in 0.8 s, air charges left {6}",
                pendingRank, measuredInAir ? "(air)" : "(ground)", pendingAlong,
                pendingForce, model, delta.magnitude, airCharges));
        }
    }
}
