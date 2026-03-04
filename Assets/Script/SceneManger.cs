using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneManger : MonoBehaviour
{
    public static SceneManger instance;

    private void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject); // 保持跨场景存在
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void SwitchScene(string name)
    {
        GameManger.instance.ClearPopups();
        SceneManager.LoadScene(name); // 场景名字
    }

    public string GetScreenName()
    {
        return SceneManager.GetActiveScene().name;
    }
}
