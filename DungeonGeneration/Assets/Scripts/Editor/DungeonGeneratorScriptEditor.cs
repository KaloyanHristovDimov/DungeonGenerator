using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DungeonGeneratorScript))]
public class DungeonGeneratorScriptEditor : Editor
{
    private bool showGenerationSettings;
    private bool showPrefabs;
    private bool showOptional;

    private SerializedProperty minRoomSize;
    private SerializedProperty startRoomParams;
    private SerializedProperty randomSizeMin;
    private SerializedProperty randomSizeMax;
    private SerializedProperty generationsBeforePreservedRooms;
    private SerializedProperty preservedRoomChance;
    private SerializedProperty deleteRoomPercentage;

    private SerializedProperty floorPrefab;
    private SerializedProperty playerPrefab;
    private SerializedProperty cameraPrefab;
    private SerializedProperty navMesh;
    private SerializedProperty uiPrefab;
    private SerializedProperty eventSystem;
    private SerializedProperty wallPrefabs;

    private SerializedProperty generationType;
    private SerializedProperty executeNextStepKey;
    private SerializedProperty immediateStart;
    private SerializedProperty useRandomSeed;
    private SerializedProperty seed;

    private SerializedProperty objectsToKeepByName;

    private void OnEnable()
    {
        minRoomSize = serializedObject.FindProperty("minRoomSize");
        startRoomParams = serializedObject.FindProperty("startRoomParams");
        randomSizeMin = serializedObject.FindProperty("randomSizeMin");
        randomSizeMax = serializedObject.FindProperty("randomSizeMax");
        generationsBeforePreservedRooms = serializedObject.FindProperty("generationsBeforePreservedRooms");
        preservedRoomChance = serializedObject.FindProperty("preservedRoomChance");
        deleteRoomPercentage = serializedObject.FindProperty("deleteRoomPercentage");

        floorPrefab = serializedObject.FindProperty("floorPrefab");
        playerPrefab = serializedObject.FindProperty("playerPrefab");
        cameraPrefab = serializedObject.FindProperty("cameraPrefab");
        navMesh = serializedObject.FindProperty("navMesh");
        uiPrefab = serializedObject.FindProperty("uiPrefab");
        eventSystem = serializedObject.FindProperty("eventSystem");
        wallPrefabs = serializedObject.FindProperty("wallPrefabs");

        generationType = serializedObject.FindProperty("generationType");
        executeNextStepKey = serializedObject.FindProperty("executeNextStepKey");
        immediateStart = serializedObject.FindProperty("immediateStart");
        useRandomSeed = serializedObject.FindProperty("useRandomSeed");
        seed = serializedObject.FindProperty("seed");

        objectsToKeepByName = serializedObject.FindProperty("objectsToKeepByName");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        showGenerationSettings = EditorGUILayout.Foldout(
            showGenerationSettings,
            "Generation Settings",
            true
        );

        if (showGenerationSettings)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(minRoomSize);
            EditorGUILayout.PropertyField(startRoomParams);
            EditorGUILayout.PropertyField(randomSizeMin);
            EditorGUILayout.PropertyField(randomSizeMax);
            EditorGUILayout.PropertyField(generationsBeforePreservedRooms);
            EditorGUILayout.PropertyField(preservedRoomChance);
            EditorGUILayout.PropertyField(deleteRoomPercentage);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        showPrefabs = EditorGUILayout.Foldout(
            showPrefabs,
            "Prefabs",
            true
        );

        if (showPrefabs)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(floorPrefab);
            EditorGUILayout.PropertyField(playerPrefab);
            EditorGUILayout.PropertyField(cameraPrefab);
            EditorGUILayout.PropertyField(navMesh);
            EditorGUILayout.PropertyField(uiPrefab);
            EditorGUILayout.PropertyField(eventSystem);
            EditorGUILayout.PropertyField(wallPrefabs);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        showOptional = EditorGUILayout.Foldout(
            showOptional,
            "Optional",
            true
        );

        if (showOptional)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(generationType);

            DungeonGeneratorScript dungeonGenerator = (DungeonGeneratorScript)target;

            if (dungeonGenerator.ShouldShowNextStepKeySelector())
            {
                EditorGUILayout.PropertyField(executeNextStepKey);
            }

            EditorGUILayout.PropertyField(immediateStart);
            EditorGUILayout.PropertyField(useRandomSeed);
            EditorGUILayout.PropertyField(seed);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space();

        EditorGUILayout.PropertyField(objectsToKeepByName);

        serializedObject.ApplyModifiedProperties();

        DungeonGeneratorScript script = (DungeonGeneratorScript)target;

        EditorGUILayout.Space();

        if (GUILayout.Button("Generate Dungeon"))
        {
            script.StartDungeonGeneration();
        }
    }
}