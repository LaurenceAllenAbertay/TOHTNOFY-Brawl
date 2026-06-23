using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Owns the unit downed presentation queue.
    ///
    /// When a unit's health reaches zero, Unit.Die() enqueues a (victim, killer) pair
    /// here before unregistering the unit from gameplay systems.  The unit's GameObject
    /// remains active until DrainDownedQueue has played its downed animation.
    ///
    /// After the animation, one of two things happens depending on the unit type:
    ///   • PlayerUnits and EnemyUnits with leavesBodyOnDown = true call Unit.BecomeBody(),
    ///     freezing on the last downed frame and remaining on the map as a neutral obstacle.
    ///   • EnemyUnits with leavesBodyOnDown = false are fully unregistered and hidden —
    ///     the original "vanish on down" behaviour for minions, summons, etc.
    ///
    /// DrainDownedQueue is called from two places:
    ///   • AbilitySequencer.RunSequence  — after all ability effects resolve, passing ctx.caster
    ///     as the unit to return the camera to afterwards.
    ///   • TurnManager.TriggerEnvironmentEffects — after all tile effects resolve, passing null
    ///     (no single killer to return to; the camera stays on the last downed unit).
    ///
    /// Add this component to the same scene GameObject as UnitManager (or any persistent manager).
    /// </summary>
    public class UnitDownedSequencer : MonoBehaviour
    {
        public static UnitDownedSequencer Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Timing")]
        [Tooltip("How long to linger on the downed unit after the downed animation finishes " +
                 "before panning to the next or returning to the killer.")]
        [SerializeField] private float lingerAfterDownedSeconds = 0.5f;

        [Tooltip("Transition duration used when panning the camera to each downed unit.")]
        [SerializeField] private float cameraTransitionDuration = 0.8f;

        // ── Internal state ────────────────────────────────────────────────────

        private readonly Queue<PendingDowned> _queue = new Queue<PendingDowned>();
        // Tracks units whose downed state has already been presented inline by AbilitySequencer
        // so DrainDownedQueue can skip them and avoid a double-presentation.
        private readonly HashSet<Unit> _presentedInline = new HashSet<Unit>();
        private bool _isDraining = false;

        private CameraController _camera;

        // ── Types ─────────────────────────────────────────────────────────────

        private struct PendingDowned
        {
            public Unit victim;
            public Unit killer; // may be null (tile damage, debug kill, etc.)
        }

        // ── Unity lifecycle ───────────────────────────────────────────────────

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        private void Start()
        {
            _camera = FindAnyObjectByType<CameraController>();
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>
        /// Called by Unit.Die() immediately after gameplay systems (UnitManager, TurnManager)
        /// have been notified.  Enqueues the victim for presentation.
        /// </summary>
        public void EnqueueDowned(Unit victim, Unit killer)
        {
            if (victim == null) return;
            _queue.Enqueue(new PendingDowned { victim = victim, killer = killer });
        }

        /// <summary>
        /// Plays every pending downed animation one after the other, then pans the camera
        /// back to <paramref name="returnToUnit"/> when finished.
        ///
        /// Callers should yield on this coroutine:
        ///   yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue(killer));
        ///
        /// Safe to call when the queue is empty — returns immediately.
        /// </summary>
        public IEnumerator DrainDownedQueue(Unit returnToUnit)
        {
            // Only one drain may run at a time.  If somehow called re-entrantly, wait.
            while (_isDraining)
                yield return null;

            if (_queue.Count == 0)
                yield break;

            _isDraining = true;

            while (_queue.Count > 0)
            {
                var entry = _queue.Dequeue();

                // Skip any unit whose downed state was already presented inline by AbilitySequencer.
                if (_presentedInline.Contains(entry.victim))
                    continue;

                yield return StartCoroutine(PresentDowned(entry.victim));
            }

            _presentedInline.Clear();

            // Return camera to the killer / active unit after all downed animations are shown.
            if (returnToUnit != null && _camera != null && returnToUnit.gameObject != null)
            {
                yield return StartCoroutine(
                    _camera.TransitionTo(
                        _camera.UnitFocusPosition(returnToUnit),
                        cameraTransitionDuration));
            }

            _isDraining = false;
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Presents a single unit's downed animation without panning the camera —
        /// the camera is already on this unit because AbilitySequencer targeted them.
        ///
        /// Called inline from AbilitySequencer.PlayTargetEffectsWithAnimation immediately
        /// after the damage lands on a lethal hit.
        ///
        /// Marks the victim in _presentedInline so DrainDownedQueue skips them.
        /// </summary>
        public IEnumerator PresentDownedInline(Unit victim)
        {
            if (victim == null) yield break;

            // Mark as handled so DrainDownedQueue skips this entry.
            _presentedInline.Add(victim);

            var unitAnimator = victim.GetComponent<UnitAnimator>();
            var healthBar    = victim.GetComponentInChildren<UnitHealthBarDisplay>();

            // Start the downed animation immediately — don't wait for the health bar tween
            // first, as that would leave the unit sitting in Idle_Hurt for the tween duration.
            // The bar tween runs concurrently; we wait for both to finish before hiding the bar.
            unitAnimator?.PlayDowned();

            Coroutine downedWait = StartCoroutine(WaitForDownedAnimation(victim));
            Coroutine tweenWait  = healthBar != null ? StartCoroutine(healthBar.WaitForTweenComplete()) : null;

            if (downedWait != null) yield return downedWait;
            if (tweenWait  != null) yield return tweenWait;

            // Hide the health bar and status effects now that both have finished.
            healthBar?.HideImmediate();
            victim.GetComponentInChildren<StatusEffectIconDisplay>()?.HideImmediate();

            // Linger so the player can register what happened before the camera moves on.
            yield return new WaitForSeconds(lingerAfterDownedSeconds);

            // Transition the unit to a body or remove it — same logic as PresentDowned.
            bool shouldLeaveBody = victim is PlayerUnit ||
                                   (victim is EnemyUnit enemy && enemy.leavesBodyOnDown);

            if (shouldLeaveBody)
                victim.BecomeBody();
            else
            {
                UnitManager.UnregisterUnit(victim);
                victim.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Pans camera to the victim, plays their downed animation to completion,
        /// lingers briefly, then either transitions the unit into a body or fully
        /// hides it depending on whether it should leave a body on the map.
        /// </summary>
        private IEnumerator PresentDowned(Unit victim)
        {
            if (victim == null) yield break;

            // ── 1. Pan camera to the downed unit ──────────────────────────────
            if (_camera != null)
            {
                yield return StartCoroutine(
                    _camera.TransitionTo(
                        _camera.UnitFocusPosition(victim),
                        cameraTransitionDuration));
            }

            // ── 1.5. Start the downed animation immediately. The health bar tween runs
            //         concurrently — we wait for both to finish before hiding the bar.
            //         Waiting for the tween first would leave the unit in Idle_Hurt for
            //         its entire duration before the downed animation begins.
            var healthBar    = victim.GetComponentInChildren<UnitHealthBarDisplay>();
            var unitAnimator = victim.GetComponent<UnitAnimator>();

            unitAnimator?.PlayDowned();

            Coroutine downedWait = StartCoroutine(WaitForDownedAnimation(victim));
            Coroutine tweenWait  = healthBar != null ? StartCoroutine(healthBar.WaitForTweenComplete()) : null;

            if (downedWait != null) yield return downedWait;
            if (tweenWait  != null) yield return tweenWait;

            // ── 2.5. Hide the health bar and status effects now that both have finished.
            healthBar?.HideImmediate();
            victim.GetComponentInChildren<StatusEffectIconDisplay>()?.HideImmediate();

            // ── 3. Linger so the player can register what happened ─────────────
            yield return new WaitForSeconds(lingerAfterDownedSeconds);

            // ── 4. Become a body, or vanish entirely ──────────────────────────
            // PlayerUnits always leave a body (they can be revived).
            // EnemyUnits leave a body only when leavesBodyOnDown is true — designers
            // can disable this for summons, minions, or any enemy that should vanish cleanly.
            bool shouldLeaveBody = victim is PlayerUnit ||
                                   (victim is EnemyUnit enemy && enemy.leavesBodyOnDown);

            if (shouldLeaveBody)
            {
                // Freeze on the last frame of the downed animation and remain on the map
                // as a neutral obstacle. BecomeBody() re-occupies the tile and notifies
                // UnitManager so targeting and pathfinding see the body correctly.
                victim.BecomeBody();
            }
            else
            {
                // Fully remove from all manager lists and hide the GameObject.
                // This is the original behaviour for enemies that should disappear on down.
                UnitManager.UnregisterUnit(victim);
                victim.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Waits until the Animator on <paramref name="victim"/> has finished playing
        /// the down state. Waits up to maxWait seconds before giving up gracefully.
        /// </summary>
        private IEnumerator WaitForDownedAnimation(Unit victim)
        {
            var animator = victim.GetComponent<Animator>();
            if (animator == null) yield break;

            // Give the animator one frame to register the state change.
            yield return new WaitForSeconds(0.1f);

            const float maxWait = 5f;
            float elapsed = 0f;

            while (elapsed < maxWait)
            {
                var stateInfo = animator.GetCurrentAnimatorStateInfo(0);

                if (stateInfo.IsName("Downed") && stateInfo.normalizedTime >= 1.0f && !stateInfo.loop)
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Timed out — animation may be missing or the clip is set to loop (shouldn't be).
            Debug.LogWarning($"[UnitDownedSequencer] Downed animation timed out for {victim.name}.");
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>True while DrainDownedQueue is actively presenting animations.</summary>
        public bool IsDraining => _isDraining;

        /// <summary>True if there are downed units waiting to be presented.</summary>
        public bool HasPendingDowned => _queue.Count > 0;
    }
}