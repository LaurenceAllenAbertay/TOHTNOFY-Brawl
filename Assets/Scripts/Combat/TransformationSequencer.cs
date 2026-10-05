using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class TransformationSequencer : MonoBehaviour
    {
        public static TransformationSequencer Instance { get; private set; }

        [Header("Timing")]
        [SerializeField] private float cameraTransitionDuration = 0.8f;

        [SerializeField] private float transformAnimationMaxWait = 5f;

        [SerializeField] private float lingerAfterTransformSeconds = 0.5f;

        private readonly Queue<PendingTransformation> _queue = new Queue<PendingTransformation>();
        private bool _isDraining = false;

        private CameraController _camera;

        private struct PendingTransformation
        {
            public Unit unit;
            public CharacterData newForm;
            public RuntimeAnimatorController newController;
            public string transformState;
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

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public bool HasPending => _queue.Count > 0 || _isDraining;

        public bool IsPending(Unit unit)
        {
            if (unit == null) return false;

            foreach (var entry in _queue)
            {
                if (entry.unit == unit) return true;
            }

            return false;
        }

        public void Enqueue(Unit unit, CharacterData newForm,
            RuntimeAnimatorController newController, string transformState)
        {
            if (unit == null || newForm == null) return;
            if (IsPending(unit)) return;

            _queue.Enqueue(new PendingTransformation
            {
                unit = unit,
                newForm = newForm,
                newController = newController,
                transformState = transformState
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
                yield return StartCoroutine(PlayTransformation(entry));
            }

            _isDraining = false;
        }

        private IEnumerator PlayTransformation(PendingTransformation entry)
        {
            var unit = entry.unit;
            if (unit == null || unit.IsDead || unit.IsBody) yield break;

            var unitAnimator = unit.GetComponent<UnitAnimator>();

            int focusHandle = -1;
            if (_camera != null)
            {
                Coroutine transition;
                focusHandle = _camera.PushFocus(unit, cameraTransitionDuration, out transition);
                if (transition != null) yield return transition;
            }

            bool hasTransformState = unitAnimator != null
                                     && !string.IsNullOrEmpty(entry.transformState)
                                     && unitAnimator.HasState(entry.transformState);

            if (hasTransformState)
            {
                unitAnimator.ForcePlayAnimation(entry.transformState);

                yield return StartCoroutine(unitAnimator.WaitForAnimationToComplete(
                    entry.transformState, transformAnimationMaxWait));
            }
            else if (unitAnimator != null)
            {
                Debug.LogWarning($"[TransformationSequencer] '{entry.transformState}' is not a state on " +
                                 $"{unit.name}'s Animator Controller — transforming without an animation. " +
                                 "Add the state to the Animator Controller.");
            }

            unit.TransformInto(entry.newForm);

            if (entry.newController != null)
                unitAnimator?.SetRuntimeController(entry.newController);

            UIEvents.OnUnitHealthChanged();
            UIEvents.OnActiveUnitChanged();

            if (lingerAfterTransformSeconds > 0f)
                yield return new WaitForSeconds(lingerAfterTransformSeconds);

            if (focusHandle >= 0)
            {
                _camera.PopFocus(focusHandle, out var revealTransition);
                if (revealTransition != null) yield return revealTransition;
            }
        }
    }
}