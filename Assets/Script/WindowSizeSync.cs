using System.Collections.Generic;
using UnityEngine;

public class WindowSizeSync : MonoBehaviour
{
    // 记录上一帧的窗口尺寸
    private int lastWindowWidth;
    private int lastWindowHeight;
    // 预设基准参数（根据你的游戏初始配置设定，如16:9窗口的初始视野角）
    public float baseFOV = 60f; // 相机初始视野角（默认通常60，可在Inspector面板调整）
    public float baseAspectRatio = 16f / 9f; // 基准宽高比（对应打包前的默认窗口比例）
    [SerializeField] private List<Camera> Cameras; // 缓存主相机

    void Start()
    {
        lastWindowWidth = Screen.width;
        lastWindowHeight = Screen.height;
    }

    void Update()
    {
        foreach (Camera cam in Cameras)
        {
            if (cam == null) return;

            // 检测窗口尺寸是否变化
            if (Screen.width != lastWindowWidth || Screen.height != lastWindowHeight)
            {
                // 1. 同步更新游戏内渲染分辨率
                Screen.SetResolution(Screen.width, Screen.height, false);

                // 2. 计算当前窗口的实际宽高比
                float currentAspectRatio = (float)Screen.width / Screen.height;

                // 3. 适配3D透视相机：调整视野角，让3D球体视觉大小同步窗口
                // 核心逻辑：根据宽高比变化，修正相机FOV，保持3D物体视觉呈现一致
                if (cam.orthographic == false) // 仅对透视相机生效
                {
                    // 计算视野角修正系数，适配非基准宽高比的窗口
                    float fovCorrection = Mathf.Atan(Mathf.Tan(baseFOV * Mathf.Deg2Rad / 2f) * (currentAspectRatio / baseAspectRatio)) * 2f * Mathf.Rad2Deg;
                    cam.fieldOfView = fovCorrection;
                }

                // 4. 刷新记录的窗口尺寸
                lastWindowWidth = Screen.width;
                lastWindowHeight = Screen.height;

                Debug.Log($"窗口已适配：{Screen.width} × {Screen.height}，相机FOV更新为：{cam.fieldOfView:F2}");
            }
        }
    }
}