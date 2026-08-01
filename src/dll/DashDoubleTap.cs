using InControl;
using UnityEngine;

namespace SevenDashesToDie
{
    // ---------------------------------------------------------------------------------
    // The second way to trigger a dash: tap a movement key twice. Off by default (Gears
    // switch "Double tap"), because a mis-detection costs stamina and moves the player.
    //
    // The taps are read from PlayerActionsLocal's own MoveForward / MoveBack / MoveLeft /
    // MoveRight actions rather than from Unity's raw keyboard, which buys three things for
    // free: it follows the player's rebound keys, it is the same source the ground dash
    // already reads its direction from (see the note in Dash.DashDirection about
    // EntityPlayerLocal.movementInput reading back as zero from our postfix), and on a
    // gamepad it works off whatever those actions are bound to instead of ignoring the
    // controller entirely.
    //
    // The rule is the one a double CLICK uses: press, release, press again, with the two
    // presses no further apart than the window. Measuring from the first press rather than
    // from the release is what keeps "hold W across the map, let go, walk on" from counting
    // as a double tap - the hold itself eats the window. The release in between is required
    // so that a key repeat or a stuck key can never chain.
    //
    // ⚠ Poll() runs every frame, and every settings read goes through reflection into Gears.
    // So the tracking is pure local arithmetic and the settings are only touched on the one
    // frame a genuine second press lands - a few times a second at worst, not 60.
    // ---------------------------------------------------------------------------------
    public static class DashDoubleTap
    {
        /// <summary>Player-local direction per tracked action; index order matches actions[].</summary>
        static readonly Vector3[] Locals =
        {
            Vector3.forward, Vector3.back, Vector3.left, Vector3.right
        };

        static PlayerActionsLocal owner;
        static readonly PlayerAction[] actions = new PlayerAction[4];
        static readonly float[] firstPressAt = new float[4];
        static readonly bool[] releasedSince = new bool[4];

        /// <summary>
        /// Advances the tap tracker one frame. Returns the index of a direction that was just
        /// double-tapped, or -1. Must be called every frame, before any early-out.
        /// </summary>
        public static int Poll(EntityPlayerLocal player)
        {
            if (player == null || !Bind(player.playerInput)) return -1;

            // Time.unscaledTime, not Time.time: the window is a human reflex measured in
            // milliseconds and has no business stretching when the game slows down.
            float now = Time.unscaledTime;
            int hit = -1;

            for (int i = 0; i < actions.Length; i++)
            {
                PlayerAction a = actions[i];
                if (a == null) continue;

                if (a.WasReleased) releasedSince[i] = true;
                if (!a.WasPressed) continue;

                // firstPressAt > 0 means "armed": a first press was seen at all.
                bool candidate = hit < 0 && releasedSince[i] && firstPressAt[i] > 0f;
                if (candidate && Settings.DoubleTap &&
                    now - firstPressAt[i] <= Settings.DoubleTapWindowSeconds)
                {
                    hit = i;
                    firstPressAt[i] = 0f;   // a third tap starts a fresh pair, it does not chain
                    releasedSince[i] = false;
                    continue;
                }

                firstPressAt[i] = now;
                releasedSince[i] = false;
            }

            return hit;
        }

        /// <summary>World direction for an index returned by <see cref="Poll"/>.</summary>
        public static Vector3 Direction(EntityPlayerLocal player, int index)
        {
            if (player == null || index < 0 || index >= Locals.Length) return Vector3.zero;

            Vector3 local = Locals[index];

            // MoveByInput negates movement input under FlipControls. Mirror it here too, or
            // the dash goes the opposite way from the two taps that just triggered it.
            if (Dash.Effect(player, PassiveEffects.FlipControls) > 0f) local = -local;

            return Dash.WorldDirection(player, local);
        }

        /// <summary>
        /// Caches the four movement actions for this input set. The set is recreated on relog
        /// and for a second local player, so it is re-read whenever the instance changes -
        /// and the half-finished taps of the old set are dropped with it.
        /// </summary>
        static bool Bind(PlayerActionsLocal input)
        {
            if (input == null) return false;
            if (ReferenceEquals(input, owner)) return true;

            actions[0] = input.MoveForward;
            actions[1] = input.MoveBack;
            actions[2] = input.MoveLeft;
            actions[3] = input.MoveRight;
            for (int i = 0; i < actions.Length; i++)
            {
                firstPressAt[i] = 0f;
                releasedSince[i] = false;
            }

            owner = input;
            return true;
        }
    }
}
