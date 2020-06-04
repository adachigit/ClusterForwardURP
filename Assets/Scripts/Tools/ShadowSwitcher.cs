using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ShadowSwitcher : MonoBehaviour
{
    [SerializeField] private Light light;

    public void SwitchShadowType()
    {
        if (light.shadows == LightShadows.None)
        {
            light.shadows = LightShadows.Hard;
        }
        else if (light.shadows == LightShadows.Hard)
        {
            light.shadows = LightShadows.Soft;
        }
        else
        {
            light.shadows = LightShadows.None;
        }
    }
}
