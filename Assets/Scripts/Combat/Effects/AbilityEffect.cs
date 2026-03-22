using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public abstract class AbilityEffect : ScriptableObject
    {
        public abstract void Apply(AbilityContext ctx, IReadOnlyList<Unit> targets);
    }
}