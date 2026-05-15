using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.LightTransport;

public class DungeonGeneratorScript : MonoBehaviour
{
    [SerializeField] private int minRoomSize = 5;
    [SerializeField] private RectInt startRoomParams = new RectInt(0, 0, 100, 100);
    [SerializeField] private bool wait = false;
    [SerializeField] private float randomSizeMin = 0.05f;
    [SerializeField] private float randomSizeMax = 0.75f;
    [SerializeField] private int wallHeight = 3;
    [SerializeField] private bool spawnAssets = false;
    [SerializeField] private GameObject wallPrefab;
    [SerializeField] private GameObject floorPrefab;
    [SerializeField] private GameObject cellingPrefab;
    private List<RectInt> roomsToDraw = new List<RectInt>();
    private List<RectInt> newRooms = new List<RectInt>();
    private List<RectInt> roomsToRemove = new List<RectInt>();
    private bool keepDividing = true;
    private List<Vector3> takenPositions = new List<Vector3>();
    private List<RectInt> Doors = new List<RectInt>();

    void Start()
    {
        GenerateDungeon();
    }

    [Button]
    void GenerateDungeon()
    {
        DebugDrawingBatcher.GetInstance().ClearAllBatchedCalls();
        roomsToDraw.Clear();
        RectInt baseRoom = startRoomParams;
        roomsToDraw.Add(baseRoom);
        keepDividing = true;
        while (keepDividing)
        {
            keepDividing = false;
            foreach (var room in roomsToDraw)
            {
                if (TrySplit(room, out RectInt roomA, out RectInt roomB))
                {
                    roomsToRemove.Add(room);
                    newRooms.Add(roomA);
                    newRooms.Add(roomB);
                    keepDividing = true;
                }
            }
            foreach (var room in roomsToRemove) roomsToDraw.Remove(room);
            roomsToRemove.Clear();

            foreach (var room in newRooms) roomsToDraw.Add(room);
            newRooms.Clear();
        }
        if (wait) StartCoroutine(DrawRooms());
        else
        {
            foreach (var r in roomsToDraw)
            {
                DebugDrawingBatcher.GetInstance().BatchCall(() =>
                {
                    AlgorithmsUtils.DebugRectInt(r, Color.yellow);
                });
            }
            if (spawnAssets)
            {
                SpawnAssets();
            }
        }
    }

    private void SpawnAssets() 
    {
        takenPositions.Clear();
        SpawnWalls();
        SpawnFloorAndCelling();
    }

    private void SpawnFloorAndCelling() 
    {
        GameObject FloorParent = new GameObject();
        FloorParent.name = "Floor";
        Transform floorTransform = FloorParent.transform;

        GameObject CellingParent = new GameObject();
        CellingParent.name = "Celling";
        Transform cellingTransform = CellingParent.transform;

        for (int i = 0; i < startRoomParams.width; i++) 
        {
            for (int j = 0; j < startRoomParams.height; j++)
            {
                if (!takenPositions.Contains(new Vector3(i, 0.5f, j))) 
                {
                    floorPrefab.transform.position = new Vector3(i, 0f, j);
                    cellingPrefab.transform.position = new Vector3(i, wallHeight, j);

                    Instantiate(floorPrefab, floorTransform);
                    Instantiate(cellingPrefab, cellingTransform);
                }
            }
        }
    }

    private void SpawnWalls() 
    {
        GameObject wallsParent = new GameObject();
        wallsParent.name = "Walls";
        Transform transform = wallsParent.transform;
        foreach (var r in roomsToDraw)
        {
            for (int i = 0; i <= r.width; i++)
            {
                for (int j = 0; j <= r.height; j++)
                {
                    if (i == 0 || i == r.width || j == 0 || j == r.height)
                    {
                        takenPositions.Add(new Vector3(r.xMin + i, 0.5f, r.yMin + j));
                    }
                }
            }
        }
        for (int i = 0; i < wallHeight; i++) 
        {
            foreach (var position in takenPositions)
            {
                Vector3 spawnPosition = position;
                spawnPosition.y = i + 0.5f;
                wallPrefab.transform.position = spawnPosition;
                Instantiate(wallPrefab, transform);
            }
        }
    }

    IEnumerator DrawRooms()
    {
        foreach (var r in roomsToDraw)
        {
            DebugDrawingBatcher.GetInstance().BatchCall(() =>
            {
                AlgorithmsUtils.DebugRectInt(r, Color.yellow);
            });
            yield return new WaitForSeconds(0.1f);
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

    public bool IsSplittable(RectInt room, bool splitDirection)
    {
        if (splitDirection)
            if (room.height >= minRoomSize * 2) return true; else return false;
        else if (room.width >= minRoomSize * 2) return true; else return false;
    }

    public bool RandomBool()
    {
        int number = UnityEngine.Random.Range(0, 10000);
        if (number % 2 == 0) return true;
        else return false;
    }
}
