
using System;
using UnityEngine;
using UnityEngine.UI;

public class ProjMatPrinter : MonoBehaviour
{
    [SerializeField] protected Text text;

    private void Start()
    {
        text.text = $"FOV = {Camera.main.fieldOfView}, Aspect = {Camera.main.aspect}, Near = {Camera.main.nearClipPlane}, Far = {Camera.main.farClipPlane}\n" +
                    $"GL Projection Matrix:\n{GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false).ToString()}\n" +
                    $"Inverse GL Projection Matrix:\n{GL.GetGPUProjectionMatrix(Camera.main.projectionMatrix, false).inverse.ToString()}\n" +
                    $"Camera Projection Matrix:\n{Camera.main.projectionMatrix.ToString()}\n" +
                    $"Inverse Camera Projection Matrix:\n{Camera.main.projectionMatrix.inverse.ToString()}";
    }
}
