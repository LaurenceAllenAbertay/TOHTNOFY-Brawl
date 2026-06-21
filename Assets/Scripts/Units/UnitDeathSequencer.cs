using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    /// <summary>
    /// Owns the unit death presentation queue.
    ///
    /// When a unit's health reaches zero, Unit.Die() enqueues a (victim, killer) pair
    /// here before unregistering the unit from gameplay systems.  The unit's GameObject
    /// remains active until DrainDeathQueue has played its death animation.
    ///
    /// After the animation, one of two things happens depending on the unit type:
    ///   • PlayerUnits and EnemyUnits with leavesBodyOnDeath = true call Unit.BecomeBody(),
    ///     freezing on the last death frame and remaining on the map as a neutral obstacle.
    ///   • EnemyUnits with leavesBodyOnDeath = false are fully unregistered and hidden —
    ///     the original "vanish on death" behaviour for minions, summons, etc.
    ///
    /// DrainDeathQueue is called from two places:
    ///   • AbilitySequencer.RunSequence  — after all ability effects resolve, passing ctx.caster
    ///     as the unit to return the camera to afterwards.
    ///   • TurnManager.TriggerEnvironmentEffects — after all tile effects resolve, passing null
    ///     (no single killer to return to; the camera stays on the last death).
    ///
    /// Add this component to the same scene GameObject as UnitManager (or any persistent manager).
    /// </summary>
    public class UnitDeathSequencer : MonoBehaviour
    {
        public static UnitDeathSequencer Instance { get; private set; }

        // ── Inspector ─────────────────────────────────────────────────────────

        [Header("Timing")]
        [Tooltip("How long to linger on the dead unit after the death animation finishes " +
                 "before panning to the next death or returning to the killer.")]
        [SerializeField] private float lingerAfterDeathSeconds = 0.5f;

        [Tooltip("Transition duration used when panning the camera to each dying unit.")]
        [SerializeField] private float cameraTransitionDuration = 0.8f;

        // ── Internal state ────────────────────────────────────────────────────

        private readonly Queue<PendingDeath> _queue = new Queue<PendingDeath>();
        // Tracks units whose death has already been presented inline by AbilitySequencer
        // so DrainDeathQueue can skip them and avoid a double-presentation.
        private readonly HashSet<Unit> _presentedInline = new HashSet<Unit>();
        private bool _isDraining = false;

        private CameraController _camera;

        // ── Types ─────────────────────────────────────────────────────────────

        private struct PendingDeath
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
        public void EnqueueDeath(Unit victim, Unit killer)
        {
            if (victim == null) return;
            _queue.Enqueue(new PendingDeath { victim = victim, killer = killer });
        }

        /// <summary>
        /// Plays every pending death animation one after the other, then pans the camera
        /// back to <paramref name="returnToUnit"/> when finished.
        ///
        /// Callers should yield on this coroutine:
        ///   yield return StartCoroutine(UnitDeathSequencer.Instance.DrainDeathQueue(killer));
        ///
        /// Safe to call when the queue is empty — returns immediately.
        /// </summary>
        public IEnumerator DrainDeathQueue(Unit returnToUnit)
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

                // Skip any unit whose death was already presented inline by AbilitySequencer.
                if (_presentedInline.Contains(entry.victim))
                    continue;

                yield return StartCoroutine(PresentDeath(entry.victim));
            }

            _presentedInline.Clear();

            // Return camera to the killer / active unit after all deaths are shown.
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
        /// Presents a single unit's death animation without panning the camera —
        /// the camera is already on this unit because AbilitySequencer targeted them.
        ///
        /// Called inline from AbilitySequencer.PlayTargetEffectsWithAnimation immediately
        /// after the hurt animation and health bar tween finish on a lethal hit, so the
        /// player sees: hurt reaction → bar drains to zero → bar hides → death animation.
        ///
        /// Marks the victim in _presentedInline so DrainDeathQueue skips them.
        /// </summary>
        public IEnumerator PresentDeathInline(Unit victim)
        {
            if (victim == null) yield break;

            // Mark as handled so DrainDeathQueue skips this entry.
            _presentedInline.Add(victim);

            // Play death animation and wait for it to complete.
            var unitAnimator = victim.GetComponent<UnitAnimator>();
            if (unitAnimator != null)
            {
                unitAnimator.PlayDeath();
                yield return StartCoroutine(WaitForDeathAnimation(victim));
            }

            // Linger so the player can register the death before the camera moves on.
            yield return new WaitForSeconds(lingerAfterDeathSeconds);

            // Transition the unit to a body or remove it — same logic as PresentDeath.
            bool shouldLeaveBody = victim is PlayerUnit ||
                                   (victim is EnemyUnit enemy && enemy.leavesBodyOnDeath);

            if (shouldLeaveBody)
                victim.BecomeBody();
            else
            {
                UnitManager.UnregisterUnit(victim);
                victim.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Pans camera to the victim, plays their death animation to completion,
        /// lingers briefly, then either transitions the unit into a body or fully
        /// hides it depending on whether it should leave a body on the map.
        /// </summary>
        private IEnumerator PresentDeath(Unit victim)
        {
            if (victim == null) yield break;

            // ── 1. Pan camera to the dying unit ───────────────────────────────
            if (_camera != null)
            {
                yield return StartCoroutine(
                    _camera.TransitionTo(
                        _camera.UnitFocusPosition(victim),
                        cameraTransitionDuration));
            }

            // ── 1.5. Wait for any residual health bar tween, then hide bar and
            //         status effects before the death animation starts.
            //         (The camera pan is typically longer than the tween, but we
            //         wait explicitly so tile-effect deaths are handled correctly
            //         regardless of timing.)
            var healthBar = victim.GetComponentInChildren<UnitHealthBarDisplay>();
            if (healthBar != null)
            {
                yield return StartCoroutine(healthBar.WaitForTweenComplete());
                healthBar.HideImmediate();
            }
            victim.GetComponentInChildren<StatusEffectIconDisplay>()?.HideImmediate();

            // ── 2. Play death animation at full speed ─────────────────────────
            var unitAnimator = victim.GetComponent<UnitAnimator>();
            if (unitAnimator != null)
            {
                unitAnimator.PlayDeath();
                yield return StartCoroutine(WaitForDeathAnimation(victim));
            }

            // ── 3. Linger so the player can register what happened ─────────────
            yield return new WaitForSeconds(lingerAfterDeathSeconds);

            // ── 4. Become a body, or vanish entirely ──────────────────────────
            // PlayerUnits always leave a body (they can be revived).
            // EnemyUnits leave a body only when leavesBodyOnDeath is true — designers
            // can disable this for summons, minions, or any enemy that should vanish cleanly.
            bool shouldLeaveBody = victim is PlayerUnit ||
                                   (victim is EnemyUnit enemy && enemy.leavesBodyOnDeath);

            if (shouldLeaveBody)
            {
                // Freeze on the last frame of the death animation and remain on the map
                // as a neutral obstacle. BecomeBody() re-occupies the tile and notifies
                // UnitManager so targeting and pathfinding see the body correctly.
                victim.BecomeBody();
            }
            else
            {
                // Fully remove from all manager lists and hide the GameObject.
                // This is the original behaviour for enemies that should disappear on death.
                UnitManager.UnregisterUnit(victim);
                victim.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// Waits until the Animator on <paramref name="victim"/> has finished playing
        /// the Death state. Waits up to maxWait seconds before giving up gracefully.
        /// </summary>
        private IEnumerator WaitForDeathAnimation(Unit victim)
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

                if (stateInfo.IsName("Death") && stateInfo.normalizedTime >= 1.0f && !stateInfo.loop)
                    yield break;

                elapsed += Time.deltaTime;
                yield return null;
            }

            // Timed out — animation may be missing or the clip is set to loop (shouldn't be).
            Debug.LogWarning($"[UnitDeathSequencer] Death animation timed out for {victim.name}.");
        }

        // ── Queries ───────────────────────────────────────────────────────────

        /// <summary>True while DrainDeathQueue is actively presenting animations.</summary>
        public bool IsDraining => _isDraining;

        /// <summary>True if there are deaths waiting to be presented.</summary>
        public bool HasPendingDeaths => _queue.Count > 0;
    }
}