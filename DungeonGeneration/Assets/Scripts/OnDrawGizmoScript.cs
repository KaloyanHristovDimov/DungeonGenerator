using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class OnDrawGizmoScript : MonoBehaviour
{
    public int wallWidth = 2;
    public int minRoomSize = 10;
    public RectInt startRoomParams = new RectInt(0, 0, 100, 100);
    List<RectInt> roomsToDraw = new List<RectInt>();
    List<RectInt> newRooms = new List<RectInt>();
    List<RectInt> roomsToRemove = new List<RectInt>();
    bool keepDividing = true;
    public bool restart = false;

    private void OnDrawGizmos()
    {
        RectInt baseRoom = startRoomParams;
        roomsToDraw.Add(baseRoom);
        while (keepDividing)
        {
            foreach (var room in roomsToDraw)
            {
                keepDividing = false;
                if (IsSplittable(room))
                {
                    if (RandomBool()) VerticalSplit(room);
                    else HorizontalSplit(room);
                    keepDividing = true;
                }
            }
            foreach (var room in roomsToRemove)
            {
                roomsToDraw.Remove(room);
                Debug.Log("Removed room");
            }
            foreach (var room in newRooms)
            {
                roomsToDraw.Add(room);
                Debug.Log("Added room");
            }
        }
        AlgorithmsUtils.DebugRectInt(baseRoom, Color.yellow, 1000f);
        //StartCoroutine(DrawRooms());
        foreach (var r in roomsToDraw)
        {
            AlgorithmsUtils.DebugRectInt(r, Color.yellow, 1000f);
        }

        if (restart) 
        {
            roomsToDraw.Clear();
            newRooms.Clear();
            roomsToRemove.Clear();

            keepDividing = true;

            restart = false;
            
        }
    }

    /*IEnumerator DrawRooms()
    {
        foreach (var r in roomsToDraw)
        {
            AlgorithmsUtils.DebugRectInt(r, Color.yellow, 1000f);
            yield return new WaitForSeconds(0.5f);
        }
    }*/

    public void HorizontalSplit(RectInt room)
    {
        RectInt room1 = new RectInt();
        room1.x = room.x;
        room1.y = room.y;
        room1.width = room.width / 2 - wallWidth / 2;
        room1.height = room.height;

        RectInt room2 = new RectInt();
        room2.x = (room.x + room.width) / 2 + wallWidth / 2;
        room2.y = room.y;
        room2.width = room.width / 2 - wallWidth / 2;
        room2.height = room.height;

        newRooms.Add(room1);
        newRooms.Add(room2);
        roomsToRemove.Add(room);
    }
    public void VerticalSplit(RectInt room)
    {
        RectInt room1 = new RectInt();
        room1.x = room.x;
        room1.y = room.y;
        room1.width = room.width;
        room1.height = room.height / 2 - wallWidth / 2;

        RectInt room2 = new RectInt();
        room2.x = room.x;
        room2.y = (room.y + room.height) / 2 + wallWidth / 2;
        room2.width = room.width;
        room2.height = room.height / 2 - wallWidth / 2;

        newRooms.Add(room1);
        newRooms.Add(room2);
        roomsToRemove.Add(room);
    }

    public bool IsSplittable(RectInt room)
    {
        if (room.width >= minRoomSize * 2 && room.height >= minRoomSize * 2)
            return true;
        else
            return false;
    }

    public bool RandomBool()
    {
        int number = UnityEngine.Random.Range(0, 10000);
        if (number % 2 == 0) return true;
        else return false;
    }


}
