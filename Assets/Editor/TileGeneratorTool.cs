using UnityEngine;
using UnityEditor;
using DDD.TNFY.BRAWL;

public class TileGeneratorTool : EditorWindow
{
    [Header("Tile Generation Settings")]
    private GameObject tilePrefab;
    private Tile cornerTile1;
    private Tile cornerTile2;
    private MapConfiguration mapConfiguration;
    private bool alignToGrid = true;
    private Transform parentTransform;

    [Header("Gap Generation Settings")]
    private float gapProbability = 0.2f;
    private float noiseScale = 0.1f;
    private bool useNoise = true;


    private float tileSpacing = 1.5f;
    private int targetYLevel = 0;

    [MenuItem("Tools/TNFY Brawl/Tile Generator")]
    public static void ShowWindow()
    {
        GetWindow<TileGeneratorTool>("Tile Generator");
    }

    private void OnGUI()
    {
        GUILayout.Label("Tile Square Generator", EditorStyles.boldLabel);
        GUILayout.Space(10);

        mapConfiguration = (MapConfiguration)EditorGUILayout.ObjectField(
            "Map Configuration",
            mapConfiguration,
            typeof(MapConfiguration),
            false
        );

        tilePrefab = (GameObject)EditorGUILayout.ObjectField(
            "Tile Prefab",
            tilePrefab,
            typeof(GameObject),
            false
        );

        GUILayout.Space(10);

        GUILayout.Label("Define Square Corners", EditorStyles.boldLabel);
        cornerTile1 = (Tile)EditorGUILayout.ObjectField(
            "Corner Tile 1",
            cornerTile1,
            typeof(Tile),
            true
        );

        cornerTile2 = (Tile)EditorGUILayout.ObjectField(
            "Corner Tile 2",
            cornerTile2,
            typeof(Tile),
            true
        );

        GUILayout.Space(10);

        GUILayout.Label("Y Level Settings", EditorStyles.boldLabel);
        targetYLevel = EditorGUILayout.IntField("Target Y Level", targetYLevel);

        if (mapConfiguration != null)
        {
            float calculatedY = targetYLevel * mapConfiguration.tileSpacing.y;
            EditorGUILayout.LabelField("Calculated Y Position", calculatedY.ToString("F2"));
        }

        GUILayout.Space(10);

        GUILayout.Label("Generation Settings", EditorStyles.boldLabel);

        if (mapConfiguration != null)
        {
            EditorGUILayout.LabelField("Tile Spacing (from MapConfig)",
                $"X: {mapConfiguration.tileSpacing.x:F2}, Z: {mapConfiguration.tileSpacing.z:F2}");

            tileSpacing = mapConfiguration.tileSpacing.x; 
        }
        else
        {
            tileSpacing = EditorGUILayout.FloatField("Manual Tile Spacing", tileSpacing);
            EditorGUILayout.HelpBox("Assign a Map Configuration to use configured tile spacing.", MessageType.Info);
        }

        alignToGrid = EditorGUILayout.Toggle("Align to Grid", alignToGrid);

        parentTransform = (Transform)EditorGUILayout.ObjectField(
            "Parent Transform",
            parentTransform,
            typeof(Transform),
            true
        );

        GUILayout.Space(20);

        bool canGenerate = ValidateInputs();

        if (!canGenerate)
        {
            EditorGUILayout.HelpBox(GetValidationMessage(), MessageType.Warning);
        }

        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            if (GUILayout.Button("Generate Tile Square", GUILayout.Height(30)))
            {
                GenerateTileSquare();
            }

            GUILayout.Space(5);

            if (GUILayout.Button("Preview Only (No Generation)", GUILayout.Height(25)))
            {
                PreviewTilePositions();
            }
        }

        GUILayout.Space(10);

        GUILayout.Label("Quick Actions", EditorStyles.boldLabel);

        if (GUILayout.Button("Select All Tiles in Scene"))
        {
            SelectAllTiles();
        }

        if (GUILayout.Button("Clear Corner Selections"))
        {
            cornerTile1 = null;
            cornerTile2 = null;
        }
    }

    private bool ValidateInputs()
    {
        if (tilePrefab == null) return false;
        if (cornerTile1 == null || cornerTile2 == null) return false;
        if (cornerTile1 == cornerTile2) return false;

        float effectiveSpacing = GetEffectiveSpacing();
        if (effectiveSpacing <= 0) return false;

        var tileComponent = tilePrefab.GetComponent<Tile>();
        if (tileComponent == null) return false;

        return true;
    }

    private string GetValidationMessage()
    {
        if (tilePrefab == null) return "Please assign a Tile Prefab";
        if (tilePrefab.GetComponent<Tile>() == null) return "Prefab must have a Tile component";
        if (cornerTile1 == null || cornerTile2 == null) return "Please assign both corner tiles";
        if (cornerTile1 == cornerTile2) return "Corner tiles must be different";

        float effectiveSpacing = GetEffectiveSpacing();
        if (effectiveSpacing <= 0) return "Tile spacing must be greater than 0";

        return "Ready to generate";
    }

    private void GenerateTileSquare()
    {
        Vector3 pos1 = cornerTile1.transform.position;
        Vector3 pos2 = cornerTile2.transform.position;

        float minX = Mathf.Min(pos1.x, pos2.x);
        float maxX = Mathf.Max(pos1.x, pos2.x);
        float minZ = Mathf.Min(pos1.z, pos2.z);
        float maxZ = Mathf.Max(pos1.z, pos2.z);

        float yPos;
        if (mapConfiguration != null)
        {
            yPos = targetYLevel * mapConfiguration.tileSpacing.y;
        }
        else
        {
            yPos = pos1.y; 
        }

        float effectiveSpacingX = mapConfiguration != null ? mapConfiguration.tileSpacing.x : tileSpacing;
        float effectiveSpacingZ = mapConfiguration != null ? mapConfiguration.tileSpacing.z : tileSpacing;

        int tilesX = Mathf.RoundToInt((maxX - minX) / effectiveSpacingX) + 1;
        int tilesZ = Mathf.RoundToInt((maxZ - minZ) / effectiveSpacingZ) + 1;

        GameObject[] createdTiles = new GameObject[tilesX * tilesZ];
        int tileIndex = 0;

        GameObject tileContainer = null;
        if (parentTransform == null)
        {
            tileContainer = new GameObject($"Generated Tiles Level {targetYLevel} ({tilesX}x{tilesZ})");
            parentTransform = tileContainer.transform;
        }

        for (int x = 0; x < tilesX; x++)
        {
            for (int z = 0; z < tilesZ; z++)
            {
                Vector3 tilePosition = new Vector3(
                    minX + (x * effectiveSpacingX),
                    yPos,
                    minZ + (z * effectiveSpacingZ)
                );

                if (alignToGrid)
                {
                    tilePosition.x = Mathf.Round(tilePosition.x / effectiveSpacingX) * effectiveSpacingX;
                    tilePosition.z = Mathf.Round(tilePosition.z / effectiveSpacingZ) * effectiveSpacingZ;
                }

                if (TileExistsAtPosition(tilePosition, 0.1f))
                {
                    Debug.Log($"Skipping tile at {tilePosition} - tile already exists");
                    continue;
                }

                GameObject newTile = (GameObject)PrefabUtility.InstantiatePrefab(tilePrefab);
                newTile.transform.position = tilePosition;
                newTile.transform.parent = parentTransform;
                newTile.name = $"Tile_L{targetYLevel}_{x}_{z}";

                var tileComponent = newTile.GetComponent<Tile>();
                if (tileComponent != null)
                {
                    tileComponent.gridPosition = new Vector2Int(x, z);
                }

                createdTiles[tileIndex++] = newTile;
            }
        }

        Undo.RegisterCreatedObjectUndo(parentTransform.gameObject, "Generate Tile Square");

        Selection.objects = createdTiles;

        Debug.Log($"Generated {tileIndex} tiles in a {tilesX}x{tilesZ} grid at Y level {targetYLevel} (Y pos: {yPos})");

        var gridManager = FindAnyObjectByType<GridManager>();
        if (gridManager != null)
        {
            gridManager.BuildTileList();
            Debug.Log("GridManager tile list updated");
        }
    }

    private void GenerateTileSquareWithGaps()
    {
        Vector3 pos1 = cornerTile1.transform.position;
        Vector3 pos2 = cornerTile2.transform.position;
        
        float minX = Mathf.Min(pos1.x, pos2.x);
        float maxX = Mathf.Max(pos1.x, pos2.x);
        float minZ = Mathf.Min(pos1.z, pos2.z);
        float maxZ = Mathf.Max(pos1.z, pos2.z);
        
        float yPos;
        if (mapConfiguration != null)
        {
            yPos = targetYLevel * mapConfiguration.tileSpacing.y;
        }
        else
        {
            yPos = pos1.y;
        }

        float effectiveSpacingX = mapConfiguration != null ? mapConfiguration.tileSpacing.x : tileSpacing;
        float effectiveSpacingZ = mapConfiguration != null ? mapConfiguration.tileSpacing.z : tileSpacing;

        int tilesX = Mathf.RoundToInt((maxX - minX) / effectiveSpacingX) + 1;
        int tilesZ = Mathf.RoundToInt((maxZ - minZ) / effectiveSpacingZ) + 1;

        float noiseOffsetX = Random.Range(-1000f, 1000f);
        float noiseOffsetZ = Random.Range(-1000f, 1000f);

        var createdTilesList = new System.Collections.Generic.List<GameObject>();

        GameObject tileContainer = null;
        if (parentTransform == null)
        {
            tileContainer = new GameObject($"Generated Tiles with Gaps Level {targetYLevel} ({tilesX}x{tilesZ})");
            parentTransform = tileContainer.transform;
        }

        int tilesCreated = 0;
        int tilesSkippedGaps = 0;
        int tilesSkippedExisting = 0;

        for (int x = 0; x < tilesX; x++)
        {
            for (int z = 0; z < tilesZ; z++)
            {
                Vector3 tilePosition = new Vector3(
                    minX + (x * effectiveSpacingX),
                    yPos,
                    minZ + (z * effectiveSpacingZ)
                );

                if (alignToGrid)
                {
                    tilePosition.x = Mathf.Round(tilePosition.x / effectiveSpacingX) * effectiveSpacingX;
                    tilePosition.z = Mathf.Round(tilePosition.z / effectiveSpacingZ) * effectiveSpacingZ;
                }

                if (TileExistsAtPosition(tilePosition, 0.1f))
                {
                    tilesSkippedExisting++;
                    continue;
                }

                bool shouldCreateGap = false;

                if (useNoise)
                {
                    float noiseValue = Mathf.PerlinNoise(
                        (x * noiseScale) + noiseOffsetX,
                        (z * noiseScale) + noiseOffsetZ
                    );
                    
                    shouldCreateGap = noiseValue < gapProbability;
                }
                else
                {
                    shouldCreateGap = Random.value < gapProbability;
                }

                bool isCorner = (x == 0 && z == 0) ||
                               (x == 0 && z == tilesZ - 1) ||
                               (x == tilesX - 1 && z == 0) ||
                               (x == tilesX - 1 && z == tilesZ - 1);

                if (shouldCreateGap && !isCorner)
                {
                    tilesSkippedGaps++;
                    continue;
                }

                GameObject newTile = (GameObject)PrefabUtility.InstantiatePrefab(tilePrefab);
                newTile.transform.position = tilePosition;
                newTile.transform.parent = parentTransform;
                newTile.name = $"Tile_L{targetYLevel}_{x}_{z}";

                var tileComponent = newTile.GetComponent<Tile>();
                if (tileComponent != null)
                {
                    tileComponent.gridPosition = new Vector2Int(x, z);
                }

                createdTilesList.Add(newTile);
                tilesCreated++;
            }
        }

        if (tileContainer != null)
        {
            Undo.RegisterCreatedObjectUndo(tileContainer, "Generate Tile Square with Gaps");
        }
        else
        {
            foreach (var tile in createdTilesList)
            {
                Undo.RegisterCreatedObjectUndo(tile, "Generate Tile Square with Gaps");
            }
        }

        Selection.objects = createdTilesList.ToArray();

        string gapMethod = useNoise ? "Perlin noise" : "random probability";
        Debug.Log($"Generated {tilesCreated} tiles with gaps using {gapMethod} at Y level {targetYLevel}");
        Debug.Log($"Grid size: {tilesX}x{tilesZ}, Gaps created: {tilesSkippedGaps}, Existing tiles skipped: {tilesSkippedExisting}");

        var gridManager = FindAnyObjectByType<GridManager>();
        if (gridManager != null)
        {
            gridManager.BuildTileList();
            Debug.Log("GridManager tile list updated");
        }
    }

    private bool TileExistsAtPosition(Vector3 position, float tolerance)
    {
        Tile[] allTiles = FindObjectsByType<Tile>(FindObjectsSortMode.None);
        foreach (var tile in allTiles)
        {
            if (Vector3.Distance(tile.transform.position, position) < tolerance)
            {
                return true;
            }
        }
        return false;
    }

    private void PreviewTilePositions()
    {
        Vector3 pos1 = cornerTile1.transform.position;
        Vector3 pos2 = cornerTile2.transform.position;

        float minX = Mathf.Min(pos1.x, pos2.x);
        float maxX = Mathf.Max(pos1.x, pos2.x);
        float minZ = Mathf.Min(pos1.z, pos2.z);
        float maxZ = Mathf.Max(pos1.z, pos2.z);

        float effectiveSpacingX = mapConfiguration != null ? mapConfiguration.tileSpacing.x : tileSpacing;
        float effectiveSpacingZ = mapConfiguration != null ? mapConfiguration.tileSpacing.z : tileSpacing;

        int tilesX = Mathf.RoundToInt((maxX - minX) / effectiveSpacingX) + 1;
        int tilesZ = Mathf.RoundToInt((maxZ - minZ) / effectiveSpacingZ) + 1;

        int totalPossibleTiles = tilesX * tilesZ;
        int estimatedGaps = Mathf.RoundToInt(totalPossibleTiles * gapProbability);
        int estimatedTiles = totalPossibleTiles - estimatedGaps + 4;

        Debug.Log($"Complete generation would create {totalPossibleTiles} tiles in a {tilesX}x{tilesZ} grid");
        Debug.Log($"Gap generation would create approximately {estimatedTiles} tiles with ~{estimatedGaps} gaps");
        Debug.Log($"X range: {minX} to {maxX}");
        Debug.Log($"Z range: {minZ} to {maxZ}");
        Debug.Log($"Y level: {targetYLevel} (Y pos: {(mapConfiguration != null ? targetYLevel * mapConfiguration.tileSpacing.y : pos1.y)})");
    }

    private void SelectAllTiles()
    {
        Tile[] allTiles = FindObjectsByType<Tile>(FindObjectsSortMode.None);
        GameObject[] tileObjects = new GameObject[allTiles.Length];

        for (int i = 0; i < allTiles.Length; i++)
        {
            tileObjects[i] = allTiles[i].gameObject;
        }

        Selection.objects = tileObjects;
        Debug.Log($"Selected {allTiles.Length} tiles");
    }

    private float GetEffectiveSpacing()
    {
        return mapConfiguration != null ? mapConfiguration.tileSpacing.x : tileSpacing;
    }
}