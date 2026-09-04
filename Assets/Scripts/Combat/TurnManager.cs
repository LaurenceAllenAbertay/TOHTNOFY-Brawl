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
        public static event System.Action OnCombatEmpty;

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

        private Unit _activeUnit;

        private int _roundNumber = 1;

        private bool _combatStarted = false;

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
            BeginCombat();
        }

        public void BeginCombat()
        {
            if (_combatStarted) return;

            BuildTurnOrder();

            if (turnOrder.Count == 0)
            {
                Debug.Log("[TurnManager] No units present yet — waiting for the spawner to call BeginCombat().");
                return;
            }

            _combatStarted = true;
            currentIndex = 0;
            StartCoroutine(StartNextTurn());
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

            if (unit == _activeUnit)
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

            bool wasEmpty = _activeUnit == null;

            turnOrder.Add(unit);

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);

            if (wasEmpty)
            {
                currentIndex = turnOrder.Count - 1;
                StartCoroutine(StartNextTurn());
            }

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

        IEnumerator StartNextTurn()
        {
            if (turnOrder.Count == 0)
            {
                Debug.LogWarning("TurnManager: No units in turn order!");
                _activeUnit = null;
                OnCombatEmpty?.Invoke();
                yield break;
            }
            
            totalTurnCount++;
            
            _activeUnitDiedThisTurn = false;

            Unit current = CurrentUnit;
            _activeUnit = current;
            
            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Stunned))
            {
                StartCoroutine(HandleStunnedTurn(current));
                yield break;
            }

            if (StatusEffectManager.Instance != null &&
                StatusEffectManager.Instance.HasStatusEffect(current, StatusEffectType.Dizzy))
            {
                StartCoroutine(HandleDizzyTurn(current));
                yield break;
            }

            current.StartTurn();
            
            OnTurnStarted?.Invoke(current);
            OnTotalTurnChanged?.Invoke(totalTurnCount);

            if (BigMomentSequencer.Instance != null)
                yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());
        }

        public Coroutine EndTurn()
        {
            if (_activeUnitDiedThisTurn)
            {
                Debug.Log("[TurnManager] Active unit died during their own turn — skipping EndTurn calls and advancing sequence.");
                return StartCoroutine(EndTurnSequence());
            }

            Unit currentUnit = CurrentUnit;
            
            if (currentUnit != null && !currentUnit.IsAIControlled)
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
            {
                var transition = cameraController.SetIdleFocus(unit, 1f);
                if (transition != null) yield return transition;
            }

            yield return new WaitForSeconds(3f);
            
            OnTurnEnded?.Invoke(unit);
            StartCoroutine(EndTurnSequence());
        }

        private IEnumerator HandleDizzyTurn(Unit unit)
        {
            Debug.Log($"[TurnManager] {unit.name} is dizzy — forcing random movement.");
            
            if (cameraController != null)
            {
                var transition = cameraController.SetIdleFocus(unit, 1f);
                if (transition != null) yield return transition;
            }
            
            yield return new WaitForSeconds(1f);
            
            unit.StartTurn();
            OnTurnStarted?.Invoke(unit);

            if (BigMomentSequencer.Instance != null)
                yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());
            
            if (unit.CanMove() && unit.currentTile != null && UnitMovementController.Instance != null)
            {
                var reachableTiles = GridManager.Instance.GetReachableTiles(
                    unit.currentTile, unit.GetEffectiveMovementRange());
                
                reachableTiles.Remove(unit.currentTile);

                if (reachableTiles.Count > 0)
                {
                    Tile randomDest = reachableTiles[Random.Range(0, reachableTiles.Count)];
                    Debug.Log($"[Dizzy] {unit.name} stumbles to {randomDest.name}.");

                    int dizzyFocusHandle = -1;
                    if (cameraController != null)
                    {
                        Coroutine transition;
                        dizzyFocusHandle = cameraController.PushFollow(unit, 1f, out transition);
                        yield return transition;
                    }

                    yield return StartCoroutine(UnitMovementController.Instance.ExecuteAnimatedMovement(
                        unit, randomDest));

                    if (dizzyFocusHandle >= 0)
                        cameraController.PopFocus(dizzyFocusHandle);
                    
                    if (UnitDownedSequencer.Instance != null)
                        yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue());
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
            if (BigMomentSequencer.Instance != null)
                yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());

            if (turnOrder.Count == 0)
            {
                yield return StartCoroutine(StartNextTurn());
                yield break;
            }

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
            
            yield return StartCoroutine(StartNextTurn());
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

            if (BigMomentSequencer.Instance != null)
                yield return StartCoroutine(BigMomentSequencer.Instance.DrainQueue());

            if (UnitDownedSequencer.Instance != null)
                yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue());
            
            var neutralSnapshot = new List<NeutralUnit>(UnitManager.AllNeutralUnits);
            foreach (var neutral in neutralSnapshot)
            {
                if (neutral == null || neutral.IsDead) continue;
                if (neutral.environmentAbility == null) continue;

                int neutralFocusHandle = -1;
                if (cameraController != null)
                {
                    Coroutine transition;
                    neutralFocusHandle = cameraController.PushFocus(neutral, 1f, out transition);
                    yield return transition;
                }

                var ctx = new AbilityContext
                {
                    caster = neutral,
                    ability = neutral.environmentAbility,
                    aimDir  = neutral.currentFacing
                };

                yield return StartCoroutine(neutral.ExecuteAbilityCoroutine(ctx));

                if (neutralFocusHandle >= 0)
                    cameraController.PopFocus(neutralFocusHandle);
                
                if (UnitDownedSequencer.Instance != null)
                    yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue());
            }
            
            if (UnitDownedSequencer.Instance != null)
                yield return StartCoroutine(UnitDownedSequencer.Instance.DrainDownedQueue());
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

            StartCoroutine(StartNextTurn());
            return true;
        }

        public bool MoveUnitToNextInTurnOrder(Unit unit)
        {
            if (unit == null) return false;

            int fromIndex = turnOrder.IndexOf(unit);
            if (fromIndex < 0 || fromIndex == currentIndex) return false;

            turnOrder.RemoveAt(fromIndex);

            int activeIndex = fromIndex < currentIndex ? currentIndex - 1 : currentIndex;
            int insertIndex = Mathf.Clamp(activeIndex + 1, 0, turnOrder.Count);

            turnOrder.Insert(insertIndex, unit);
            currentIndex = activeIndex;

            OnTurnNumberChanged?.Invoke(totalTurnCount, currentIndex);
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