using UnityEngine;
using System.Collections;

public class Startup : MonoBehaviour
{

    // Use this for initialization
    void Start()
    {
        Screen.SetResolution((int) (720 * Camera.main.aspect), 720, true);
        Application.targetFrameRate = 600;
    }
}
