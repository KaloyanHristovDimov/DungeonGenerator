using NaughtyAttributes;
using System.Collections;
using UnityEngine;

public class TopDownCamera : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string cameraTag = "Camera";
    [SerializeField] private float followDistance = 7.5f;
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private float stopDistance = 0.01f;
    private Transform player;
    private bool isFollowing = false;

    private float fixedY;

    private void Awake()
    {
        fixedY = transform.position.y;

        GameObject playerObject = GameObject.FindGameObjectWithTag(playerTag);
        player = playerObject.transform;

        SnapToPlayerXZ();
    }

    private void Update()
    {
        Vector3 cameraXZ = new Vector3(transform.position.x, 0f, transform.position.z);
        Vector3 playerXZ = new Vector3(player.position.x, 0f, player.position.z);

        float distance = Vector3.Distance(cameraXZ, playerXZ);
        Vector3 targetPosition = new Vector3(player.position.x, fixedY, player.position.z);
        if (distance > followDistance && !isFollowing)
        {
            isFollowing = true;
        }
        if (isFollowing)
        {
            transform.position = Vector3.Lerp(
                transform.position,
                targetPosition,
                followSpeed * Time.deltaTime
            );

            cameraXZ = new Vector3(transform.position.x, 0f, transform.position.z);
            playerXZ = new Vector3(player.position.x, 0f, player.position.z);

            float newDistance = Vector3.Distance(cameraXZ, playerXZ);

            if (newDistance <= stopDistance)
            {
                SnapToPlayerXZ();
                isFollowing = false;
                Debug.Log("Stopped following");
            }
        }
    }

    private void SnapToPlayerXZ()
    {
        transform.position = new Vector3(player.position.x, fixedY, player.position.z);
    }

    [Button]
    private void SwapCameras() 
    {
        GameObject camera = GameObject.FindGameObjectWithTag(cameraTag);
        GameObject cameraMain = GameObject.FindGameObjectWithTag("MainCamera");

        cameraMain.tag = cameraTag;
        camera.tag = "MainCamera";
    }
}
