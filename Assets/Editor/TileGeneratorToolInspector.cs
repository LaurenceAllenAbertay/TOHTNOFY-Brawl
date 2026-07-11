using UnityEngine;
using UnityEditor;
using DDD.TNFY.BRAWL;

public static class TileGeneratorToolInspector
{
    [MenuItem("GameObject/TNFY Brawl/Generate Tiles Between Selected", false, 10)]
    private static void GenerateTilesBetweenSelected()
    {
        if (Selection.gameObjects.Length != 2)
        {
            EditorUtility.DisplayDialog("Invalid Selection",
                "Please select exactly 2 Tile objects to define the square corners.", "OK");
            return;
        }

        Tile tile1 = Selection.gameObjects[0].GetComponent<Tile>();
        Tile tile2 = Selection.gameObjects[1].GetComponent<Tile>();

        if (tile1 == null || tile2 == null)
        {
            EditorUtility.DisplayDialog("Invalid Selection",
                "Both selected objects must have Tile components.", "OK");
            return;
        }

        var window = EditorWindow.GetWindow<TileGeneratorTool>("Tile Generator");

        var type = typeof(TileGeneratorTool);
        var cornerTile1Field = type.GetField("cornerTile1",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var cornerTile2Field = type.GetField("cornerTile2",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (cornerTile1Field != null && cornerTile2Field != null)
        {
            cornerTile1Field.SetValue(window, tile1);
            cornerTile2Field.SetValue(window, tile2);
        }

        window.Show();
    }

    [MenuItem("GameObject/TNFY Brawl/Generate Tiles Between Selected", true)]
    private static bool ValidateGenerateTilesBetweenSelected()
    {
        return Selection.gameObjects.Length == 2;
    }
}