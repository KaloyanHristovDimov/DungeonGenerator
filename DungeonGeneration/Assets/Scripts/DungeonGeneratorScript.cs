using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.AI.Navigation;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.LightTransport;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;
using static UnityEngine.Rendering.DebugUI.Table;

public class DungeonGeneratorScript : MonoBehaviour
{
    [SerializeField] private int minRoomSize = 6;
    [SerializeField] private RectInt startRoomParams = new RectInt(0, 0, 100, 100);
    [SerializeField] private bool wait = false;
    [SerializeField] private float randomSizeMin = 0.05f;
    [SerializeField] private float randomSizeMax = 0.75f;
    [SerializeField] private int wallHeight = 2;
    [SerializeField] private List<GameObject> wallPrefabs;
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject cellingPrefab;
    //[SerializeField] private GameObject doorPrefab;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject cameraPrefab;
    [SerializeField] private GameObject navMesh;
    [SerializeField] private GameObject UIPrefab;
    [SerializeField] private GameObject EventSystem;
    [SerializeField] private int generationsBeforePreservedRooms = 10;
    [SerializeField] private int preservedRoomChance = 10;
    private List<RectInt> roomsPreserved = new List<RectInt>();
    private List<RectInt> roomsToDraw = new List<RectInt>();
    private List<RectInt> newRooms = new List<RectInt>();
    private List<RectInt> roomsToRemove = new List<RectInt>();
    private bool keepDividing = true;
    private List<Vector3> takenPositions = new List<Vector3>();
    private List<Vector3> doors = new List<Vector3>();
    private List<Vector3> floorPositions = new List<Vector3>();
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
    private List<Vector3Line> vector3Lines = new List<Vector3Line>();
    private NavMeshSurface navMeshSurface = null;

    void Start()
    {
        if (wait)
            StartCoroutine(SlowGenerateDungeon());
        else
            GenerateDungeon();
    }

    private IEnumerator SlowGenerateDungeon() 
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();

        AddBaseRoom();

        yield return StartCoroutine(SlowDivideRooms());

        AddPreservedRooms();

        SlowRemoveSmallestRooms();

        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();

        FindIntersections();

        yield return StartCoroutine(SlowSpawnAssets());

        SpawnNavMeshAndPlayer();
    }

    private IEnumerator SlowDivideRooms() 
    {
        keepDividing = true;
        int generation = 0;
        while (keepDividing)
        {
            //DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
            keepDividing = false;
            foreach (var room in roomsToDraw)
            {
                generation++;
                if (TrySplit(room, out RectInt roomA, out RectInt roomB))
                {
                    bool preserveRoom = false;
                    if (generation >= generationsBeforePreservedRooms)
                    {
                        List<int> ints = new List<int>();
                        for (int i = 0; i < preservedRoomChance; i++) 
                        {
                            int number = UnityEngine.Random.Range(0, 100);
                            ints.Add(number);
                        }
                        int numberToPreserve = UnityEngine.Random.Range(0, 100);
                        foreach (int i in ints)
                        {
                            if (i == numberToPreserve) 
                            {
                                preserveRoom = true;
                            }
                        }
                    }
                    if (!preserveRoom)
                    {
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
                        roomsToRemove.Add(room);
                        newRooms.Add(roomA);
                        newRooms.Add(roomB);
                        keepDividing = true;
                    }
                    else 
                    {
                        DebugDrawingBatcher.GetInstance().BatchCall(() =>
                        {
                            AlgorithmsUtils.DebugRectInt(room, Color.blue);
                        });
                        yield return new WaitForSeconds(0.3f);
                        roomsPreserved.Add(room);
                        roomsToRemove.Add(room);
                        keepDividing = true;
                    }
                   
                }
            }
        foreach (var room in roomsToRemove) roomsToDraw.Remove(room);
        roomsToRemove.Clear();

        foreach (var room in newRooms) roomsToDraw.Add(room);
        newRooms.Clear();
    }
}

    private void AddPreservedRooms() 
    {
        foreach (var room in roomsPreserved) 
        {
            roomsToDraw.Add(room);
        }
    }

    private IEnumerator SlowRemoveSmallestRooms()
    {
        roomsToRemove.Clear();
        int smallRoomsToRemove = (int)(roomsToDraw.Count / 10);
        for (int i = 0; i < smallRoomsToRemove; i++)
        {
            RectInt smallestRoom = roomsToDraw[0];
            float size = smallestRoom.width * smallestRoom.height;
            foreach (var room in roomsToDraw)
            {
                float currentRoomSize = room.width * room.height;
                if (size > currentRoomSize)
                {
                    size = currentRoomSize;
                }
            }
            bool removed = false;
            foreach (var room in roomsToDraw)
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
                roomsToDraw.Remove(room);
            }
        }
    }

    private void RemoveSmallestRooms() 
    {
        roomsToRemove.Clear();
        int smallRoomsToRemove = (int)(roomsToDraw.Count / 10);
        for (int i = 0; i < smallRoomsToRemove; i++) 
        {
            RectInt smallestRoom = roomsToDraw[0];
            float size = smallestRoom.width * smallestRoom.height;
            foreach (var room in roomsToDraw) 
            {
                float currentRoomSize = room.width * room.height;
                if (size > currentRoomSize) 
                {
                    size = currentRoomSize;
                }
            }
            bool removed = false;
            foreach (var room in roomsToDraw) 
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
                roomsToDraw.Remove(room);
            }
        }
    }

    private IEnumerator SlowSpawnAssets()
    {
        takenPositions.Clear();
        yield return StartCoroutine(SlowSpawnWalls());
        yield return StartCoroutine(SlowSpawnFloorAndCelling());
    }

    private IEnumerator SlowSpawnFloorAndCelling()
    {
        GameObject FloorParent = new GameObject();
        FloorParent.name = "Floor";
        Transform floorTransform = FloorParent.transform;

        GameObject CellingParent = new GameObject();
        CellingParent.name = "Celling";
        Transform cellingTransform = CellingParent.transform;

        foreach (var room in roomsToDraw)
        {
            for (int i = 0; i < room.height; i++)
            {
                for (int j = 0; j < room.width; j++)
                {
                    Vector3 position = new Vector3(room.xMin + j, 0f, room.yMin + i);
                    floorPrefab.transform.position = new Vector3(position.x, 0f, position.z);
                    cellingPrefab.transform.position = new Vector3(position.x, wallHeight, position.z);

                    floorPositions.Add(position);

                    Instantiate(floorPrefab, floorTransform);
                    Instantiate(cellingPrefab, cellingTransform);
                }
            }
            yield return new WaitForSeconds(0.1f);
        }
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
                int prefabNumber = (tileMap[i, j] * (int)Math.Pow(2, 0))
                    + (tileMap[i, j + 1] * (int)Math.Pow(2, 1))
                    + (tileMap[i + 1, j + 1] * (int)Math.Pow(2, 2))
                    + (tileMap[i + 1, j] * (int)Math.Pow(2, 3));
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

    [Button]
    private void GenerateDungeon()
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
        
        AddBaseRoom();

        DivideRooms();

        AddPreservedRooms();

        RemoveSmallestRooms();

        FindIntersections();
        SpawnAssets();

        SpawnNavMeshAndPlayer();

        DrawRooms();
    }

    private void AddBaseRoom() 
    {
        roomsToDraw.Clear();

        RectInt baseRoom = startRoomParams;
        roomsToDraw.Add(baseRoom);
    }

    private void DivideRooms() 
    {
        keepDividing = true;
        int generation = 0;
        while (keepDividing)
        {
            keepDividing = false;
            generation++;
            foreach (var room in roomsToDraw)
            {
                if (TrySplit(room, out RectInt roomA, out RectInt roomB))
                {
                    bool preserveRoom = false;
                    if (generation >= generationsBeforePreservedRooms)
                    {
                        List<int> ints = new List<int>();
                        for (int i = 0; i < preservedRoomChance; i++)
                        {
                            int number = UnityEngine.Random.Range(0, 100);
                            ints.Add(number);
                        }
                        int numberToPreserve = UnityEngine.Random.Range(0, 100);
                        foreach (int i in ints)
                        {
                            if (i == numberToPreserve)
                            {
                                preserveRoom = true;
                            }
                        }
                    }

                    if (!preserveRoom)
                    {
                        roomsToRemove.Add(room);
                        newRooms.Add(roomA);
                        newRooms.Add(roomB);
                        keepDividing = true;
                    }
                    else 
                    {
                        roomsPreserved.Add(room);
                        roomsToRemove.Add(room);
                        keepDividing = true;
                    }
                }
            }
            foreach (var room in roomsToRemove) roomsToDraw.Remove(room);
            roomsToRemove.Clear();

            foreach (var room in newRooms) roomsToDraw.Add(room);
            newRooms.Clear();
        }
    }

    private void DrawRooms()
    {
        foreach (var r in roomsToDraw)
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(r, Color.yellow);
            });
        }
    }

    private void FindIntersections()
    {
        vector3Lines.Clear();
        for (int i = 0; i < roomsToDraw.Count; i++)
        {
            for (int j = i + 1; j < roomsToDraw.Count; j++)
            {
                RectInt roomA = roomsToDraw[i];
                RectInt roomB = roomsToDraw[j];
                if (roomB != roomA)
                {
                    RectInt intersection = AlgorithmsUtils.Intersect(roomB, roomA);
                    if (intersection.width == 0 && intersection.height > 1)
                    {
                        Vector3 start = new Vector3(intersection.x, 0.5f, intersection.y+1);
                        Vector3 end = new Vector3(intersection.x, 0.5f, intersection.yMax-1);
                        Vector3Line intersectionLine = new Vector3Line(start, end);
                        vector3Lines.Add(intersectionLine);
                    }
                    else if (intersection.width > 1 && intersection.height == 0)
                    {
                        Vector3 start = new Vector3(intersection.x+1, 0.5f, intersection.y);
                        Vector3 end = new Vector3(intersection.xMax-1, 0.5f, intersection.y);
                        Vector3Line intersectionLine = new Vector3Line(start, end);
                        vector3Lines.Add(intersectionLine);
                    }
                    
                }
            }
        }
        DecideDoors();
    }

    private void DecideDoors() 
    {
        doors.Clear();
        foreach (var line in vector3Lines) 
        {
            Vector3 doorPosition = Vector3.Lerp(line.start, line.end, UnityEngine.Random.value);
            Vector3 mat = line.start - line.end;
            doorPosition.x = Mathf.Round(doorPosition.x);
            doorPosition.y = 0.5f;
            doorPosition.z = Mathf.Round(doorPosition.z);

            doors.Add(doorPosition);
            //doors.Add(new Vector3(UnityEngine.Random.Range(line.start.x, line.end.x), 0f, UnityEngine.Random.Range(line.start.y, line.end.y)));
        }
    }

    private void SpawnAssets() 
    {
        takenPositions.Clear();
        //SpawnDoors();
        SpawnWalls();
        SpawnFloorAndCelling();
    }

    //private void SpawnDoors() 
    //{
    //    GameObject doorParent = new GameObject();
    //    doorParent.name = "Doors";
    //    Transform transform = doorParent.transform;

    //    foreach (var door in doors) 
    //    {
    //        doorPrefab.transform.position = door;
    //        Instantiate(doorPrefab, transform);
    //    }
    //}

    private void SpawnFloorAndCelling() 
    {
        GameObject FloorParent = new GameObject();
        FloorParent.name = "Floor";
        Transform floorTransform = FloorParent.transform;

        GameObject CellingParent = new GameObject();
        CellingParent.name = "Celling";
        Transform cellingTransform = CellingParent.transform;

        foreach (var room in roomsToDraw) 
        {
            for (int i = 0; i < room.height; i++) 
            {
                for (int j = 0; j < room.width; j++) 
                {
                    Vector3 position = new Vector3(room.xMin + j, 0f, room.yMin + i);
                    floorPrefab.transform.position = new Vector3(position.x, 0f, position.z);
                    cellingPrefab.transform.position = new Vector3(position.x, wallHeight, position.z);

                    floorPositions.Add(position);

                    Instantiate(floorPrefab, floorTransform);
                    Instantiate(cellingPrefab, cellingTransform);
                }
            }
        }
    }

    private void SetTakenPositions() 
    {
        foreach (var r in roomsToDraw)
        {
            for (int i = 0; i <= r.width; i++)
            {
                for (int j = 0; j <= r.height; j++)
                {
                    if ((i == 0 || i == r.width || j == 0 || j == r.height) && !doors.Contains(new Vector3(r.xMin + i, 0.5f, r.yMin + j)))
                    {
                        takenPositions.Add(new Vector3(r.xMin + i, 0.5f, r.yMin + j));
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

        foreach (Vector3 position in takenPositions)
        {
            tileMap[(int)(position.x - 0.5f), (int)(position.z - 0.5f)] = 1;
        }

        return tileMap;
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
                int prefabNumber = (tileMap[i, j] * (int)Math.Pow(2, 0))
                    + (tileMap[i, j + 1] * (int)Math.Pow(2, 1))
                    + (tileMap[i + 1, j + 1] * (int)Math.Pow(2, 2))
                    + (tileMap[i + 1, j] * (int)Math.Pow(2, 3));
                if (prefabNumber != 0)
                {
                    GameObject wallToInstantiate = wallPrefabs[prefabNumber];
                    wallToInstantiate.transform.position = new Vector3(i + 1f, 0f, j + 1f);
                    Instantiate(wallToInstantiate, wallsParent.transform);
                }
            }
        }
    }

    private bool TrySplit(RectInt room, out RectInt roomA, out RectInt roomB)
    {
        roomA = default ;
        roomB = default;

        if (RandomBool())
        {
            // X
            if (room.width < minRoomSize * 2) return false;

            int usable = room.width;                 
            float minRatio = (float)minRoomSize / usable;      
            float r = UnityEngine.Random.Range(randomSizeMin, randomSizeMax);   
            if (r < minRatio) r = minRatio;                    

            int small = Mathf.RoundToInt(usable * r);          
            small = Mathf.Clamp(small, minRoomSize, usable - minRoomSize); 
            int big = usable - small;                           

            bool aIsBig = RandomBool();                        

            int aWidth = aIsBig ? big : small;                 
            int bWidth = usable - aWidth;                       

            roomA = new RectInt(room.x, room.y, aWidth, room.height);
            roomB = new RectInt(room.xMax - bWidth, room.y, bWidth, room.height); 

            return true;
        }
        else
        {
            // Y
            if (room.height < minRoomSize * 2) return false;

            int usable = room.height;               
            float minRatio = (float)minRoomSize / usable;      
            float r = UnityEngine.Random.Range(randomSizeMin, randomSizeMax);  
            if (r < minRatio) r = minRatio;                    

            int small = Mathf.RoundToInt(usable * r);          
            small = Mathf.Clamp(small, minRoomSize, usable - minRoomSize); 
            int big = usable - small;                         

            bool aIsBig = RandomBool();                        

            int aHeight = aIsBig ? big : small;              
            int bHeight = usable - aHeight;                  

            roomA = new RectInt(room.x, room.y, room.width, aHeight);
            roomB = new RectInt(room.x, room.yMax - bHeight, room.width, bHeight);

            return true;
        }
    }

    private bool RandomBool()
    {
        int number = UnityEngine.Random.Range(0, 10000);
        if (number % 2 == 0) return true;
        else return false;
    }

    private void SpawnNavMeshAndPlayer() 
    {
        GameObject navmesh = Instantiate(navMesh);
        navMeshSurface = navmesh.GetComponent<NavMeshSurface>();
        navMeshSurface.BuildNavMesh();
        int playerPosition = UnityEngine.Random.Range(0, (floorPositions.Count-1));
        GameObject player = Instantiate(playerPrefab);
        player.transform.position = floorPositions[playerPosition];
        GameObject camera = Instantiate(cameraPrefab);
        player.AddComponent<PlayerController>();
        NavMeshAgent navMeshAgent = player.GetComponent<NavMeshAgent>();
        GameObject UI = Instantiate(UIPrefab);
        GameObject eventSystem = Instantiate(EventSystem);
    }
}
