using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    [CreateAssetMenu(menuName = "TNFY Brawl/Effects/Spawn Neutral Unit")]
    public class SpawnNeutralUnitEffect : AbilityEffect
    {
        [Header("Prefab")]
        [SerializeField] private GameObject neutralUnitPrefab;

        [Header("Placement Rules")]
        [SerializeField] private bool allowOnlyOne = true;
        
        [SerializeField] private Vector3 spawnOffset = Vector3.zero;
        
        private NeutralUnit _activeInstance;
        
        public bool HasActiveInstance => _activeInstance != null;
        
        public override EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;
        
        public override bool RequiresEmptyTargetTile => true;

        public override void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets)
        {
            if (neutralUnitPrefab == null)
            {
                Debug.LogWarning("[SpawnNeutralUnitEffect] No prefab assigned — nothing will spawn.");
                return;
            }
            
            Tile spawnTile = ctx.targetTile ?? ctx.caster?.currentTile;
            if (spawnTile == null)
            {
                Debug.LogWarning("[SpawnNeutralUnitEffect] No valid target tile found in AbilityContext.");
                return;
            }
            
            if (spawnTile.currentUnit != null)
            {
                Debug.LogWarning($"[SpawnNeutralUnitEffect] Target tile '{spawnTile.name}' is occupied — spawn aborted.");
                return;
            }
            
            if (allowOnlyOne && _activeInstance != null)
            {
                RemoveActiveInstance();
            }

            Vector3 spawnPosition = spawnTile.transform.position + spawnOffset;
            GameObject go = Object.Instantiate(
                neutralUnitPrefab, spawnPosition, neutralUnitPrefab.transform.rotation);
            go.name = neutralUnitPrefab.name; 

            NeutralUnit spawned = go.GetComponent<NeutralUnit>();
            if (spawned == null)
            {
                Debug.LogError($"[SpawnNeutralUnitEffect] Prefab '{neutralUnitPrefab.name}' " +
                               $"has no NeutralUnit component — destroying.");
                Object.Destroy(go);
                return;
            }
            
            spawned.SetCurrentTile(spawnTile);
            
            spawned.spawnSource = this;
            
            UnitManager.RegisterUnit(spawned);

            _activeInstance = spawned;

            Debug.Log($"[SpawnNeutralUnitEffect] Spawned '{go.name}' on '{spawnTile.name}'.");
        }
        
        public void OnInstanceDestroyed(NeutralUnit instance)
        {
            if (_activeInstance == instance)
                _activeInstance = null;
        }
        
        private void RemoveActiveInstance()
        {
            if (_activeInstance == null) return;
            
            if (_activeInstance.currentTile != null &&
                _activeInstance.currentTile.currentUnit == _activeInstance)
            {
                _activeInstance.currentTile.currentUnit = null;
            }

            UnitManager.UnregisterUnit(_activeInstance);
            Object.Destroy(_activeInstance.gameObject);
        }
    }
}