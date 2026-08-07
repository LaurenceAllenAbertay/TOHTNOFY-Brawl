using System.Collections.Generic;
using UnityEngine;

namespace DDD.TNFY.BRAWL
{
    public static class DebugSessionConfig
    {
        public static readonly List<PlayerSpawnConfig> PlayerSpawns = new List<PlayerSpawnConfig>();
        
        public static readonly List<EnemySpawnConfig> EnemySpawns = new List<EnemySpawnConfig>();

        public static void Clear()
        {
            PlayerSpawns.Clear();
            EnemySpawns.Clear();
        }
    }

    [System.Serializable]
    public class PlayerSpawnConfig
    {
        public CharacterData characterData;
    }
    
    [System.Serializable]
    public class EnemySpawnConfig
    {
        public CharacterData characterData;
    }
}