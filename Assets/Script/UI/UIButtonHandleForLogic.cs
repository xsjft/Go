using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIButtonHandleForLogic : MonoBehaviour
{
    public void Logic()
    {
        GameManger.instance.Logic();
    }

    public void OfflineStart()
    {
        GameManger.instance.OfflineStart(); 
    }

    public void ResetButtonInteractableFor(Button btn)
    {
        if (btn == null) return;
        StartCoroutine(GameManger.instance.ResetButtonInteractable(btn));
    }
}
