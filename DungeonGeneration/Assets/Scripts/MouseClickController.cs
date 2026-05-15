using JetBrains.Annotations;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MouseClickController : MonoBehaviour
{
    public Vector3 clickPosition;
    public UnityEvent<Vector3> OnClick;
    //private GameObject player;

    //void Start() 
    //{
    //    player = GameObject.FindWithTag("Player");
    //    PlayerController playerController = player.GetComponent<PlayerController>();
    //    OnClick.AddListener(playerController.GoToDestination);
    //}
    void Update() 
    { 
        // Get the mouse click position in world space 
        if (Input.GetMouseButtonDown(0)) { 
            Ray mouseRay = Camera.main.ScreenPointToRay( Input.mousePosition ); 
            if (Physics.Raycast( mouseRay, out RaycastHit hitInfo )) 
            {
                Vector3 clickWorldPosition = hitInfo.point; 
                Debug.Log(clickWorldPosition);
                Debug.DrawRay(Camera.main.transform.position, hitInfo.point - Camera.main.transform.position, Color.red, 1);
                DebugExtension.DebugWireSphere( clickWorldPosition, 1, 1, true );

                // TODO EXERCISE 2: Store the click position here
                List<Vector3> clickWorldPositions = new List<Vector3>();
                clickWorldPositions.Add(clickWorldPosition);

                // TODO EXERCISE 5: Trigger a Unity event to notify other scripts about the click here
                Debug.Log("TODO: Trigger event");
                OnClick.Invoke(clickWorldPosition);
            }
        }

        // TODO EXERCISE 2: Add visual debugging here
        Debug.Log("TODO: Add visual debugging");

    }

}
