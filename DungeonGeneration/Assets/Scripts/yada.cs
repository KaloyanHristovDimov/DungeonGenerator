using UnityEngine;

public class yada : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    private void OnDrawGizmos()
    {
        RectInt room = new RectInt(0, 0, 100, 100);
        AlgorithmsUtils.DebugRectInt(room, Color.red);
    }
}
