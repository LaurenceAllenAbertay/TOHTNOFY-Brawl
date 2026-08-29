using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public enum EffectAnimationPhase
    {
        PreEffect, 
        Displacement, 
        Damage, 
        StatusBuff,   
        StatusDebuff,  
        PostEffect      
    }

    public abstract class AbilityEffect : ScriptableObject
    {
        public virtual EffectAnimationPhase AnimationPhase => EffectAnimationPhase.PostEffect;
        
        public virtual string TargetAnimationHint => null;
        
        public virtual float ExpectedAnimationDuration => 0f;
        
        public virtual bool RequiresEmptyTargetTile => false;
        
        public virtual bool IsSelfOnly(AbilityContext ctx) => false;
        
        public virtual bool IsBuffEffect => false;
        
        public virtual string SelfCastAnimationHint => null;
        
        public virtual bool NeedsCameraPreview(AbilityContext ctx, out Tile focusTile)
        {
            focusTile = null;
            return false;
        }

        public int midAnimationEventIndex = -1;

        public abstract void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets);
    }
}