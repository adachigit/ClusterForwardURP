using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class GameObjectSwitcher : MonoBehaviour
{
    [SerializeField] private Button button;
    [SerializeField] private GameObject gameObject;

    private void Awake()
    {
        button.onClick.AddListener(OnButtonClick);
    }

    private void OnDestroy()
    {
        button.onClick.RemoveListener(OnButtonClick);
    }

    private void OnButtonClick()
    {
        if (null == gameObject) return;

        gameObject.SetActive(!gameObject.activeSelf);
    }
}
