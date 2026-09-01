using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class UnitDownedSequencer : MonoBehaviour
    {
        public static UnitDownedSequencer Instance { get; private set; }

        [Header("Timing")]
        [SerializeField] private float lingerAfterDownedSeconds = 0.5f;

        [SerializeField] private float cameraTransitionDuration = 0.8f;

        private readonly Queue<PendingDowned> _queue = new Queue<PendingDowned>();

        private readonly HashSet<Unit> _presentedInline = new HashSet<Unit>();
        private bool _isDraining = false;

        private CameraController _camera;

        private struct PendingDowned
        {
            public Unit victim;
            public Unit killer;
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

        public void EnqueueDowned(Unit victim, Unit killer)
        {
            if (victim == null) return;
            _queue.Enqueue(new PendingDowned { victim = victim, killer = killer });
        }

        public IEnumerator DrainDownedQueue()
        {
            while (_isDraining)
                yield return null;

            if (_queue.Count == 0)
                yield break;

            _isDraining = true;

            while (_queue.Count > 0)
            {
                var entry = _queue.Dequeue();
                
                if (_presentedInline.Contains(entry.victim))
                    continue;

                yield return StartCoroutine(PresentDowned(entry.victim));
            }

            _presentedInline.Clear();
            _isDraining = false;
        }

        public IEnumerator PresentDownedInline(Unit victim)
        {
            if (victim == null) yield break;
            
            _presentedInline.Add(victim);

            var unitAnimator = victim.GetComponent<UnitAnimator>();
            var healthBar    = victim.GetComponentInChildren<UnitHealthBarDisplay>();
            
            unitAnimator?.PlayDowned();

            Coroutine downedWait = StartCoroutine(WaitForDownedAnimation(victim));
            Coroutine tweenWait  = healthBar != null ? StartCoroutine(healthBar.WaitForTweenComplete()) : null;

            if (downedWait != null) yield return downedWait;
            if (tweenWait  != null) yield return tweenWait;

            healthBar?.HideImmediate();
            victim.GetComponentInChildren<StatusEffectIconDisplay>()?.HideImmediate();

            yield return new WaitForSeconds(lingerAfterDownedSeconds);

            bool shouldLeaveBody = victim is PlayerUnit ||
                                   (victim is NpcUnit npc && npc.leavesBodyOnDown);

            if (shouldLeaveBody)
                victim.BecomeBody();
            else
            {
                UnitManager.UnregisterUnit(victim);
                victim.gameObject.SetActive(false);
            }
        }

        private IEnumerator PresentDowned(Unit victim)
        {
            if (victim == null) yield break;

            int focusHandle = -1;
            if (_camera != null)
            {
                Coroutine transition;
                focusHandle = _camera.PushFocus(victim, cameraTransitionDuration, out transition);
                yield return transition;
            }
            
            var healthBar    = victim.GetComponentInChildren<UnitHealthBarDisplay>();
            var unitAnimator = victim.GetComponent<UnitAnimator>();

            unitAnimator?.PlayDowned();

            Coroutine downedWait = StartCoroutine(WaitForDownedAnimation(victim));
            Coroutine tweenWait  = healthBar != null ? StartCoroutine(healthBar.WaitForTweenComplete()) : null;

            if (downedWait != null) yield return downedWait;
            if (tweenWait  != null) yield return tweenWait;
            
            healthBar?.HideImmediate();
            victim.GetComponentInChildren<StatusEffectIconDisplay>()?.HideImmediate();

            yield return new WaitForSeconds(lingerAfterDownedSeconds);

            if (focusHandle >= 0)
                _camera.PopFocus(focusHandle);
            
            bool shouldLeaveBody = victim is PlayerUnit ||
                                   (victim is NpcUnit npc && npc.leavesBodyOnDown);

            if (shouldLeaveBody)
            {
                victim.BecomeBody();
            }
            else
            {
                UnitManager.UnregisterUnit(victim);
                victim.gameObject.SetActive(false);
            }
        }
        
        private IEnumerator WaitForDownedAnimation(Unit victim)
        {
            var animator = victim.GetComponent<Animator>();
            if (animator == null) yield break;
            
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
            
            Debug.LogWarning($"[UnitDownedSequencer] Downed animation timed out for {victim.name}.");
        }
        
        public bool IsDraining => _isDraining;
        
        public bool HasPendingDowned => _queue.Count > 0;
    }
}