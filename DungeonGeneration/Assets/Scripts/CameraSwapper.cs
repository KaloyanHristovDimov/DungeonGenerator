using UnityEngine;

public class CameraSwapper : MonoBehaviour
{
    [SerializeField] private string cameraTag = "Camera";
    GameObject camera;
    GameObject cameraMain;
    private void Awake()
    {
        camera = GameObject.FindGameObjectWithTag(cameraTag);
        cameraMain = GameObject.FindGameObjectWithTag("MainCamera");

        camera.SetActive(false);
        cameraMain.SetActive(true);
    }

    public void SwapCameras()
    {
        if (cameraMain.activeSelf)
        {
            cameraMain.SetActive(false);
            camera.SetActive(true);

            cameraMain.tag = cameraTag;
            camera.tag = "MainCamera";
        }
        else
        {
            camera.SetActive(false);
            cameraMain.SetActive(true);

            camera.tag = cameraTag;
            cameraMain.tag = "MainCamera";
        }
    }
}
    