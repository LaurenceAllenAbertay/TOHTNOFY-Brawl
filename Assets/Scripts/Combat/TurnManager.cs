using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public class TurnManager : MonoBehaviour
    {
        public static TurnManager Instance { get; private set; }

        public static event System.Action<Unit> OnTurnStarted; 
        public static event System.Action<Unit> OnTurnEnded; 
        public static event System.Action<int, int> OnTurnNumberChanged;
        public static event System.Action<int> OnTotalTurnChanged;

        private List<Unit> turnOrder = new List<Unit>();
        private int currentIndex = 0;
        [SerializeField] private int totalTurnCount = 0;
        public TextMeshProUGUI turnOrderText;
        
        private CombatManager combatManager;
        private CameraController cameraController;
        
        public Unit CurrentUnit => turnOrder.Count > 0 ? turnOrder[currentIndex] : null;
        public List<Unit> TurnOrder => turnOrder;
        public int CurrentTurnIndex => currentIndex;
        public int TotalTurnCount => totalTurnCount;
        
        private int _stableRoundCount = 0;
        
        private bool _activeUnitDiedThisTurn = false;

        private int _roundNumber = 1;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void Start()
        {
            combatManager = FindAnyObjectByType<CombatManager>();
            cameraController = FindAnyObjectByType<CameraController>();
            BuildTurnOrder();
            StartNextTurn();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void OnEnable()
        {
            UnitManager.OnUnitDied += HandleUnitDied;
        }

        void OnDisable()
        {
            UnitManager.OnUnitDied -= HandleUnitDied;
        }
        
        private void HandleUnitDied(Unit unit)
        {
            int diedIndex = turnOrder.IndexOf(unit);
            if (diedIndex < 0) return;
            
            if (diedIndex == currentIndex)
                _activeUnitDiedThisTurn = true;

            turnOrder.RemoveAt(diedIndex);
            
            if (diedIndex <= currentIndex)
                currentIndex = Mathf.Max(0, currentIndex - 1);

            if (turnOrder.Count > 0)
                currentIndex = Mathf.Clamp(currentIndex, 0, turnOrder.Count - 1);
        }

        public bool AddUnitToTurnOrder(Unit unit)
        {
            if (unit == null) return false;
            if (!(unit is PlayerUnit || unit is NpcUnit)) return false;
            if (turnOrder.Contains(unit)) return false;

            turnOrder.Add(unit);

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);
            return true;
        }

        void BuildTurnOrder()
        {
            var validUnits = UnitManager.AllUnits.Where(u => u is PlayerUnit || u is NpcUnit);

            var rolled = validUnits.Select(u => new
            {
                unit = u,
                initiative = Random.Range(0, u.currentSpeed + 1)
            });
            
            var sorted = rolled
                .OrderByDescending(x => x.initiative)
                .ThenBy(x => Random.value)
                .Select(x => x.unit)
                .ToList();
            
            turnOrder = sorted.Where(u => u.goesFirst)
                .Concat(sorted.Where(u => !u.goesFirst))
                .ToList();

            if (turnOrderText == null) return;
            turnOrderText.text = "Turn Order:";
            foreach (var unit in turnOrder)
            {
                turnOrderText.text += $"\n- {unit.gameObject.name}";
            }
        }

        void StartNextTurn()
        {
            if (turnOrder.Count == 0)
            {
                Debug.LogWarning("TurnManager: No units in turn order!");
                return;
            }
            
            totalTurnCount++;
            
            _activeUnitDiedThisTurn = false;

            Unit current = CurrentUnit;
            
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Stunned))
            {
                StartCoroutine(HandleStunnedTurn(current));
                return;
            }

            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Dizzy))
            {
                StartCoroutine(HandleDizzyTurn(current));
                return;
            }

            current.StartTurn();
            
            OnTurnStarted?.Invoke(current);
            OnTotalTurnChanged?.Invoke(totalTurnCount);
        }

        public Coroutine EndTurn()
        {
            if (_activeUnitDiedThisTurn)
            {
                Debug.Log("[TurnManager] Active unit died during their own turn — skipping EndTurn calls and advancing sequence.");
                return StartCoroutine(EndTurnSequence());
            }

            Unit currentUnit = CurrentUnit;
            
            if (currentUnit is PlayerUnit)
            {
                UIEvents.OnTurnChanged();
            }

            currentUnit?.EndTurn();
            
            OnTurnEnded?.Invoke(currentUnit);

            return StartCoroutine(EndTurnSequence());
        }

        private IEnumerator HandleStunnedTurn(Unit unit)
        {
            Debug.Log($"[TurnManager] {unit.name} is stunned - skipping turn");

            unit.TickAbilityCooldowns();
            
            if (cameraController != null)
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));

            yield return new WaitForSeconds(3f);
            
            OnTurnEnded?.Invoke(unit);
            StartCoroutine(EndTurnSequence());
        }

        private IEnumerator HandleDizzyTurn(Unit unit)
        {
            Debug.Log($"[TurnManager] {unit.name} is dizzy — forcing random movement.");
            
            if (cameraController != null)
                yield return StartCoroutine(cameraController.TransitionTo(
                    cameraController.UnitFocusPosition(unit)));
            
            yield return new WaitForSeconds(1f);
            
            unit.StartTurn();
            OnTurnStarted?.Invoke(unit);
            
            if (unit.CanMove() && unit.currentTile != null && UnitMovementController.Instance != null)
            {
                var reachableTiles = GridManager.Instance.GetReachableTiles(
                    unit.currentTile, unit.GetEffectiveMovementRange());
                
                reachableTiles.Remove(unit.currentTile);

                if (reachableTiles.Count > 0)
                {
                    Tile randomDest = reachableTiles[Random.Range(0, reachableTiles.Count)];
                    Debug.Log($"[Dizzy] {unit.name} stumbles to {randomDest.name}.");
                    yield return StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                        unit, randomDest, followCameraForAI: true));
                    
                    if (UnitDownedSequencer.Instance != null)
                        yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue(unit));
                }
                else
                {
                    Debug.Log($"[Dizzy] {unit.name} has nowhere to stumble.");
                }
            }
            
            if (_activeUnitDiedThisTurn)
            {
                StartCoroutine(EndTurnSequence());
                yield break;
            }

            unit.EndTurn();
            OnTurnEnded?.Invoke(unit);
            StartCoroutine(EndTurnSequence());
        }

        private IEnumerator EndTurnSequence()
        {
            currentIndex++;
            if (currentIndex >= turnOrder.Count)
            {
                currentIndex = 0;
                _roundNumber++;
                
                _stableRoundCount = turnOrder.Count;
                yield return StartCoroutine(TriggerEnvironmentEffects());
                _stableRoundCount = 0; 
            }

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);
            
            if (turnOrder.Count == 0) yield break;

            StartNextTurn();
        }

        private IEnumerator TriggerEnvironmentEffects()
        {
            if (GridManager.Instance == null) yield break;

            int currentRound = GetCurrentRound();
            
            foreach (var tile in GridManager.Instance.AllTiles)
            {
                if (tile.HasActiveEffects)
                    yield return StartCoroutine(tile.TriggerEffects(currentRound));
            }

            if (UnitDownedSequencer.Instance != null)
                yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue(returnToUnit: null));
            
            var neutralSnapshot = new List<NeutralUnit>(UnitManager.AllNeutralUnits);
            foreach (var neutral in neutralSnapshot)
            {
                if (neutral == null || neutral.IsDead) continue;
                if (neutral.environmentAbility == null) continue;
                
                if (cameraController != null)
                    yield return StartCoroutine(cameraController.TransitionTo(
                        cameraController.UnitFocusPosition(neutral)));

                var ctx = new AbilityContext
                {
                    caster = neutral,
                    ability = neutral.environmentAbility,
                    aimDir  = neutral.currentFacing
                };

                yield return StartCoroutine(neutral.ExecuteAbilityCoroutine(ctx));
                
                if (UnitDownedSequencer.Instance != null)
                    yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue(returnToUnit: null));
            }
            
            if (UnitDownedSequencer.Instance != null)
                yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue(returnToUnit: null));
        }
        
        public void ResetTotalTurnCount()
        {
            totalTurnCount = 0;
        }

        public int GetCurrentRound()
        {
            return _roundNumber;
        }

        public bool AdminForceSkipToUnit(Unit targetUnit)
        {
            if (targetUnit == null) return false;

            int targetIndex = turnOrder.IndexOf(targetUnit);
            if (targetIndex < 0) return false;

            bool willWrap = targetIndex <= currentIndex;

            currentIndex = targetIndex;
            if (willWrap)
                _roundNumber++;

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);

            StartNextTurn();
            return true;
        }

        public int GetTurnInCurrentRound()
        {
            int count = _stableRoundCount > 0 ? _stableRoundCount : turnOrder.Count;
            if (count == 0) return 0;
            return ((totalTurnCount - 1) % count) + 1;
        }
    }
}