using NaughtyAttributes;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

public class DungeonGeneratorScript : MonoBehaviour
{
    [Header("Generation Settings")]
    [SerializeField] private int minRoomSize = 6;
    [SerializeField] private RectInt startRoomParams = new RectInt(0, 0, 100, 100);
    [SerializeField] private float randomSizeMin = 0.05f;
    [SerializeField] private float randomSizeMax = 0.75f;
    [SerializeField] private int generationsBeforePreservedRooms = 10;
    [SerializeField] private int preservedRoomChance = 10;

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

    private List<RectInt> roomsPreserved = new List<RectInt>();
    private List<RectInt> finalRooms = new List<RectInt>();
    private List<RectInt> newRooms = new List<RectInt>();
    private List<RectInt> roomsToRemove = new List<RectInt>();
    private bool keepDividing = true;
    private List<Vector3> wallPositions = new List<Vector3>();
    private List<Vector3> doors = new List<Vector3>();
    private List<Vector3> floorPositions = new List<Vector3>();
    private NavMeshSurface navMeshSurface = null;
    private List<Vector3Line> sharedWallLines = new List<Vector3Line>();
    private struct Vector3Line
    {
        public Vector3 start;
        public Vector3 end;

        public Vector3Line(Vector3 start, Vector3 end)
        {
            this.start = start;
            this.end = end;
        }
    }

    void Start()
    {
        SeedPick();
        if (wait)
            StartCoroutine(SlowGenerateDungeon());
        else
            GenerateDungeon();
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
    [Button]
    private void GenerateDungeon()
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
        
        AddBaseRoom();

        DivideRooms();

        AddPreservedRooms();

        RemoveSmallestRooms();

        //Doors depend on intersections, and walls depend on doors.
        FindIntersections();
        SpawnAssets();

        SpawnGameplayObjects();

        DrawRooms();
    }

    private void AddBaseRoom() 
    {
        finalRooms.Clear();

        RectInt baseRoom = startRoomParams;
        finalRooms.Add(baseRoom);
    }

    private void DivideRooms() 
    {
        keepDividing = true;
        int generation = 0;
        while (keepDividing)
        {
            keepDividing = false;
            generation++;
            foreach (var room in finalRooms)
            {
                if (TrySplit(room, out RectInt roomA, out RectInt roomB))
                {
                    bool preserveRoom = PreserveRoom(generation);

                    if (preserveRoom)
                    {
                        QueuePreservedRoom(room);
                        continue;
                    }

                    QueueRoomSplit(room, roomA, roomB);
                }
            }
            ApplyQueuedRoomChanges();
        }
    }

    private bool TrySplit(RectInt room, out RectInt roomA, out RectInt roomB)
    {
        roomA = default ;
        roomB = default;

        if (RandomBool())
        {
            // X
            if (room.width < minRoomSize * 2)
                return false;

            int aWidth = GetRandomSplitSize(room.width);
            int bWidth = room.width - aWidth;

            roomA = new RectInt(room.x, room.y, aWidth, room.height);
            roomB = new RectInt(room.xMax - bWidth, room.y, bWidth, room.height);

            return true;
        }
        else
        {
            // Y
            if (room.height < minRoomSize * 2)
                return false;

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

    private void QueueRoomSplit(RectInt oldRoom, RectInt roomA, RectInt roomB)
    {
        roomsToRemove.Add(oldRoom);
        newRooms.Add(roomA);
        newRooms.Add(roomB);
        keepDividing = true;
    }

    private void QueuePreservedRoom(RectInt room)
    {
        roomsPreserved.Add(room);
        roomsToRemove.Add(room);
        keepDividing = true;
    }

    private void ApplyQueuedRoomChanges()
    {
        foreach (var room in roomsToRemove)
            finalRooms.Remove(room);

        roomsToRemove.Clear();

        foreach (var room in newRooms)
            finalRooms.Add(room);

        newRooms.Clear();
    }

    private void AddPreservedRooms()
    {
        foreach (var room in roomsPreserved)
        {
            finalRooms.Add(room);
        }
    }

    private void RemoveSmallestRooms()
    {
        roomsToRemove.Clear();
        int smallRoomsToRemove = (int)(finalRooms.Count / 10);
        for (int i = 0; i < smallRoomsToRemove; i++)
        {
            RectInt smallestRoom = finalRooms[0];
            float size = smallestRoom.width * smallestRoom.height;
            foreach (var room in finalRooms)
            {
                float currentRoomSize = room.width * room.height;
                if (size > currentRoomSize)
                {
                    size = currentRoomSize;
                }
            }
            bool removed = false;
            foreach (var room in finalRooms)
            {
                float currentRoomSize = room.width * room.height;
                if (!removed && size == currentRoomSize)
                {
                    roomsToRemove.Add(room);
                    removed = true;
                }
            }
            foreach (var room in roomsToRemove)
            {
                finalRooms.Remove(room);
            }
        }
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
                    AddSharedWallLine(intersection);
                }
            }
        }
        DecideDoors();
    }

    private void AddSharedWallLine(RectInt intersection)
    {
        if (IsVerticalIntersection(intersection))
        {
            Vector3 start = new Vector3(intersection.x, 0.5f, intersection.y + 1);
            Vector3 end = new Vector3(intersection.x, 0.5f, intersection.yMax - 1);

            sharedWallLines.Add(new Vector3Line(start, end));
        }
        else if (IsHorizontalIntersection(intersection))
        {
            Vector3 start = new Vector3(intersection.x + 1, 0.5f, intersection.y);
            Vector3 end = new Vector3(intersection.xMax - 1, 0.5f, intersection.y);

            sharedWallLines.Add(new Vector3Line(start, end));
        }
    }

    private bool IsVerticalIntersection(RectInt intersection) 
    {
        if(intersection.width == 0 && intersection.height > 1)
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
            Vector3 doorPosition = Vector3.Lerp(line.start, line.end, UnityEngine.Random.value);
            doorPosition.x = Mathf.Round(doorPosition.x);
            doorPosition.y = 0.5f;
            doorPosition.z = Mathf.Round(doorPosition.z);

            doors.Add(doorPosition);
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
        GameObject floorParent = new GameObject("Floor");
        Transform floorTransform = floorParent.transform;

        foreach (var room in finalRooms) 
        {
            for (int i = 0; i < room.height; i++) 
            {
                for (int j = 0; j < room.width; j++) 
                {
                    Vector3 position = new Vector3(room.xMin + j, 0f, room.yMin + i);
                    if (position.x != 0 && position.z != 0) 
                    {
                        floorPrefab.transform.position = new Vector3(position.x, 0f, position.z);

                        floorPositions.Add(position);

                        Instantiate(floorPrefab, floorTransform);
                    }
                }
            }
        }
    }

    private void SpawnGameplayObjects()
    {
        GameObject navmesh = Instantiate(navMesh);

        navMeshSurface = navmesh.GetComponent<NavMeshSurface>();
        navMeshSurface.BuildNavMesh();

        int playerPosition = UnityEngine.Random.Range(0, floorPositions.Count );
        GameObject player = Instantiate(playerPrefab);
        player.transform.position = floorPositions[playerPosition];

        Instantiate(cameraPrefab);
        player.AddComponent<PlayerController>();

        Instantiate(uiPrefab);
        Instantiate(eventSystem);
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
    [Button]
    private IEnumerator SlowGenerateDungeon() 
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();

        AddBaseRoom();

        yield return StartCoroutine(SlowDivideRooms());

        AddPreservedRooms();

        yield return StartCoroutine(SlowRemoveSmallestRooms());

        //Doors depend on intersections, and walls depend on doors.
        yield return StartCoroutine(SlowFindIntersections());
        yield return StartCoroutine(SlowSpawnAssets());

        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
        
        SpawnGameplayObjects();
    }

    private IEnumerator SlowDivideRooms()
    {
        keepDividing = true;
        int generation = 0;
        while (keepDividing)
        {
            keepDividing = false;
            foreach (var room in finalRooms)
            {
                generation++;
                if (TrySplit(room, out RectInt roomA, out RectInt roomB))
                {
                    bool preserveRoom = PreserveRoom(generation);
                    
                    if (preserveRoom)
                    {
                        DebugDrawingBatcher.GetInstance().BatchCall(() =>
                        {
                            AlgorithmsUtils.DebugRectInt(room, Color.blue);
                        });
                        yield return new WaitForSeconds(0.3f);

                        QueuePreservedRoom(room);
                        continue;
                    }

                    DebugDrawingBatcher.GetInstance().BatchCall(() =>
                    {
                        AlgorithmsUtils.DebugRectInt(room, Color.green);
                    });
                    yield return new WaitForSeconds(0.3f);

                    DebugDrawingBatcher.GetInstance().BatchCall(() =>
                    {
                        AlgorithmsUtils.DebugRectInt(roomA, Color.yellow);
                        AlgorithmsUtils.DebugRectInt(roomB, Color.yellow);
                    });
                    yield return new WaitForSeconds(0.1f);

                    QueueRoomSplit(room, roomA, roomB);
                }
            }
            ApplyQueuedRoomChanges();
        }
    }

    private IEnumerator SlowRemoveSmallestRooms()
    {
        roomsToRemove.Clear();
        int smallRoomsToRemove = (int)(finalRooms.Count / 10);
        for (int i = 0; i < smallRoomsToRemove; i++)
        {
            RectInt smallestRoom = finalRooms[0];
            float size = smallestRoom.width * smallestRoom.height;
            foreach (var room in finalRooms)
            {
                float currentRoomSize = room.width * room.height;
                if (size > currentRoomSize)
                {
                    size = currentRoomSize;
                }
            }
            bool removed = false;
            foreach (var room in finalRooms)
            {
                float currentRoomSize = room.width * room.height;
                if (!removed && size == currentRoomSize)
                {
                    DebugDrawingBatcher.GetInstance().BatchCall(() =>
                    {
                        AlgorithmsUtils.DebugRectInt(room, Color.red);
                    });
                    roomsToRemove.Add(room);
                    removed = true;
                    yield return new WaitForSeconds(0.5f);
                }
            }
            foreach (var room in roomsToRemove)
            {
                finalRooms.Remove(room);
            }
        }
    }

    private IEnumerator SlowFindIntersections()
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
                    AddSharedWallLine(intersection);
                }
            }
        }
        yield return StartCoroutine(SlowDecideDoors());
    }

    private IEnumerator SlowDecideDoors()
    {
        doors.Clear();
        foreach (var line in sharedWallLines)
        {
            Vector3 doorPosition = Vector3.Lerp(line.start, line.end, UnityEngine.Random.value);
            doorPosition.x = Mathf.Round(doorPosition.x);
            doorPosition.y = 0.5f;
            doorPosition.z = Mathf.Round(doorPosition.z);

            doors.Add(doorPosition);

            RectInt doorSquare = new RectInt((int)doorPosition.x - 1, (int)doorPosition.z - 1, 1, 1);
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(doorSquare, Color.cyan);
            });
            yield return new WaitForSeconds(0.02f);
        }
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
                    yield return new WaitForSeconds(0.001f);
                }
            }
        }
    }

    private IEnumerator SlowSpawnFloor()
    {
        GameObject floorParent = new GameObject("Floor");
        Transform floorTransform = floorParent.transform;

        foreach (var room in finalRooms)
        {
            for (int i = 0; i < room.height; i++)
            {
                for (int j = 0; j < room.width; j++)
                {
                    Vector3 position = new Vector3(room.xMin + j, 0f, room.yMin + i);
                    if (position.x != 0 && position.z != 0)
                    {
                        floorPrefab.transform.position = new Vector3(position.x, 0f, position.z);

                        floorPositions.Add(position);

                        Instantiate(floorPrefab, floorTransform);
                    }
                }
            }
            yield return new WaitForSeconds(0.1f);
        }
    }
}
