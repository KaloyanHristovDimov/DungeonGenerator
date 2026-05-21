using JetBrains.Annotations;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class MouseClickController : MonoBehaviour
{
    public Vector3 clickPosition;
    public UnityEvent<Vector3> OnClick;

    void Update() 
    { 
        if (Input.GetMouseButtonDown(0)) { 
            Ray mouseRay = Camera.main.ScreenPointToRay( Input.mousePosition ); 
            if (Physics.Raycast( mouseRay, out RaycastHit hitInfo )) 
            {
                Vector3 clickWorldPosition = hitInfo.point; 
                Debug.Log(clickWorldPosition);
                Debug.DrawRay(Camera.main.transform.position, hitInfo.point - Camera.main.transform.position, Color.red, 1);
                DebugExtension.DebugWireSphere( clickWorldPosition, 1, 1, true );

                List<Vector3> clickWorldPositions = new List<Vector3>();
                clickWorldPositions.Add(clickWorldPosition);

                OnClick.Invoke(clickWorldPosition);
            }
        }
        
    }

}
