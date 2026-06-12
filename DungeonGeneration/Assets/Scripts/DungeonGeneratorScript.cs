using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Procedurally generates a dungeon by recursively splitting a starting rectangle into rooms,
/// optionally preserving larger rooms, removing some small rooms while keeping the layout connected,
/// creating doors between neighbouring rooms, removing unnecessary doors through graph connectivity checks,
/// and finally spawning walls, floors, NavMesh, player, camera, UI, and debug visuals.
/// 
/// The script supports both instant generation and slow debug generation.
/// Slow generation performs the same main steps but yields between operations so the process can be visualized.
/// </summary>
public class DungeonGeneratorScript : MonoBehaviour
{
    #region Class Fields

    [Header("Generation Settings")]
    // Controls the size, randomness, recursion depth, room preservation,
    // stopping chance, and percentage of small rooms removed after generation.
    [SerializeField] private int minRoomSize = 8;
    [SerializeField] private RectInt startRoomParams = new RectInt(0, 0, 200, 200);
    [SerializeField] private float randomSizeMin = 0.05f;
    [SerializeField] private float randomSizeMax = 0.75f;
    [SerializeField] private int generationsBeforePreservedRooms = 8;
    [SerializeField] private int preservedRoomChance = 20;
    [SerializeField] private int deleteRoomPercentage = 50;
    

    [Header("Prefabs")]
    // Prefabs spawned after the logical dungeon layout has been generated.
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject cameraPrefab;
    [SerializeField] private GameObject navMesh;
    [SerializeField] private GameObject uiPrefab;
    [SerializeField] private GameObject eventSystem;
    [SerializeField] private List<GameObject> wallPrefabs;

    [Header("Optional")]
    // Debug and reproducibility options.
    // wait controls whether generation is visualized slowly or created immediately.
    // useRandomSeed decides whether the seed is randomized each run.
    [SerializeField] private GenerationType generationType = GenerationType.Instant;
    //[ShowIf(nameof(showOptionalStuff))]
    [ShowIf(nameof(ShouldShowNextStepKeySelector))]
    [SerializeField] private KeyCode executeNextStepKey = KeyCode.Space;
    [SerializeField] private bool immediateStart = true;
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int seed = 1;

    [Header("Inspector prep")]
    // Root objects with these names are kept when regenerating the scene.
    // Everything else is deleted before a new dungeon is generated.
    [SerializeField]
    private List<string> objectsToKeepByName = new List<string>()
    {
        "Dungeon Generator",
        "General light",
        "DebugDrawingBatcher_default"
    };

    private Vector3Graph graph;
    private Transform dungeonParent;
    private float waitTime;
    private bool isGenerating = false;
    private bool moveToNextStep = false;

    // Main room collections.
    // roomsPreserved stores rooms that stopped splitting early because of preservation.
    // finalRooms contains the rooms used for the final dungeon layout.
    private List<RectInt> roomsPreserved = new List<RectInt>();
    private List<RectInt> finalRooms = new List<RectInt>();

    // Generated world-space positions used for spawning and path/floor logic.
    private List<Vector3> wallPositions = new List<Vector3>();
    private List<Vector3> doors = new List<Vector3>();
    private List<Vector3> floorPositions = new List<Vector3>();
    private List<Vector3> playerSpawnPositions = new List<Vector3>();
    private NavMeshSurface navMeshSurface = null;

    // Lines representing shared walls between rooms.
    // These are later used to place possible doors.
    private List<Vector3Line> sharedWallLines = new List<Vector3Line>();

    /// <summary>
    /// Represents a shared wall segment between two rooms.
    /// start and end define the usable section of the wall where a door can be placed.
    /// roomA and roomB store the graph node positions of the rooms connected by this wall.
    /// </summary>
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
    private enum GenerationType
    {
        Instant,
        KeyBased,
        Slow
    }
    #endregion


    #region Setup
    /// <summary>
    /// Initializes generation settings, creates the connectivity graph,
    /// and optionally starts dungeon generation automatically.
    /// </summary>
    void Start()
    {
        waitTime = (startRoomParams.height + startRoomParams.width) / 20;
        graph = new Vector3Graph();
        if (immediateStart)
            StartDungeonGeneration();
    }

    /// <summary>
    /// Picks a new random seed if random seeds are enabled,
    /// then initializes Unity's random generator with the selected seed.
    /// This makes generation either reproducible or different each run.
    /// </summary>
    private void SeedPick()
    {
        if (useRandomSeed)
        {
            seed = UnityEngine.Random.Range(0, int.MaxValue);
        }
        UnityEngine.Random.InitState(seed);
    }

    /// <summary>
    /// Clears debug drawings and the Unity console, resets the graph,
    /// deletes the old dungeon and unwanted root scene objects,
    /// then creates a fresh Dungeon parent object for the new generated content.
    /// </summary>
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

    /// <summary>
    /// Clears the Unity Editor console using reflection.
    /// This only works inside the Unity Editor because it uses UnityEditor.LogEntries.
    /// </summary>
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

    /// <summary>
    /// Safely destroys a GameObject using Destroy during play mode
    /// and DestroyImmediate outside play mode.
    /// </summary>
    private void DestroyObjectSafe(GameObject obj)
    {
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }
    #endregion


    #region Generation Pipeline

    /// <summary>
    /// Starts dungeon generation using the selected generation type.
    /// Any currently running generation coroutine is stopped first,
    /// allowing regeneration to safely replace an in-progress generation.
    /// </summary>
    [Button("Generate Dungeon", EButtonEnableMode.Playmode)]
    private void StartDungeonGeneration()
    {
        SeedPick();
        StopAllCoroutines();
        StartCoroutine(GenerateDungeon());
    }

    [ShowIf(nameof(ShouldShowNextStepButton))]
    [Button("Move to next step", EButtonEnableMode.Playmode)]
    private void MoveToNextStep()
    {
        moveToNextStep = true;
    }

    // Generation pipeline:
    // 1. Prepare the scene.
    // 2. Recursively split the starting area into rooms.
    // 3. Add preserved rooms back into the final layout.
    // 4. Remove some small rooms while preserving connectivity.
    // 5. Find shared walls between rooms.
    // 6. Place doors on shared walls.
    // 7. Remove unnecessary doors while preserving graph connectivity.
    // 8. Spawn walls and floor.
    // 9. Spawn gameplay objects.

    /// <summary>
    /// Runs the full dungeon generation pipeline.
    /// Slow-only debug drawing and waiting are controlled by the selected generation type.
    /// </summary>
    public IEnumerator GenerateDungeon()
    {
        isGenerating = true;
        yield return StartCoroutine(PrepareSceneForGeneration());

        yield return StartCoroutine(WaitIfKeyBased());
        yield return StartCoroutine(GenerateRooms());

        yield return StartCoroutine(WaitIfKeyBased());
        yield return StartCoroutine(GenerateDoors());

        yield return StartCoroutine(WaitIfKeyBased());
        yield return StartCoroutine(GraphVisualization());

        yield return StartCoroutine(WaitIfKeyBased());
        yield return StartCoroutine(SpawnAssets());
        isGenerating = false;
    }

    #endregion


    #region Room Logic

    /// <summary>
    /// Divides rooms while preserving some at a bigger size, adds back the prezerved rooms, then removes the smallest ones.
    /// </summary>
    private IEnumerator GenerateRooms()
    {
        yield return StartCoroutine(DivideRooms());

        AddPreservedRooms();

        if (generationType == GenerationType.Instant || generationType == GenerationType.KeyBased)
            DrawRooms();

        yield return StartCoroutine(WaitIfKeyBased());
        yield return StartCoroutine(RemoveSmallestRooms());
    }

    #region Room Generation

    /// <summary>
    /// Clears previous room data and starts recursive room division from the initial room rectangle.
    /// </summary>
    private IEnumerator DivideRooms()
    {
        finalRooms.Clear();
        roomsPreserved.Clear();

        yield return StartCoroutine(DivideRoomRecursive(startRoomParams, 1));
    }

    /// <summary>
    /// Recursively attempts to split a room into two smaller rooms.
    /// In slow mode, rooms are drawn as they are split, preserved, or finalized.
    /// </summary>
    private IEnumerator DivideRoomRecursive(RectInt room, int generation)
    {
        if (!TrySplit(room, out RectInt roomA, out RectInt roomB))
        {
            finalRooms.Add(room);

            if (generationType == GenerationType.Slow)
            {
                DebugDrawingBatcher.GetInstance().BatchCall(() =>
                {
                    AlgorithmsUtils.DebugRectInt(room, Color.yellow);
                });
                yield return null;
            }
            yield break;
        }

        bool preserveRoom = PreserveRoom(generation);

        if (preserveRoom)
        {
            roomsPreserved.Add(room);

            if (generationType == GenerationType.Slow)
            {
                DebugDrawingBatcher.GetInstance().BatchCall(() =>
                {
                    AlgorithmsUtils.DebugRectInt(room, Color.blue);
                });
                yield return null;
            }
            yield break;
        }

        if (generationType == GenerationType.Slow)
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(room, Color.green);
            });
            yield return null;
        }



        if (generationType == GenerationType.Slow)
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(roomA, Color.yellow);
                AlgorithmsUtils.DebugRectInt(roomB, Color.yellow);
            });
            yield return null;
        }

        yield return StartCoroutine(DivideRoomRecursive(roomA, generation + 1));
        yield return StartCoroutine(DivideRoomRecursive(roomB, generation + 1));
    }

    /// <summary>
    /// Attempts to split a room either vertically or horizontally.
    /// The split direction is chosen randomly if both directions are possible,
    /// otherwise it uses the only valid direction.
    /// </summary>
    /// <param name="room">The room to split.</param>
    /// <param name="roomA">The first resulting room.</param>
    /// <param name="roomB">The second resulting room.</param>
    /// <returns>True if the room was successfully split, false if it was too small.</returns>
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

    /// <summary>
    /// Chooses a random split size for one axis while respecting the minimum room size.
    /// The method sometimes returns the larger side and sometimes the smaller side
    /// to avoid all splits leaning in the same direction.
    /// </summary>
    /// <param name="usableSize">The width or height being split.</param>
    /// <returns>The size of the first part of the split.</returns>
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

    /// <summary>
    /// Determines whether a room should be preserved instead of split further.
    /// Preservation can only happen after a minimum generation depth has been reached.
    /// </summary>
    /// <param name="generation">The current recursion depth.</param>
    /// <returns>True if the room should be preserved.</returns>
    private bool PreserveRoom(int generation)
    {
        if (generation < generationsBeforePreservedRooms)
            return false;

        return UnityEngine.Random.Range(0, 100) < preservedRoomChance;
    }

    /// <summary>
    /// Adds all preserved rooms back into the final room list after recursive division ends.
    /// </summary>
    private void AddPreservedRooms()
    {
        foreach (var room in roomsPreserved)
        {
            finalRooms.Add(room);
        }
        Debug.Log($"Made {finalRooms.Count} rooms");
    }

    #endregion


    #region Room Removal

    /// <summary>
    /// Removes a percentage of the smallest rooms from the final room list.
    /// Rooms are only removed if doing so does not disconnect the remaining layout.
    /// In slow mode, removed rooms are drawn and yielded over time.
    /// </summary>
    private IEnumerator RemoveSmallestRooms()
    {
        List<RectInt> roomsToRemove = new List<RectInt>();
        int smallRoomsToRemove = (int)(finalRooms.Count * deleteRoomPercentage / 100);
        Debug.Log($"Rooms to remove: {smallRoomsToRemove}");
        int removedRooms = 0;
        List<RectInt> roomPool = new List<RectInt>(finalRooms);
        roomPool.Sort((a, b) => (a.width * a.height).CompareTo(b.width * b.height));

        for (int i = 0; i < smallRoomsToRemove; i++)
        {
            bool removed = false;
            while (!removed && roomPool.Count > 0)
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

                    if (generationType == GenerationType.Slow)
                    {
                        yield return new WaitForSeconds(0.01f);
                    }
                }
                else
                    roomPool.Remove(room);
            }
        }

        Debug.Log($"Rooms removed: {removedRooms}");
    }

    /// <summary>
    /// Checks whether a room can be removed without disconnecting the dungeon.
    /// It temporarily removes the room, then performs a breadth-first search over the remaining rooms,
    /// using shared wall intersections as connections.
    /// </summary>
    /// <param name="room">The room being tested for removal.</param>
    /// <returns>True if all remaining rooms stay connected after removing this room.</returns>
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

    #endregion


    #endregion


    #region Door Logic

    //Doors depend on intersections, and walls depend on doors.
    private IEnumerator GenerateDoors()
    {
        FindIntersections();
        yield return StartCoroutine(DecideDoors());
        yield return StartCoroutine(WaitIfKeyBased());
        yield return StartCoroutine(RemoveExtraDoors());
    }


    #region Intersctions

    /// <summary>
    /// Finds all shared wall intersections between every pair of final rooms.
    /// Valid intersections are converted into shared wall lines,
    /// which are later used as possible door placement locations.
    /// </summary>
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

    /// <summary>
    /// Converts a valid room intersection into a shared wall line.
    /// Vertical intersections create vertical wall lines,
    /// horizontal intersections create horizontal wall lines.
    /// One tile is removed from each end to avoid placing doors directly on corners.
    /// </summary>
    /// <param name="intersection">The shared edge between two rooms.</param>
    /// <param name="roomA">The graph node position of the first room.</param>
    /// <param name="roomB">The graph node position of the second room.</param>
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

    /// <summary>
    /// Checks whether an intersection represents a vertical shared wall.
    /// A vertical shared wall has no width but has enough height to place a door.
    /// </summary>
    private bool IsVerticalIntersection(RectInt intersection)
    {
        if (intersection.width == 0 && intersection.height > 1)
            return true;
        return false;
    }

    /// <summary>
    /// Checks whether an intersection represents a horizontal shared wall.
    /// A horizontal shared wall has no height but has enough width to place a door.
    /// </summary>
    private bool IsHorizontalIntersection(RectInt intersection)
    {
        if (intersection.width > 1 && intersection.height == 0)
            return true;
        return false;
    }

    #endregion


    #region Door Generation

    /// <summary>
    /// Places one door on each shared wall line.
    /// Each door is added to the graph as a node connecting two rooms.
    /// In slow mode, created doors are drawn and yielded over time.
    /// </summary>
    private IEnumerator DecideDoors()
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
            if (generationType == GenerationType.Slow)
            {
                yield return new WaitForSeconds(0.02f);
            }

            doorPosition.x -= 0.5f;
            doorPosition.y -= 0.5f;
            doorPosition.z -= 0.5f;

            graph.AddEdge(line.roomA, doorPosition);
            graph.AddEdge(doorPosition, line.roomB);
        }
        Debug.Log($"Made {doors.Count} doors");
    }

    #endregion


    #region Door Removal

    /// <summary>
    /// Removes unnecessary doors while keeping all room nodes connected.
    /// In slow mode, removed doors are drawn and yielded over time.
    /// </summary>
    private IEnumerator RemoveExtraDoors()
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

                if (generationType == GenerationType.Slow)
                {
                    yield return new WaitForSeconds(0.01f);
                }
            }
        }
        Debug.Log($"Removed {doorsRemoved} doors");
    }

    /// <summary>
    /// Converts all final rooms into graph node positions using their center points.
    /// These nodes are used when checking whether removing doors disconnects the dungeon.
    /// </summary>
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

    #endregion


    #endregion


    #region Asset Spawning

    /// <summary>
    /// Clears previously calculated wall and floor positions,
    /// then spawns the dungeon walls and floor tiles.
    /// In the end it spawns the gameplay objects.
    /// </summary>
    private IEnumerator SpawnAssets()
    {
        wallPositions.Clear();
        floorPositions.Clear();

        yield return StartCoroutine(SpawnWalls());
        yield return StartCoroutine(SpawnFloor());

        yield return StartCoroutine(WaitIfKeyBased());
        SpawnGameplayObjects();
    }


    #region Wall Generation

    /// <summary>
    /// Generates the wall tile map and spawns wall prefabs based on 2x2 tile patterns.
    /// In slow mode, spawning yields periodically to keep the process visible.
    /// </summary>
    private IEnumerator SpawnWalls()
    {
        CalculateWallPositions();
        int[,] tileMap = GenerateWallTileMap();
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
                    if (counter >= waitTime)
                    {
                        counter = 0;
                        if (generationType == GenerationType.Slow)
                        {
                            yield return null;
                        }
                    }
                    else
                        counter++;
                }
            }
        }
    }

    /// <summary>
    /// Calculates all wall positions around the borders of final rooms,
    /// excluding positions occupied by doors.
    /// </summary>
    private void CalculateWallPositions()
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

    /// <summary>
    /// Converts the wall position list into a 2D tile map.
    /// A value of 1 means a wall exists at that tile,
    /// while 0 means the tile is empty.
    /// </summary>
    private int[,] GenerateWallTileMap()
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

    /// <summary>
    /// Converts a 2x2 section of the wall tile map into a prefab index.
    /// Each corner contributes one bit, producing values from 0 to 15.
    /// </summary>
    private int GetWallPrefabIndex(int[,] tileMap, int x, int y)
    {
        return tileMap[x, y]
            + tileMap[x, y + 1] * 2
            + tileMap[x + 1, y + 1] * 4
            + tileMap[x + 1, y] * 8;
    }

    #endregion


    #region Floor Generation

    /// <summary>
    /// Spawns floor tiles using flood fill.
    /// In slow mode, spawning yields periodically to spread work over multiple frames.
    /// </summary>
    private IEnumerator SpawnFloor()
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

            if (counter > waitTime * 3)
            {
                if (generationType == GenerationType.Slow)
                {
                    yield return null;
                }
                counter = 0;
            }
            counter++;

            if (passableTiles.Contains(tile) && !stopTiles.Contains(tile))
                playerSpawnPositions.Add(position);
        }
    }

    /// <summary>
    /// Builds a set of all tiles that the floor fill is allowed to pass through.
    /// This includes room interior tiles and door tiles.
    /// </summary>
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

    /// <summary>
    /// Builds a set of wall tiles where the flood fill should stop.
    /// These tiles may still be reached so floor can touch the walls,
    /// but the fill should not continue past them.
    /// </summary>
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

    /// <summary>
    /// Picks a random final room and returns a clamped tile near its center.
    /// This is used as the starting point for floor flood fill.
    /// </summary>
    private Vector2Int GetRandomRoomCenterTile()
    {
        RectInt room = finalRooms[UnityEngine.Random.Range(0, finalRooms.Count)];

        int x = Mathf.RoundToInt(room.center.x);
        int z = Mathf.RoundToInt(room.center.y);

        x = Mathf.Clamp(x, room.xMin + 1, room.xMax - 1);
        z = Mathf.Clamp(z, room.yMin + 1, room.yMax - 1);

        return new Vector2Int(x, z);
    }

    // Stop tiles are allowed to be added to the reached tile list,
    // but the flood fill does not continue expanding from them.
    // This lets the floor reach wall edges without spilling outside the dungeon.
    /// <summary>
    /// Performs a breadth-first flood fill from the starting tile.
    /// The fill can move through passable tiles and can include stop tiles,
    /// but it does not continue expanding from stop tiles.
    /// </summary>
    /// <param name="startTile">The tile where the fill starts.</param>
    /// <param name="passableTiles">Tiles that the fill can move through.</param>
    /// <param name="stopTiles">Tiles that stop further spreading, usually walls.</param>
    /// <returns>All tiles reached by the flood fill.</returns>
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

    #endregion


    #region Gameplay Object Spawning

    /// <summary>
    /// Spawns gameplay-related objects after the dungeon geometry is generated.
    /// This includes the NavMesh object, player, camera, UI, event system,
    /// and the player controller component.
    /// </summary>
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


    #endregion


    #endregion


    #region Debug Drawing

    /// <summary>
    /// Draws debug outlines around all final rooms using the DebugDrawingBatcher.
    /// </summary>
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

    /// <summary>
    /// Visualizes all the nodes in the graph using the DebugDrawingBatcher.
    /// </summary>
    private IEnumerator GraphVisualization()
    {
        if (generationType == GenerationType.Instant || generationType == GenerationType.KeyBased)
            graph.PrintGraph();
        else
            yield return StartCoroutine(graph.SlowPrintGraph());
    }

    #endregion


    #region Utility

    /// <summary>
    /// Randomly shuffles a list in-place using the Fisher-Yates shuffle algorithm.
    /// </summary>
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

    /// <summary>
    /// Returns a random true or false value with equal probability.
    /// </summary>
    private bool RandomBool()
    {
        return UnityEngine.Random.Range(0, 2) == 0;
    }

    public bool ShouldShowNextStepKeySelector()
    {
        return generationType == GenerationType.KeyBased;
    }

    /// <summary>
    /// Checks if it should show the button for moving to the next step.
    /// </summary>
    private bool ShouldShowNextStepButton()
    {
        return Application.isPlaying && generationType == GenerationType.KeyBased && isGenerating;
    }

    /// <summary>
    /// Pauses execution if generation is key based until slected key is pressed.
    /// </summary>
    private IEnumerator WaitIfKeyBased()
    {
        if (generationType != GenerationType.KeyBased)
            yield break;

        yield return null;

        while (!Input.GetKeyDown(executeNextStepKey) && !moveToNextStep)
            yield return null;

        moveToNextStep = false;
    }

    #endregion
}