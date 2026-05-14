using NaughtyAttributes;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.LightTransport;

public class DungeonGeneratorScript : MonoBehaviour
{
    public int wallSize = 1;
    public int minRoomSize = 5;
    public RectInt startRoomParams = new RectInt(0, 0, 100, 100);
    List<RectInt> roomsToDraw = new List<RectInt>();
    List<RectInt> newRooms = new List<RectInt>();
    List<RectInt> roomsToRemove = new List<RectInt>();
    //List<RectInt> DoorsToDraw = new List<RectInt>();
    bool keepDividing = true;
    public bool wait = false;
    public float randomSizeMin = 0.05f;
    public float randomSizeMax = 0.75f;
    public int wallHeight = 3;
    //public int doorSize = 1;

    void Start()
    {
        
        GenerateDungeon();
    }

    [Button]
    void GenerateDungeon() 
    {
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
        AlgorithmsUtils.DebugRectInt(roomsToDraw[0], Color.yellow, 1000f, false, wallHeight);
        //FixDoorsAfterSplits();
        if (wait) StartCoroutine(DrawRooms());
        else
        {
            foreach (var r in roomsToDraw)
            {
                AlgorithmsUtils.DebugRectInt(r, Color.yellow, 1000f, false, wallHeight);
            }
            //DrawDoorsI();
        }
    }

    //private void DrawDoorsI()
    //{
    //    foreach (var r in DoorsToDraw)
    //    {
    //        AlgorithmsUtils.DebugRectInt(r, Color.red, 1000f, false, wallHeight);
    //    }
    //}

    IEnumerator DrawRooms()
    {
        foreach (var r in roomsToDraw)
        {
            AlgorithmsUtils.DebugRectInt(r, Color.yellow, 1000f, false, wallHeight);
            yield return new WaitForSeconds(0.1f);
        }
        //StartCoroutine(DrawDoors());
    }

    //IEnumerator DrawDoors()
    //{
    //    foreach (var r in DoorsToDraw)
    //    {
    //        AlgorithmsUtils.DebugRectInt(r, Color.red, 1000f, false, wallHeight);
    //        yield return new WaitForSeconds(0.1f);
    //    }
    //}

    private bool TrySplit(RectInt room, out RectInt roomA, out RectInt roomB)
    {
        roomA = default ;
        roomB = default;

        if (RandomBool())
        {
            // X
            if (room.width < minRoomSize * 2 + wallSize) return false;

            int usable = room.width - wallSize;                 // CHANGED: total room space after wall
            float minRatio = (float)minRoomSize / usable;       // CHANGED
            float r = UnityEngine.Random.Range(randomSizeMin, randomSizeMax);   // CHANGED
            if (r < minRatio) r = minRatio;                     // CHANGED (clamp)

            int small = Mathf.RoundToInt(usable * r);           // CHANGED
            small = Mathf.Clamp(small, minRoomSize, usable - minRoomSize); // safety
            int big = usable - small;                           // CHANGED

            bool aIsBig = RandomBool();                         // CHANGED

            int aWidth = aIsBig ? big : small;                  // CHANGED
            int bWidth = usable - aWidth;                       // CHANGED (ensures sum is exact)

            roomA = new RectInt(room.x, room.y, aWidth, room.height);
            roomB = new RectInt(room.xMax - bWidth, room.y, bWidth, room.height); // CHANGED (centered wall)

            return true;
        }
        else
        {
            // Y
            if (room.height < minRoomSize * 2 + wallSize) return false;

            int usable = room.height - wallSize;                // CHANGED
            float minRatio = (float)minRoomSize / usable;       // CHANGED
            float r = UnityEngine.Random.Range(randomSizeMin, randomSizeMax);   // CHANGED
            if (r < minRatio) r = minRatio;                     // CHANGED

            int small = Mathf.RoundToInt(usable * r);           // CHANGED
            small = Mathf.Clamp(small, minRoomSize, usable - minRoomSize); // safety
            int big = usable - small;                           // CHANGED

            bool aIsBig = RandomBool();                         // CHANGED

            int aHeight = aIsBig ? big : small;                 // CHANGED
            int bHeight = usable - aHeight;                     // CHANGED

            roomA = new RectInt(room.x, room.y, room.width, aHeight);
            roomB = new RectInt(room.x, room.yMax - bHeight, room.width, bHeight); // CHANGED (centered wall)

            return true;
        }
    }

    public bool IsSplittable(RectInt room, bool splitDirection)
    {
        if (splitDirection)
            if (room.height >= minRoomSize * 2 + wallSize) return true; else return false;
        else if (room.width >= minRoomSize * 2 + wallSize) return true; else return false;
    }

    public bool RandomBool()
    {
        int number = UnityEngine.Random.Range(0, 10000);
        if (number % 2 == 0) return true;
        else return false;
    }

    //private bool HasRoomOnBothSides(RectInt door)
    //{
    //    bool isVerticalDoor = door.width == wallSize;   // X-split door: spans wall in X, slides in Y
    //    bool isHorizontalDoor = door.height == wallSize; // Y-split door: spans wall in Y, slides in X

    //    if (isVerticalDoor)
    //    {
    //        // Door occupies the wall gap at x = door.x .. door.x+wallSize
    //        // Room on left should touch door.x, room on right should touch door.x+wallSize
    //        int leftTouchX = door.x;
    //        int rightTouchX = door.x + wallSize;

    //        bool hasLeft = false, hasRight = false;

    //        for (int i = 0; i < roomsToDraw.Count; i++)
    //        {
    //            RectInt r = roomsToDraw[i];

    //            // Overlap test on Y between room and door opening
    //            bool overlapY = r.yMin < door.yMax && r.yMax > door.yMin;
    //            if (!overlapY) continue;

    //            if (r.xMax == leftTouchX) hasLeft = true;
    //            if (r.xMin == rightTouchX) hasRight = true;

    //            if (hasLeft && hasRight) return true;
    //        }

    //        return false;
    //    }
    //    else if (isHorizontalDoor)
    //    {
    //        // Door occupies wall gap at y = door.y .. door.y+wallSize
    //        int bottomTouchY = door.y;
    //        int topTouchY = door.y + wallSize;

    //        bool hasBottom = false, hasTop = false;

    //        for (int i = 0; i < roomsToDraw.Count; i++)
    //        {
    //            RectInt r = roomsToDraw[i];

    //            bool overlapX = r.xMin < door.xMax && r.xMax > door.xMin;
    //            if (!overlapX) continue;

    //            if (r.yMax == bottomTouchY) hasBottom = true;
    //            if (r.yMin == topTouchY) hasTop = true;

    //            if (hasBottom && hasTop) return true;
    //        }

    //        return false;
    //    }

    //    // Unknown door orientation -> treat as invalid
    //    return false;
    //}

    //private void FixDoorsAfterSplits()
    //{
    //    for (int i = DoorsToDraw.Count - 1; i >= 0; i--)
    //    {
    //        RectInt d = DoorsToDraw[i];

    //        if (HasRoomOnBothSides(d))
    //            continue;

    //        // Try nudging along the "sliding" axis
    //        RectInt dPlus = d;
    //        RectInt dMinus = d;

    //        if (d.width == wallSize)
    //        {
    //            // slides along Y
    //            dPlus.y += doorSize;
    //            dMinus.y -= doorSize;
    //        }
    //        else if (d.height == wallSize)
    //        {
    //            // slides along X
    //            dPlus.x += doorSize;
    //            dMinus.x -= doorSize;
    //        }
    //        else
    //        {
    //            DoorsToDraw.RemoveAt(i);
    //            continue;
    //        }

    //        if (HasRoomOnBothSides(dPlus))
    //        {
    //            DoorsToDraw[i] = dPlus;
    //            continue;
    //        }

    //        if (HasRoomOnBothSides(dMinus))
    //        {
    //            DoorsToDraw[i] = dMinus;
    //            continue;
    //        }

    //        // Still bad -> delete it
    //        DoorsToDraw.RemoveAt(i);
    //    }
    //}
}
