using System;
using UnityEngine;
using UnityEngine.AI;

public class PlayerController : MonoBehaviour
{
    [SerializeField]
    private NavMeshAgent navMeshAgent;
    [SerializeField]
    private GameObject camera;

    private void Awake()
    {
        navMeshAgent = GetComponent<NavMeshAgent>();
        camera = GameObject.FindWithTag("MainCamera");
        MouseClickController mouseClickController = camera.GetComponent<MouseClickController>();
        mouseClickController.OnClick.AddListener(GoToDestination);
    }

    public void GoToDestination(Vector3 destination)
    {
        navMeshAgent.SetDestination(destination);
    }
}
