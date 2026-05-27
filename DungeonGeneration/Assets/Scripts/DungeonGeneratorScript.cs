using NaughtyAttributes;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

public class DungeonGeneratorScript : MonoBehaviour
{
    [Header("Generation Settings")]
    [SerializeField] private int minRoomSize = 8;
    [SerializeField] private RectInt startRoomParams = new RectInt(0, 0, 200, 200);
    [SerializeField] private float randomSizeMin = 0.05f;
    [SerializeField] private float randomSizeMax = 0.75f;
    [SerializeField] private int generationsBeforePreservedRooms = 8;
    [SerializeField] private int generationsBeforeChanceToStopCutting = 13;
    [SerializeField] private int preservedRoomChance = 20;
    [SerializeField] private int stopSplittingChance = 12;
    [SerializeField] private int deleteRoomPercentage = 10;

    [Header("Prefabs")]
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject cameraPrefab;
    [SerializeField] private GameObject navMesh;
    [SerializeField] private GameObject uiPrefab;
    [SerializeField] private GameObject eventSystem;
    [SerializeField] private List<GameObject> wallPrefabs;

    [Header("Optional")]
    [SerializeField] private bool wait = false;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 1;
    [SerializeField] private bool immediateStart = true;

    [Header("Inspector prep")]
    [SerializeField] private List<string> objectsToKeepByName = new List<string>() 
    {
        "Dungeon Generator",
        "General light",
        "DebugDrawingBatcher_default"
    };

    private Vector3Graph graph;
    private Transform dungeonParent;
    private float WaitTime;

    private List<RectInt> roomsPreserved = new List<RectInt>();
    private List<RectInt> finalRooms = new List<RectInt>();
    private List<RectInt> roomsToRemove = new List<RectInt>();
    private List<Vector3> wallPositions = new List<Vector3>();
    private List<Vector3> doors = new List<Vector3>();
    private List<Vector3> floorPositions = new List<Vector3>();
    private List<Vector3> playerSpawnPositions = new List<Vector3>();
    private NavMeshSurface navMeshSurface = null;
    private List<Vector3Line> sharedWallLines = new List<Vector3Line>();
    private struct Vector3Line
    {
        public Vector3 start;
        public Vector3 end;

        public Vector3 roomA;
        public Vector3 roomB;

        public Vector3Line(Vector3 start, Vector3 end, Vector3 roomA, Vector3 roomB)
        {
            this.start = start;
            this.end = end;
            this.roomA = roomA;
            this.roomB = roomB;
        }
    }

    void Start()
    {
        WaitTime = (startRoomParams.height + startRoomParams.width) / 20;
        SeedPick();
        graph = new Vector3Graph();
        if (immediateStart)
            if (wait)
                StartGenerateDungeon();
            else
                StartSlowGenerateDungeon();
    }

    private void SeedPick()
    {
        if (useRandomSeed)
        {
            seed = UnityEngine.Random.Range(0, int.MaxValue);
        }
        UnityEngine.Random.InitState(seed);
    }

    //Immediate generation
    [Button("Generate Dungeon")]
    private void StartGenerateDungeon()
    {
        StopAllCoroutines();
        StartCoroutine(GenerateDungeon());
    }

    private IEnumerator GenerateDungeon()
    {
        yield return StartCoroutine(PrepareSceneForGeneration());

        DivideRooms();

        AddPreservedRooms();

        RemoveSmallestRooms();

        //Doors depend on intersections, and walls depend on doors.
        FindIntersections();
        DecideDoors();
        RemoveExtraDoors();
        graph.PrintGraph();
        SpawnAssets();

        SpawnGameplayObjects();

        DrawRooms();
    }

    private IEnumerator PrepareSceneForGeneration()
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
        ClearUnityConsole();
        graph = new Vector3Graph();

        GameObject oldDungeon = GameObject.Find("Dungeon");

        if (oldDungeon != null)
        {
            DestroyObjectSafe(oldDungeon);
        }

        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();

        foreach (GameObject obj in rootObjects)
        {
            if (!objectsToKeepByName.Contains(obj.name))
                DestroyObjectSafe(obj);
        }

        GameObject newDungeon = new GameObject("Dungeon");
        dungeonParent = newDungeon.transform;
        yield return null;
    }

    private void ClearUnityConsole()
    {
        Assembly assembly = Assembly.GetAssembly(typeof(Editor));
        Type logEntries = assembly.GetType("UnityEditor.LogEntries");

        MethodInfo clearMethod = logEntries.GetMethod(
            "Clear",
            BindingFlags.Static | BindingFlags.Public
        );

        clearMethod.Invoke(null, null);
    }

    private void DestroyObjectSafe(GameObject obj)
    {
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private void DivideRooms()
    {
        finalRooms.Clear();
        roomsPreserved.Clear();

        DivideRoomRecursive(startRoomParams, 1);
    }

    private void DivideRoomRecursive(RectInt room, int generation)
    {
        bool stopSplitting = StopSplitting(generation);

        if (!TrySplit(room, out RectInt roomA, out RectInt roomB))
        {
            finalRooms.Add(room);
            return;
        }

        if (stopSplitting)
        {
            finalRooms.Add(room);
            return;
        }

        bool preserveRoom = PreserveRoom(generation);

        if (preserveRoom)
        {
            roomsPreserved.Add(room);
            return;
        }

        DivideRoomRecursive(roomA, generation + 1);
        DivideRoomRecursive(roomB, generation + 1);
    }

    private bool TrySplit(RectInt room, out RectInt roomA, out RectInt roomB)
    {
        roomA = default;
        roomB = default;

        bool canSplitX = room.width >= minRoomSize * 2;
        bool canSplitY = room.height >= minRoomSize * 2;

        if (!canSplitX && !canSplitY)
            return false;

        bool splitX;

        if (canSplitX && canSplitY)
        {
            splitX = RandomBool();
        }
        else if (canSplitX)
        {
            splitX = true;
        }
        else
        {
            splitX = false;
        }

        if (splitX)
        {
            int aWidth = GetRandomSplitSize(room.width);
            int bWidth = room.width - aWidth;

            roomA = new RectInt(room.x, room.y, aWidth, room.height);
            roomB = new RectInt(room.xMax - bWidth, room.y, bWidth, room.height);

            return true;
        }
        else
        {
            int aHeight = GetRandomSplitSize(room.height);
            int bHeight = room.height - aHeight;

            roomA = new RectInt(room.x, room.y, room.width, aHeight);
            roomB = new RectInt(room.x, room.yMax - bHeight, room.width, bHeight);

            return true;
        }
    }

    private int GetRandomSplitSize(int usableSize)
    {
        float minRatio = (float)minRoomSize / usableSize;
        float ratio = UnityEngine.Random.Range(randomSizeMin, randomSizeMax);

        ratio = Mathf.Max(ratio, minRatio);

        int smallSize = Mathf.RoundToInt(usableSize * ratio);
        smallSize = Mathf.Clamp(smallSize, minRoomSize, usableSize - minRoomSize);

        int bigSize = usableSize - smallSize;

        if (RandomBool())
            return bigSize;
        else
            return smallSize;
    }

    private bool PreserveRoom(int generation)
    {
        if (generation < generationsBeforePreservedRooms)
            return false;

        return UnityEngine.Random.Range(0, 100) < preservedRoomChance;
    }

    private bool StopSplitting(int generation)
    {
        if (generation < generationsBeforeChanceToStopCutting)
            return false;

        return UnityEngine.Random.Range(0, 100) < stopSplittingChance;
    }

    private void AddPreservedRooms()
    {
        foreach (var room in roomsPreserved)
        {
            finalRooms.Add(room);
        }
        Debug.Log($"Made {finalRooms.Count} rooms");
    }

    private void RemoveSmallestRooms()
    {
        roomsToRemove.Clear();
        int smallRoomsToRemove = (int)(finalRooms.Count * deleteRoomPercentage / 100);
        int removedRooms = 0;
        List<RectInt> roomPool = new List<RectInt>(finalRooms);
        roomPool.Sort((a, b) => (a.width * a.height).CompareTo(b.width * b.height));

        for (int i = 0; i < smallRoomsToRemove; i++)
        {
            bool removed = false;
            while (!removed && roomPool.Count() > 0)
            {
                RectInt room = roomPool[0];
                if (CanRemove(room))
                {
                    roomsToRemove.Add(room);
                    removed = true;
                    finalRooms.Remove(room);
                    roomPool.Remove(room);
                    removedRooms++;
                }
                else
                    roomPool.Remove(room);
            }
        }

        Debug.Log($"Rooms removed: {removedRooms}");
    }

    private bool CanRemove(RectInt room)
    {
        List<RectInt> remainingRooms = new List<RectInt>(finalRooms);
        remainingRooms.Remove(room);

        HashSet<RectInt> visitedRooms = new HashSet<RectInt>();
        Queue<RectInt> roomsToCheck = new Queue<RectInt>();

        RectInt startRoom = remainingRooms[0];

        visitedRooms.Add(startRoom);
        roomsToCheck.Enqueue(startRoom);

        while (roomsToCheck.Count > 0)
        {
            RectInt currentRoom = roomsToCheck.Dequeue();

            foreach (RectInt otherRoom in remainingRooms)
            {
                if (visitedRooms.Contains(otherRoom))
                    continue;

                RectInt intersection = AlgorithmsUtils.Intersect(currentRoom, otherRoom);

                if (IsVerticalIntersection(intersection) || IsHorizontalIntersection(intersection))
                {
                    visitedRooms.Add(otherRoom);
                    roomsToCheck.Enqueue(otherRoom);
                }
            }
        }

        return visitedRooms.Count == remainingRooms.Count;
    }

    private void FindIntersections()
    {
        sharedWallLines.Clear();
        for (int i = 0; i < finalRooms.Count; i++)
        {
            for (int j = i + 1; j < finalRooms.Count; j++)
            {
                RectInt roomA = finalRooms[i];
                RectInt roomB = finalRooms[j];
                if (roomB != roomA)
                {
                    RectInt intersection = AlgorithmsUtils.Intersect(roomB, roomA);

                    Vector3 roomAPosition = new Vector3(roomA.center.x, 0f, roomA.center.y);
                    Vector3 roomBPosition = new Vector3(roomB.center.x, 0f, roomB.center.y);

                    AddSharedWallLine(intersection, roomAPosition, roomBPosition);
                }
            }
        }
    }

    private void AddSharedWallLine(RectInt intersection, Vector3 roomA, Vector3 roomB)
    {
        if (IsVerticalIntersection(intersection))
        {
            Vector3 start = new Vector3(intersection.x, 0.5f, intersection.y + 1);
            Vector3 end = new Vector3(intersection.x, 0.5f, intersection.yMax - 1);

            sharedWallLines.Add(new Vector3Line(start, end, roomA, roomB));
        }
        else if (IsHorizontalIntersection(intersection))
        {
            Vector3 start = new Vector3(intersection.x + 1, 0.5f, intersection.y);
            Vector3 end = new Vector3(intersection.xMax - 1, 0.5f, intersection.y);

            sharedWallLines.Add(new Vector3Line(start, end, roomA, roomB));
        }
    }

    private bool IsVerticalIntersection(RectInt intersection)
    {
        if (intersection.width == 0 && intersection.height > 1)
            return true;
        return false;
    }

    private bool IsHorizontalIntersection(RectInt intersection)
    {
        if (intersection.width > 1 && intersection.height == 0)
            return true;
        return false;
    }

    private void DecideDoors()
    {
        doors.Clear();
        foreach (var line in sharedWallLines)
        {
            bool isAtEdge = true;
            Vector3 doorPosition = new Vector3();
            while (isAtEdge)
            {
                doorPosition = Vector3.Lerp(line.start, line.end, UnityEngine.Random.value);
                doorPosition.x = Mathf.Round(doorPosition.x);
                doorPosition.y = 0.5f;
                doorPosition.z = Mathf.Round(doorPosition.z);
                if (doorPosition.x > 1 && doorPosition.z > 1)
                    isAtEdge = false;
            }


            doors.Add(doorPosition);

            doorPosition.x -= 0.5f;
            doorPosition.y -= 0.5f;
            doorPosition.z -= 0.5f;

            graph.AddEdge(line.roomA, doorPosition);
            graph.AddEdge(doorPosition, line.roomB);
        }
        Debug.Log($"Made {doors.Count} doors");
    }

    private void RemoveExtraDoors()
    {
        List<Vector3> roomNodes = GetRoomNodes();

        List<Vector3> doorPool = new List<Vector3>(doors);

        ShuffleList(doorPool);

        int doorsRemoved = 0;

        foreach (var wallDoor in doorPool)
        {
            Vector3 graphDoor = new Vector3(wallDoor.x - 0.5f, wallDoor.y - 0.5f, wallDoor.z - 0.5f);
            if (graph.CanRemoveNodeWithoutDisconnecting(graphDoor, roomNodes))
            {
                graph.RemoveNode(graphDoor);

                doors.Remove(wallDoor);

                doorsRemoved++;
            }
        }
        Debug.Log($"Removed {doorsRemoved} doors");
    }

    private List<Vector3> GetRoomNodes()
    {
        List<Vector3> roomNodes = new List<Vector3>();

        foreach (RectInt room in finalRooms)
        {
            Vector3 roomPosition = new Vector3(room.center.x, 0f, room.center.y);
            roomNodes.Add(roomPosition);
        }

        return roomNodes;
    }

    private void ShuffleList<T>(List<T> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int randomIndex = UnityEngine.Random.Range(i, list.Count);

            T temp = list[i];
            list[i] = list[randomIndex];
            list[randomIndex] = temp;
        }
    }

    private void SpawnAssets()
    {
        wallPositions.Clear();
        floorPositions.Clear();

        SpawnWalls();
        SpawnFloor();
    }

    private void SpawnWalls()
    {
        SetTakenPositions();
        int[,] tileMap = GenerateTileMap();
        int rows = tileMap.GetLength(0);
        int cols = tileMap.GetLength(1);
        GameObject wallsParent = new GameObject("Walls");
        wallsParent.transform.SetParent(dungeonParent);

        for (int i = 0; i <= rows - 2; i++)
        {
            for (int j = 0; j <= cols - 2; j++)
            {
                // Convert the 2x2 wall pattern into a prefab index.
                // Each corner contributes one bit, giving values from 0 to 15.
                int prefabNumber = GetWallPrefabIndex(tileMap, i, j);

                if (prefabNumber != 0)
                {
                    GameObject wallToInstantiate = wallPrefabs[prefabNumber];
                    wallToInstantiate.transform.position = new Vector3(i + 1f, 0f, j + 1f);
                    Instantiate(wallToInstantiate, wallsParent.transform);
                }
            }
        }
    }

    private void SetTakenPositions()
    {
        foreach (var r in finalRooms)
        {
            for (int i = 0; i <= r.width; i++)
            {
                for (int j = 0; j <= r.height; j++)
                {
                    if ((i == 0 || i == r.width || j == 0 || j == r.height) && !doors.Contains(new Vector3(r.xMin + i, 0.5f, r.yMin + j)))
                    {
                        wallPositions.Add(new Vector3(r.xMin + i, 0.5f, r.yMin + j));
                    }
                }
            }
        }
    }

    private int[,] GenerateTileMap()
    {
        int[,] tileMap = new int[startRoomParams.width, startRoomParams.height];
        int rows = tileMap.GetLength(0);
        int cols = tileMap.GetLength(1);

        //Fill the map with empty spaces
        for (int i = 0; i <= rows - 1; i++)
        {
            for (int j = 0; j <= cols - 1; j++)
            {
                tileMap[i, j] = 0;
            }
        }

        foreach (Vector3 position in wallPositions)
        {
            tileMap[(int)(position.x - 0.5f), (int)(position.z - 0.5f)] = 1;
        }

        return tileMap;
    }

    private int GetWallPrefabIndex(int[,] tileMap, int x, int y)
    {
        return tileMap[x, y]
            + tileMap[x, y + 1] * 2
            + tileMap[x + 1, y + 1] * 4
            + tileMap[x + 1, y] * 8;
    }

    private void SpawnFloor()
    {
        playerSpawnPositions.Clear();

        GameObject floorParent = new GameObject("Floor");
        floorParent.transform.SetParent(dungeonParent);

        Transform floorTransform = floorParent.transform;

        HashSet<Vector2Int> passableTiles = BuildPassableFloorTiles();
        HashSet<Vector2Int> stopTiles = BuildWallStopTiles();

        Vector2Int startTile = GetRandomRoomCenterTile();

        HashSet<Vector2Int> reachedTiles = FloodFillFloor(startTile, passableTiles, stopTiles);

        foreach (Vector2Int tile in reachedTiles)
        {
            Vector3 position = new Vector3(tile.x, 0f, tile.y);

            floorPrefab.transform.position = position;

            floorPositions.Add(position);

            Instantiate(floorPrefab, floorTransform);

            if (passableTiles.Contains(tile) && !stopTiles.Contains(tile))
                playerSpawnPositions.Add(position);
        }
    }

    private HashSet<Vector2Int> BuildPassableFloorTiles()
    {
        HashSet<Vector2Int> passableTiles = new HashSet<Vector2Int>();

        foreach (RectInt room in finalRooms)
        {
            for (int x = room.xMin + 1; x < room.xMax; x++)
            {
                for (int z = room.yMin + 1; z < room.yMax; z++)
                {
                    passableTiles.Add(new Vector2Int(x, z));
                }
            }
        }

        foreach (Vector3 door in doors)
        {
            Vector2Int doorTile = new Vector2Int(
                Mathf.RoundToInt(door.x),
                Mathf.RoundToInt(door.z)
            );

            passableTiles.Add(doorTile);
        }

        return passableTiles;
    }

    private HashSet<Vector2Int> BuildWallStopTiles()
    {
        HashSet<Vector2Int> stopTiles = new HashSet<Vector2Int>();

        foreach (Vector3 wallPosition in wallPositions)
        {
            Vector2Int wallTile = new Vector2Int(
                Mathf.RoundToInt(wallPosition.x),
                Mathf.RoundToInt(wallPosition.z)
            );

            stopTiles.Add(wallTile);
        }

        return stopTiles;
    }

    private Vector2Int GetRandomRoomCenterTile()
    {
        RectInt room = finalRooms[UnityEngine.Random.Range(0, finalRooms.Count)];

        int x = Mathf.RoundToInt(room.center.x);
        int z = Mathf.RoundToInt(room.center.y);

        x = Mathf.Clamp(x, room.xMin + 1, room.xMax - 1);
        z = Mathf.Clamp(z, room.yMin + 1, room.yMax - 1);

        return new Vector2Int(x, z);
    }

    private HashSet<Vector2Int> FloodFillFloor(Vector2Int startTile, HashSet<Vector2Int> passableTiles, HashSet<Vector2Int> stopTiles)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        Queue<Vector2Int> tilesToCheck = new Queue<Vector2Int>();

        visited.Add(startTile);
        tilesToCheck.Enqueue(startTile);

        Vector2Int[] directions =
        {
    new Vector2Int(1, 0),
    new Vector2Int(-1, 0),
    new Vector2Int(0, 1),
    new Vector2Int(0, -1)
    };

        while (tilesToCheck.Count > 0)
        {
            Vector2Int currentTile = tilesToCheck.Dequeue();

            if (stopTiles.Contains(currentTile))
                continue;

            foreach (Vector2Int direction in directions)
            {
                Vector2Int neighbour = currentTile + direction;

                if (visited.Contains(neighbour))
                    continue;

                bool isPassableTile = passableTiles.Contains(neighbour);
                bool isStopTile = stopTiles.Contains(neighbour);

                if (!isPassableTile && !isStopTile)
                    continue;

                visited.Add(neighbour);

                if (isPassableTile)
                {
                    tilesToCheck.Enqueue(neighbour);
                }
            }
        }

        return visited;
    }

    private void SpawnGameplayObjects()
    {
        GameObject gameplayObjectsParentObject = new GameObject("Gameplay objects");
        Transform gameplayObjectsParentTransform = gameplayObjectsParentObject.transform;
        gameplayObjectsParentTransform.SetParent(dungeonParent);
        GameObject navmesh = Instantiate(navMesh, gameplayObjectsParentTransform);

        navMeshSurface = navmesh.GetComponent<NavMeshSurface>();
        navMeshSurface.BuildNavMesh();

        int playerPosition = UnityEngine.Random.Range(0, playerSpawnPositions.Count);
        playerPrefab.transform.position = new Vector3(playerSpawnPositions[playerPosition].x, 1f, playerSpawnPositions[playerPosition].z);
        GameObject player = Instantiate(playerPrefab, dungeonParent);

        Instantiate(cameraPrefab, gameplayObjectsParentTransform);
        player.AddComponent<PlayerController>();

        Instantiate(uiPrefab, gameplayObjectsParentTransform);
        Instantiate(eventSystem, gameplayObjectsParentTransform);
    }

    private void DrawRooms()
    {
        foreach (var r in finalRooms)
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(r, Color.yellow);
            });
        }
    }

    private bool RandomBool()
    {
        return UnityEngine.Random.Range(0, 2) == 0;
    }

    //Slow debug generation
    [Button("Slow Generate Dungeon")]
    private void StartSlowGenerateDungeon() 
    {
        StopAllCoroutines();
        StartCoroutine(SlowGenerateDungeon());
    }

    private IEnumerator SlowGenerateDungeon()
    {
        yield return StartCoroutine(PrepareSceneForGeneration());

        yield return StartCoroutine(SlowDivideRooms());
        AddPreservedRooms();
        yield return StartCoroutine(SlowRemoveSmallestRooms());

        //Doors depend on intersections, and walls depend on doors.
        FindIntersections();
        yield return StartCoroutine(SlowDecideDoors());
        yield return StartCoroutine(SlowRemoveExtraDoors());
        yield return StartCoroutine(graph.SlowPrintGraph());
        yield return StartCoroutine(SlowSpawnAssets());

        SpawnGameplayObjects();
    }

    private IEnumerator SlowDivideRooms()
    {
        finalRooms.Clear();
        roomsPreserved.Clear();

        yield return StartCoroutine(SlowDivideRoomRecursive(startRoomParams, 1));
    }

    private IEnumerator SlowDivideRoomRecursive(RectInt room, int generation)
    {
        bool stopSplitting = StopSplitting(generation);

        if (!TrySplit(room, out RectInt roomA, out RectInt roomB))
        {
            finalRooms.Add(room);

            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(room, Color.yellow);
            });

            yield return new WaitForSeconds(0.0001f);
            yield break;
        }

        bool preserveRoom = PreserveRoom(generation);

        if (preserveRoom)
        {
            roomsPreserved.Add(room);

            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(room, Color.blue);
            });

            yield return new WaitForSeconds(0.001f);
            yield break;
        }

        DebugDrawingBatcher.GetInstance().BatchCall(() =>
        {
            AlgorithmsUtils.DebugRectInt(room, Color.green);
        });

        yield return new WaitForSeconds(0.001f);

        DebugDrawingBatcher.GetInstance().BatchCall(() =>
        {
            AlgorithmsUtils.DebugRectInt(roomA, Color.yellow);
            AlgorithmsUtils.DebugRectInt(roomB, Color.yellow);
        });

        yield return new WaitForSeconds(0.001f);

        yield return StartCoroutine(SlowDivideRoomRecursive(roomA, generation + 1));
        yield return StartCoroutine(SlowDivideRoomRecursive(roomB, generation + 1));
    }

    private IEnumerator SlowRemoveSmallestRooms()
    {
        roomsToRemove.Clear();
        int smallRoomsToRemove = (int)(finalRooms.Count * deleteRoomPercentage / 100);
        Debug.Log($"Rooms to remove: {smallRoomsToRemove}");
        int removedRooms = 0;
        List<RectInt> roomPool = new List<RectInt>(finalRooms);
        roomPool.Sort((a, b) => (a.width * a.height).CompareTo(b.width * b.height));

        for (int i = 0; i < smallRoomsToRemove; i++)
        {
            bool removed = false;
            while (!removed && roomPool.Count() > 0)
            {
                RectInt room = roomPool[0];
                if (CanRemove(room))
                {
                    DebugDrawingBatcher.GetInstance().BatchCall(() =>
                    {
                        AlgorithmsUtils.DebugRectInt(room, Color.red);
                    });
                    roomsToRemove.Add(room);
                    removed = true;
                    finalRooms.Remove(room);
                    roomPool.Remove(room);
                    removedRooms++;
                    yield return new WaitForSeconds(0.01f);
                }
                else
                    roomPool.Remove(room);
            }
        }

        Debug.Log($"Rooms removed: {removedRooms}");
    }

    private IEnumerator SlowDecideDoors()
    {
        doors.Clear();
        foreach (var line in sharedWallLines)
        {
            bool isAtEdge = true;
            Vector3 doorPosition = new Vector3();
            while (isAtEdge)
            {
                doorPosition = Vector3.Lerp(line.start, line.end, UnityEngine.Random.value);
                doorPosition.x = Mathf.Round(doorPosition.x);
                doorPosition.y = 0.5f;
                doorPosition.z = Mathf.Round(doorPosition.z);
                if (doorPosition.x > 1 && doorPosition.z > 1)
                    isAtEdge = false;
            }

            doors.Add(doorPosition);

            RectInt doorSquare = new RectInt((int)doorPosition.x - 1, (int)doorPosition.z - 1, 1, 1);
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(doorSquare, Color.cyan);
            });
            yield return new WaitForSeconds(0.02f);

            doorPosition.x -= 0.5f;
            doorPosition.y -= 0.5f;
            doorPosition.z -= 0.5f;

            graph.AddEdge(line.roomA, doorPosition);
            graph.AddEdge(doorPosition, line.roomB);
        }
        Debug.Log($"Made {doors.Count} doors");
    }

    private IEnumerator SlowRemoveExtraDoors()
    {
        List<Vector3> roomNodes = GetRoomNodes();

        List<Vector3> doorPool = new List<Vector3>(doors);

        ShuffleList(doorPool);

        int doorsRemoved = 0;
        foreach (var wallDoor in doorPool)
        {
            Vector3 graphDoor = new Vector3(wallDoor.x - 0.5f, wallDoor.y - 0.5f, wallDoor.z - 0.5f);
            if (graph.CanRemoveNodeWithoutDisconnecting(graphDoor, roomNodes))
            {
                RectInt deletedDoor = new RectInt((int)(wallDoor.x - 0.5f), (int)(wallDoor.z - 0.5f), 1, 1);
                DebugDrawingBatcher.GetInstance().BatchCall(() =>
                {
                    AlgorithmsUtils.DebugRectInt(deletedDoor, Color.red);
                });

                graph.RemoveNode(graphDoor);

                doors.Remove(wallDoor);

                doorsRemoved++;

                yield return new WaitForSeconds(0.01f);
            }
        }
        Debug.Log($"Removed {doorsRemoved} doors");
    }

    private IEnumerator SlowSpawnAssets()
    {
        wallPositions.Clear();
        floorPositions.Clear();

        yield return StartCoroutine(SlowSpawnWalls());
        yield return StartCoroutine(SlowSpawnFloor());
    }

    private IEnumerator SlowSpawnWalls()
    {
        SetTakenPositions();
        int[,] tileMap = GenerateTileMap();
        int rows = tileMap.GetLength(0);
        int cols = tileMap.GetLength(1);
        GameObject wallsParent = new GameObject("Walls");
        wallsParent.transform.SetParent(dungeonParent);

        int counter = 0;
        for (int i = 0; i <= rows - 2; i++)
        {
            for (int j = 0; j <= cols - 2; j++)
            {
                int prefabNumber = GetWallPrefabIndex(tileMap, i, j);

                if (prefabNumber != 0)
                {
                    GameObject wallToInstantiate = wallPrefabs[prefabNumber];
                    wallToInstantiate.transform.position = new Vector3(i + 1f, 0f, j + 1f);
                    Instantiate(wallToInstantiate, wallsParent.transform);
                    if (counter >= WaitTime)
                    {
                        counter = 0;
                        yield return null;
                    }
                    else
                        counter++;
                }
            }
        }
    }

    private IEnumerator SlowSpawnFloor()
    {
        playerSpawnPositions.Clear();

        GameObject floorParent = new GameObject("Floor");
        Transform floorTransform = floorParent.transform;
        floorTransform.SetParent(dungeonParent);

        HashSet<Vector2Int> passableTiles = BuildPassableFloorTiles();
        HashSet<Vector2Int> stopTiles = BuildWallStopTiles();

        Vector2Int startTile = GetRandomRoomCenterTile();

        HashSet<Vector2Int> reachedTiles = FloodFillFloor(startTile, passableTiles, stopTiles);
        int counter = 0;
        foreach (Vector2Int tile in reachedTiles)
        {

            Vector3 position = new Vector3(tile.x, 0f, tile.y);

            floorPrefab.transform.position = position;

            floorPositions.Add(position);

            Instantiate(floorPrefab, floorTransform);
            
            if (counter > WaitTime*3)
            {
                yield return null;
                counter = 0;
            }
            counter++;

            if (passableTiles.Contains(tile) && !stopTiles.Contains(tile))
                playerSpawnPositions.Add(position);
        }
    }
}