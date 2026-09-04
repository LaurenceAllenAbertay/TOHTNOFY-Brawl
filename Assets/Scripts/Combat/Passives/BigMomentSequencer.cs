using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class BigMomentSequencer : MonoBehaviour
    {
        public static BigMomentSequencer Instance { get; private set; }

        [Header("Timing")]
        [SerializeField] private float cameraTransitionDuration = 0.8f;

        [SerializeField] private float bigMomentEventMaxWait = 5f;

        [SerializeField] private float lingerAfterBigMomentSeconds = 0.5f;

        private readonly Queue<PendingBigMoment> _queue = new Queue<PendingBigMoment>();
        private bool _isDraining = false;

        private CameraController _camera;

        private struct PendingBigMoment
        {
            public Unit victim;
            public StatusEffectData attackUpData;
            public float attackPower;
            public int buffDuration;
            public float restoreHealthFraction;
        }

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

        public bool HasPending => _queue.Count > 0 || _isDraining;

        public bool IsPending(Unit unit)
        {
            if (unit == null) return false;

            foreach (var entry in _queue)
            {
                if (entry.victim == unit) return true;
            }

            return false;
        }

        public void Enqueue(Unit victim, StatusEffectData attackUpData,
            float attackPower, int buffDuration, float restoreHealthFraction)
        {
            if (victim == null) return;

            _queue.Enqueue(new PendingBigMoment
            {
                victim = victim,
                attackUpData = attackUpData,
                attackPower = attackPower,
                buffDuration = buffDuration,
                restoreHealthFraction = restoreHealthFraction
            });
        }

        public IEnumerator DrainQueue()
        {
            while (_isDraining)
                yield return null;

            if (_queue.Count == 0)
                yield break;

            _isDraining = true;

            while (_queue.Count > 0)
            {
                var entry = _queue.Dequeue();
                yield return StartCoroutine(PlayBigMoment(entry));
            }

            _isDraining = false;
        }

        private IEnumerator PlayBigMoment(PendingBigMoment entry)
        {
            var victim = entry.victim;
            if (victim == null) yield break;

            var unitAnimator = victim.GetComponentInChildren<UnitAnimator>();
            var healthBar    = victim.GetComponentInChildren<UnitHealthBarDisplay>();

            int focusHandle = -1;
            if (_camera != null)
            {
                Coroutine transition;
                focusHandle = _camera.PushFocus(victim, cameraTransitionDuration, out transition);
                if (transition != null) yield return transition;
            }

            healthBar?.TweenToFraction(0f, displayHealthOverride: 0);

            if (unitAnimator != null && unitAnimator.HasBigMomentAnimation)
            {
                unitAnimator.BeginBigMoment();

                yield return StartCoroutine(unitAnimator.WaitForBigMomentComplete(bigMomentEventMaxWait));

                unitAnimator.EndBigMoment();
            }
            else
            {
                Debug.LogWarning($"[BigMomentSequencer] {victim.name}'s Animator has no Big Moment state. " +
                                  "Add the state to the Animator Controller.");
            }

            if (healthBar != null)
                yield return StartCoroutine(healthBar.WaitForTweenComplete());

            int maxHealth = victim.maxHealth > 0 ? victim.maxHealth : 1;
            int restoredHealth = Mathf.Max(1, Mathf.RoundToInt(maxHealth * entry.restoreHealthFraction));
            victim.currentHealth = restoredHealth;
            Unit.NotifyHealthChanged(victim);

            if (entry.attackUpData != null && StatusEffectManager.Instance != null)
                StatusEffectManager.Instance.ApplyStatusEffect(
                    victim, entry.attackUpData, victim, entry.buffDuration, entry.attackPower);

            if (healthBar != null)
                yield return StartCoroutine(healthBar.WaitForTweenComplete());

            if (lingerAfterBigMomentSeconds > 0f)
                yield return new WaitForSeconds(lingerAfterBigMomentSeconds);

            if (focusHandle >= 0)
            {
                _camera.PopFocus(focusHandle, out var revealTransition);
                if (revealTransition != null) yield return revealTransition;
            }
        }
    }
}