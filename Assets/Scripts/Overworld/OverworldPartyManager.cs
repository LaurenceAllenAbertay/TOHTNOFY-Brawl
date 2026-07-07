using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class OverworldPartyManager : MonoBehaviour
    {
        [Header("Leader Movement")]
        [SerializeField] private float moveSpeed = 5f;

        [Header("Follower Movement")]
        [SerializeField] private float followDistance = 1.2f;

        [SerializeField] private float followSpeed = 7f;
        
        private readonly List<Transform>      _transforms = new List<Transform>();
        private readonly List<UnitAnimator>   _animators  = new List<UnitAnimator>();
        private readonly List<SpriteRenderer> _renderers  = new List<SpriteRenderer>();
        
        private Vector3[] _prevPositions = new Vector3[0];
        
        private Vector2 _moveInput;
        
        public Transform LeaderTransform => _transforms.Count > 0 ? _transforms[0] : null;

        public void Initialise(List<GameObject> partyObjects)
        {
            _transforms.Clear();
            _animators.Clear();
            _renderers.Clear();

            foreach (var go in partyObjects)
            {
                if (go == null) continue;
                _transforms.Add(go.transform);
                _animators.Add(go.GetComponentInChildren<UnitAnimator>());
                _renderers.Add(go.GetComponentInChildren<SpriteRenderer>());
            }

            _prevPositions = new Vector3[_transforms.Count];
            for (int i = 0; i < _transforms.Count; i++)
                _prevPositions[i] = _transforms[i].position;

            foreach (var anim in _animators)
                anim?.SetActiveTurn();
        }

        private void OnEnable()
        {
            OverworldInputHandler.OnMoveInput += HandleMoveInput;
        }

        private void OnDisable()
        {
            OverworldInputHandler.OnMoveInput -= HandleMoveInput;
            _moveInput = Vector2.zero;
        }

        private void Update()
        {
            if (_transforms.Count == 0) return;

            for (int i = 0; i < _transforms.Count; i++)
                _prevPositions[i] = _transforms[i].position;

            MoveLeader();
            MoveFollowers();
            UpdateAnimationsAndFacing();
        }

        private void HandleMoveInput(Vector2 input)
        {
            _moveInput = input.magnitude > 1f ? input.normalized : input;
        }
        
        private void MoveLeader()
        {
            if (_moveInput.sqrMagnitude < 0.01f) return;
            
            _transforms[0].position +=
                new Vector3(_moveInput.x, 0f, _moveInput.y) * (moveSpeed * Time.deltaTime);
        }

        private void MoveFollowers()
        {
            for (int i = 1; i < _transforms.Count; i++)
            {
                Transform self  = _transforms[i];
                Transform ahead = _transforms[i - 1];

                Vector3 toAhead  = ahead.position - self.position;
                float   distance = toAhead.magnitude;
                
                if (distance <= followDistance) continue;
                
                Vector3 targetPos = ahead.position - toAhead.normalized * followDistance;
                self.position = Vector3.MoveTowards(self.position, targetPos,
                                                    followSpeed * Time.deltaTime);
            }
        }
        
        private void UpdateAnimationsAndFacing()
        {
            for (int i = 0; i < _transforms.Count; i++)
            {
                Vector3 delta    = _transforms[i].position - _prevPositions[i];
                bool    isMoving = delta.sqrMagnitude > 1e-6f;

                if (isMoving)
                    _animators[i]?.PlayMove();
                else
                    _animators[i]?.PlayIdle();

                if (_renderers[i] != null && isMoving && Mathf.Abs(delta.x) > 0.001f)
                    _renderers[i].flipX = delta.x > 0f;
            }
        }
    }
}