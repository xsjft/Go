using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using static UnityEngine.GraphicsBuffer;

/// <summary>
/// 3D围棋游戏主控制器
/// 负责处理棋盘逻辑、玩家输入、规则判定、联机同步等功能
/// </summary>
public class PlayerFor3D : MonoBehaviour
{
    #region 序列化字段 

    [Header("UI按钮")]
    [SerializeField] private Button RestartGameButton;
    [SerializeField] private Button HuiQiButton;
    [SerializeField] private Button PassTurnButton;
    [SerializeField] private Button ResignButton;
    [SerializeField] private Button ExitButton;
    [SerializeField] private Button FocusLastMoveButton;

    [Header("棋子预制体")]
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private Vector3 StoneSize = Vector3.one;
    [SerializeField] private GameObject LastMoveMarkerPrefab;

    [Header("预下棋（临时显示）")]
    [SerializeField] private GameObject TempBlackStone;   // black_tem prefab
    [SerializeField] private GameObject TempWhiteStone;   // white_tem prefab

    [Header("点击检测")]
    [SerializeField] private LayerMask whatIsBoard;

    [Header("规则设置")]
    [SerializeField] private bool useSuperKo = true;              // 超级劫：局面不可重复
    [SerializeField] private bool allowSuicide = false;           // 一般围棋不允许自杀
    [SerializeField] private bool rollbackHistoryOnUndo = true;   // 悔棋会回滚超级劫历史

    [Header("操作快捷键")]
    [SerializeField] private KeyCode undoKey = KeyCode.Z;
    [SerializeField] private KeyCode passKey = KeyCode.Space;
    [SerializeField] private KeyCode resignKey = KeyCode.R;

    [Header("终局设置")]
    [SerializeField] private bool endOnTwoPasses = true;          // 连续两次停一手就终局
    [SerializeField] private float komi = 7.5f;                   // 贴目（按需要改）

    [Header("日志设置")]
    [SerializeField] private bool enableLog = true;
    [SerializeField] private bool logGroupsOnMove = false;

    [Header("相机控制")]
    [SerializeField] private Transform cameraTarget;              // 为空则用本物体 transform
    [SerializeField] private float rotateSpeed = 180f;            // 度/秒
    [SerializeField] private float zoomSpeed = 0.1f;              // 缩放速度
    [SerializeField] private float minDistance = 2.5f;            // 最小距离
    [SerializeField] private float maxDistance = 6f;              // 最大距离
    [SerializeField] private float rotate180Duration = 12f;       // 180度旋转动画时长

    #endregion

    #region 内部数据结构

    /// <summary>
    /// 棋盘点数据
    /// </summary>
    private class PointData
    {
        public int index;                           // 点索引
        public Vector3 localPos;                    // 本地坐标
        public List<int> neighbors = new List<int>(); // 邻接点索引
    }

    /// <summary>
    /// 棋串信息
    /// </summary>
    private class GroupInfo
    {
        public int color;                           // 1黑 2白
        public List<int> stones = new List<int>();  // 棋串包含的所有棋子索引
        public HashSet<int> liberties = new HashSet<int>(); // 气（空点索引）
    }

    /// <summary>
    /// 游戏状态快照（用于悔棋）
    /// </summary>
    private struct GameState
    {
        public int[] stoneType;                     // 棋盘状态数组
        public bool blackTurn;                      // 是否黑方回合
        public HashSet<string> boardHistory;        // 历史局面（超级劫）
        public string boardState;                   // 当前局面字符串
        public int moveNumber;                      // 手数
        public int consecutivePasses;               // 连续停一手次数
        public bool gameOver;                       // 游戏是否结束
        public int lastMoveIndex;                   // 记录该状态下的最后落子点
    }

    /// <summary>
    /// 非法落子原因
    /// </summary>
    private enum IllegalReason
    {
        None,       // 合法
        Occupied,   // 已有棋子
        Suicide,    // 自杀
        SuperKo     // 超级劫
    }

    #endregion

    #region 私有字段

    // 棋盘数据
    private string csvPath = "Data/p.csv";          // 相对于 StreamingAssets 文件夹的路径
    private string csvFullPath;                     // CSV完整路径
    private List<PointData> points;                 // 棋盘所有点数据
    private int[] stoneType;                        // 0空 1黑 2白
    private Dictionary<int, GameObject> stoneObj;   // index -> 棋子对象

    // 游戏状态
    private bool blackTurn = true;                  // 当前是否黑方回合
    private bool gameOver = false;                  // 游戏是否结束
    private int moveNumber = 0;                     // 当前手数
    private int consecutivePasses = 0;              // 连续停一手次数（双方合计）

    // 历史记录
    private HashSet<string> boardHistory = new HashSet<string>(); // 历史局面（超级劫）
    private Stack<GameState> undoStack = new Stack<GameState>(); // 悔棋栈

    // 临时预下棋
    private int tempIdx = -1;                       // 预下棋的点索引
    private GameObject tempStoneObj = null;         // 预下棋对象

    // 联机信息
    private int PlayerType;                         // 1黑2白
    private bool IsOnline;                          // 是否联机模式

    // 相机控制
    [Header("相机")]
    [SerializeField]private Camera cam;
    [SerializeField]private Camera backCam;
    private float yaw;                              // 水平旋转角
    private float pitch;                            // 垂直旋转角
    private float distance;                         // 相机距离
    private bool rotating = false;                  // 是否正在执行180度旋转动画

    // 标记点实例
    private GameObject currentMarkerObj;
    private int lastMoveIndex = -1;                 // 最后一步落子的索引 (-1表示无)

    #endregion

    #region Unity生命周期

    private void Awake()
    {
        // 初始化CSV路径
        csvFullPath = Path.Combine(Application.streamingAssetsPath, csvPath);
        if (!File.Exists(csvFullPath))
        {
            Debug.LogError($"CSV not found: {csvFullPath}\n建议：把 p.csv 放到 Assets/Model/p.csv，然后在 Inspector 填 Model/p.csv");
        }
        else
        {
            Log($"CSV FULL PATH = {csvFullPath}");
        }

        // 注册聚焦按钮事件
        if (FocusLastMoveButton != null)
        {
            FocusLastMoveButton.onClick.AddListener(OnFocusLastMoveClicked);
        }

        // 初始化联网状态和执子类型
        SetOnline(GameManger.instance.IsOnline, GameManger.instance.PlayerType);

        // 注册联机事件
        GameManger.instance.OnLuoziRecived3D += TryPlaceStone;
        GameManger.instance.OnHuiQiSuccess += Undo;  // 悔棋成功调用两次
        GameManger.instance.OnHuiQiSuccess += Undo;
        GameManger.instance.OnPassTurnRecived += Pass;
        GameManger.instance.OnResignRecived += Resign;

        // 初始化按钮状态
        RestartGameButton.interactable = false;
        HuiQiButton.interactable = false;
        PassTurnButton.interactable = false;
        ResignButton.interactable = false;
        ExitButton.interactable = false;
        FocusLastMoveButton.interactable = false;
    }

    private void Start()
    {
        // 加载棋盘数据
        LoadBoardFromCSV();
        stoneType = new int[points.Count];
        stoneObj = new Dictionary<int, GameObject>(points.Count);

        // 初始空局面：入历史、入悔棋栈
        string initState = GetBoardState();
        boardHistory.Add(initState);
        PushUndoState(initState); // 让"第一次悔棋"回到空盘

        Log($"Board loaded. Points={points.Count}");

        // 初始化相机
        InitCameraOrbit();

        // 初始空局面入栈时，记录 lastMoveIndex 为 -1
        PushUndoState(GetBoardState());

        // 实例化标记点（开始时隐藏）
        if (LastMoveMarkerPrefab != null)
        {
            currentMarkerObj = Instantiate(LastMoveMarkerPrefab, transform);
            currentMarkerObj.SetActive(false);
        }
    }

    private void Update()
    {
        // 更新UI按钮状态
        UpdateButtonStates();


        // 处理相机输入
        HandleCameraInput();

        // 游戏结束则不处理输入
        if (gameOver) return;

        // 处理键盘输入
        HandleKeyboardInput();

        // 处理鼠标点击落子
        HandleMouseInput();
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 设置联机模式和玩家类型
    /// </summary>
    public void SetOnline(bool isOnline, int playerType)
    {
        this.IsOnline = isOnline;
        this.PlayerType = playerType;
    }

    /// <summary>
    /// 从CSV加载棋盘数据
    /// </summary>
    private void LoadBoardFromCSV()
    {
        var lines = File.ReadAllLines(csvFullPath);
        if (lines.Length <= 1) throw new Exception("CSV empty or only header.");

        // 找到最大索引
        int maxIndex = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            int idx = int.Parse(parts[0].Trim());
            if (idx > maxIndex) maxIndex = idx;
        }

        points = new List<PointData>(new PointData[maxIndex + 1]);

        // 解析每一行
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');

            int index = int.Parse(parts[0].Trim());
            float x = float.Parse(parts[1].Trim());
            float y = float.Parse(parts[2].Trim());
            float z = float.Parse(parts[3].Trim());

            var pd = new PointData
            {
                index = index,
                localPos = new Vector3(x, y, z)
            };

            // 解析邻接点
            for (int n = 4; n < parts.Length; n++)
            {
                if (int.TryParse(parts[n].Trim(), out int neighborIndex))
                {
                    if (neighborIndex >= 0) pd.neighbors.Add(neighborIndex);
                }
            }

            points[index] = pd;
        }

        // 验证数据完整性
        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null)
                throw new Exception($"CSV missing row for index={i} (points[{i}] is null)");
        }
    }

    #endregion

    #region 输入处理

    /// <summary>
    /// 更新UI按钮状态
    /// </summary>
    private void UpdateButtonStates()
    {
        if (gameOver)
        {
            RestartGameButton.interactable = true;
            HuiQiButton.interactable = false;
            PassTurnButton.interactable = false;
            ResignButton.interactable = false;
            ExitButton.interactable = true;
            FocusLastMoveButton.interactable= false;
            return;
        }

        bool isMyTurn = CheckTurn();
        RestartGameButton.interactable = false;
        HuiQiButton.interactable = isMyTurn;
        PassTurnButton.interactable = isMyTurn;
        ResignButton.interactable = isMyTurn;
        ExitButton.interactable = false;
        FocusLastMoveButton.interactable = true;
    }

    /// <summary>
    /// 处理键盘输入
    /// </summary>
    private void HandleKeyboardInput()
    {
        if (!CheckTurn()) return;

        // 悔棋
        if (Input.GetKeyDown(undoKey))
        {
            if (IsOnline)
                GameManger.instance.SendHuiQi();
            else
            {
                Undo();
                Undo();
            }
        }

        // 停一手
        if (Input.GetKeyDown(passKey))
        {
            if (IsOnline)
                GameManger.instance.SendPassTurn();
            else
                Pass();
        }

        // 认输
        if (Input.GetKeyDown(resignKey))
        {
            if (IsOnline)
                GameManger.instance.SendResign();
            else
                Resign();
        }
    }

    /// <summary>
    /// 处理鼠标点击落子
    /// </summary>
    private void HandleMouseInput()
    {
        if (!Input.GetMouseButtonDown(0) || !CheckTurn()) return;

        if (!TryGetHit(out RaycastHit hit)) return;

        int idx = WorldPointToClosestIndex(hit.point);

        // 点到已有棋子：取消预下棋
        if (stoneType[idx] != 0)
        {
            ClearTempStone();
            return;
        }

        // 第一次点：生成预下棋
        if (tempIdx < 0)
        {
            ShowTempStone(idx);
            return;
        }

        // 第二次点同一位置：确认落子
        if (idx == tempIdx)
        {
            ConfirmTempStone();
            return;
        }

        // 第二次点不同位置：移动预下棋
        ShowTempStone(idx);
    }

    /// <summary>
    /// 检查是否是自己回合（单机模式始终是）
    /// </summary>
    private bool CheckTurn()
    {
        if (!IsOnline) return true;
        return (blackTurn && PlayerType == 1) || (!blackTurn && PlayerType == 2);
    }

    /// <summary>
    /// 射线检测棋盘
    /// </summary>
    private bool TryGetHit(out RaycastHit hit)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBoard);
    }

    /// <summary>
    /// 世界坐标转换为最近的棋盘点索引
    /// </summary>
    private int WorldPointToClosestIndex(Vector3 worldPoint)
    {
        int closest = -1;
        float minSqr = float.MaxValue;

        for (int i = 0; i < points.Count; i++)
        {
            Vector3 wp = transform.TransformPoint(points[i].localPos);
            float d = (wp - worldPoint).sqrMagnitude;
            if (d < minSqr)
            {
                minSqr = d;
                closest = i;
            }
        }

        return closest;
    }

    #endregion

    #region 核心：落子与规则

    /// <summary>
    /// 尝试在指定位置落子
    /// </summary>
    private void TryPlaceStone(int idx)
    {
        if (idx < 0 || idx >= stoneType.Length) return;

        // 检查位置是否已占用
        if (stoneType[idx] != 0)
        {
            Log($"[非法] 点 {idx} 已有棋子。");
            return;
        }

        int color = blackTurn ? 1 : 2;
        int opponent = (color == 1) ? 2 : 1;

        // 保存落子前状态（用于非法回滚）
        int[] before = (int[])stoneType.Clone();
        string beforeState = GetBoardState();

        // 先落子
        stoneType[idx] = color;

        // 计算分组（落子后）并尝试提子
        var groupsAfterPlace = BuildGroups(out int[] headOf);
        List<int> captured = CaptureAdjacentOpponentGroups(idx, opponent, groupsAfterPlace, headOf);

        // 重新算一次分组（提子会改变局面）
        var groupsAfterCapture = BuildGroups(out headOf);

        // 自杀检查（除非允许自杀）
        if (!allowSuicide)
        {
            int myHead = headOf[idx];
            if (myHead < 0 || !groupsAfterCapture.ContainsKey(myHead) || groupsAfterCapture[myHead].liberties.Count == 0)
            {
                Rollback(before, beforeState);
                Log($"[非法] 自杀禁手：落子点 {idx}（{ColorName(color)}）无气。");
                return;
            }
        }

        // 超级劫检查
        string newState = GetBoardState();
        if (useSuperKo && boardHistory.Contains(newState))
        {
            Rollback(before, beforeState);
            Log($"[非法] 超级劫：该落子会复现历史局面。点 {idx}（{ColorName(color)}）");
            return;
        }

        // 合法：落子生效
        moveNumber++;
        RebuildVisualFromState(stoneType);

        // 更新最后一步的索引并刷新显示
        lastMoveIndex = idx;
        UpdateLastMoveMarker();

        // 如果是联网模式，向对方发送落子位置
        if (IsOnline)
        {
            GameManger.instance.SendLuoziInfo3D(idx);
        }

        blackTurn = !blackTurn;
        // 记录历史/悔棋
        boardHistory.Add(newState);
        PushUndoState(newState);

        consecutivePasses = 0;

        // 输出日志
        LogMove(idx, color, captured, groupsAfterCapture, headOf);

    }

    /// <summary>
    /// 回滚到落子前状态
    /// </summary>
    private void Rollback(int[] before, string beforeState)
    {
        stoneType = before;
        RebuildVisualFromState(before);
        // 注意：beforeState 本来就在历史里，不需要改 boardHistory
    }

    /// <summary>
    /// 提取相邻的无气对方棋串
    /// </summary>
    private List<int> CaptureAdjacentOpponentGroups(
        int placedIdx,
        int opponentColor,
        Dictionary<int, GroupInfo> groups,
        int[] headOf)
    {
        HashSet<int> opponentHeads = new HashSet<int>();
        foreach (int nb in points[placedIdx].neighbors)
        {
            if (nb < 0 || nb >= stoneType.Length) continue;
            if (stoneType[nb] == opponentColor)
            {
                int h = headOf[nb];
                if (h >= 0) opponentHeads.Add(h);
            }
        }

        List<int> capturedStones = new List<int>();
        foreach (int head in opponentHeads)
        {
            if (!groups.ContainsKey(head)) continue;
            if (groups[head].liberties.Count == 0)
            {
                foreach (int s in groups[head].stones)
                {
                    stoneType[s] = 0;
                    capturedStones.Add(s);
                }
            }
        }

        return capturedStones;
    }

    #endregion

    #region 分组算法（BFS）

    /// <summary>
    /// 构建棋串分组（BFS）并生成 stone->head 映射
    /// </summary>
    private Dictionary<int, GroupInfo> BuildGroups(out int[] headOf)
    {
        var groups = new Dictionary<int, GroupInfo>();
        headOf = new int[stoneType.Length];
        for (int i = 0; i < headOf.Length; i++) headOf[i] = -1;

        bool[] visited = new bool[stoneType.Length];

        for (int i = 0; i < stoneType.Length; i++)
        {
            if (stoneType[i] == 0 || visited[i]) continue;

            int color = stoneType[i];
            int head = i;

            var g = new GroupInfo { color = color };
            Queue<int> q = new Queue<int>();
            q.Enqueue(i);
            visited[i] = true;

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                g.stones.Add(cur);
                headOf[cur] = head;

                foreach (int nb in points[cur].neighbors)
                {
                    if (nb < 0 || nb >= stoneType.Length) continue;

                    if (stoneType[nb] == 0)
                    {
                        g.liberties.Add(nb);
                    }
                    else if (stoneType[nb] == color && !visited[nb])
                    {
                        visited[nb] = true;
                        q.Enqueue(nb);
                    }
                }
            }

            groups[head] = g;
        }

        return groups;
    }

    #endregion

    #region 可视化

    /// <summary>
    /// 根据状态数组重建所有棋子可视化对象
    /// </summary>
    private void RebuildVisualFromState(int[] state)
    {
        // 销毁所有现有棋子
        foreach (var kv in stoneObj)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        stoneObj.Clear();

        // 生成新棋子
        for (int i = 0; i < state.Length; i++)
        {
            if (state[i] == 0) continue;
            SpawnStoneAt(i, state[i]);
        }
    }

    /// <summary>
    /// 在指定位置生成棋子对象
    /// </summary>
    private void SpawnStoneAt(int idx, int color)
    {
        Vector3 worldPos = transform.TransformPoint(points[idx].localPos);

        // 用球心->点 做法线，避免 hit.normal 导致邻居棋子"贴歪"
        Vector3 normal = (worldPos - transform.position).normalized;
        Quaternion rot = Quaternion.LookRotation(normal, Vector3.forward);

        GameObject prefab = (color == 1) ? BlackStone : WhiteStone;
        GameObject go = Instantiate(prefab, worldPos, rot, transform);
        go.transform.localScale = StoneSize;

        stoneObj[idx] = go;
    }

    /// <summary>
    /// 清除临时预下棋对象
    /// </summary>
    private void ClearTempStone()
    {
        if (tempStoneObj != null) Destroy(tempStoneObj);
        tempStoneObj = null;
        tempIdx = -1;
    }

    /// <summary>
    /// 显示预下棋（临时棋子）
    /// </summary>
    private void ShowTempStone(int idx)
    {
        // 只允许在空点预下
        if (idx < 0 || idx >= stoneType.Length) return;

        if (stoneType[idx] != 0)
        {
            ClearTempStone();
            return;
        }

        // 按回合选择预下棋 prefab
        GameObject prefab = blackTurn ? TempBlackStone : TempWhiteStone;
        if (prefab == null)
        {
            Debug.LogError("TempBlackStone / TempWhiteStone 未绑定：请把 black_tem / white_tem prefab 拖入 Inspector");
            return;
        }

        // 若已有 temp，先删掉再生成新的
        if (tempStoneObj != null) Destroy(tempStoneObj);

        Vector3 worldPos = transform.TransformPoint(points[idx].localPos);
        Vector3 normal = (worldPos - transform.position).normalized;
        Quaternion rot = Quaternion.LookRotation(normal, Vector3.forward);

        GameObject go = Instantiate(prefab, worldPos, rot, transform);
        go.transform.localScale = StoneSize;

        tempStoneObj = go;
        tempIdx = idx;
    }

    /// <summary>
    /// 确认预下棋，正式落子
    /// </summary>
    private void ConfirmTempStone()
    {
        if (tempIdx < 0) return;

        int idx = tempIdx;
        ClearTempStone();         // 先清掉预下棋，再正式落子（避免视觉冲突）
        TryPlaceStone(idx);       // 走你原来的完整规则/提子/劫/日志/入栈
    }

    #endregion

    #region 标记与聚焦

    /// <summary>
    /// 更新最后一步棋的标记位置
    /// </summary>
    private void UpdateLastMoveMarker()
    {
        if (currentMarkerObj == null) return;

        // 如果索引无效，或者该位置实际上没有棋子，则隐藏
        if (lastMoveIndex < 0 || lastMoveIndex >= points.Count || stoneType[lastMoveIndex] == 0)
        {
            currentMarkerObj.SetActive(false);
            return;
        }

        // 1. 获取目标点的【本地坐标】 (不再使用世界坐标，避免旋转带来的误差)
        Vector3 targetLocalPos = points[lastMoveIndex].localPos;

        // 2. 计算本地法线方向 (假设球心在本地原点(0,0,0)，这是最准确的方向)
        Vector3 localNormal = targetLocalPos.normalized;

        // 3. 调整偏移量 
        float surfaceOffset = 0.03f; 

        // 设置标记的【本地位置】
        currentMarkerObj.transform.localPosition = targetLocalPos + localNormal * surfaceOffset;

        // 4. 设置旋转
        // 将本地法线转换为世界法线，确保标记正面朝外
        Vector3 worldNormal = transform.TransformDirection(localNormal);
        currentMarkerObj.transform.rotation = Quaternion.LookRotation(worldNormal);
        
        // 激活显示
        currentMarkerObj.SetActive(true);
    }

    public void OnFocusLastMoveClicked()
    {
        if (lastMoveIndex < 0 || points == null) return;
        
        // 启动平滑旋转协程
        StartCoroutine(AnimateCameraFocus(lastMoveIndex));
    }

    /// <summary>
    /// 平滑移动相机视角到指定棋子正上方
    /// </summary>
    private System.Collections.IEnumerator AnimateCameraFocus(int targetIndex)
    {
        if (targetIndex < 0 || targetIndex >= points.Count) yield break;

        // 1. 计算目标方向（从球心指向棋子）
        Vector3 targetPosLocal = points[targetIndex].localPos;
        Vector3 dir = targetPosLocal.normalized; // 假设棋盘中心是(0,0,0)

        // 2. 根据方向反推目标的 Pitch 和 Yaw
        // Unity坐标系中：
        // Pitch (X轴旋转) = Asin(y)
        // Yaw (Y轴旋转) = Atan2(x, z)
        float targetPitch = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 180f;

        // 3. 处理角度循环 (0-360)，确保从最近路径旋转
        float startYaw = yaw;
        float startPitch = pitch;

        // 简单的插值处理
        float duration = 0.8f; // 动画时长
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            float t = timer / duration;
            // 使用平滑插值
            t = t * t * (3f - 2f * t);

            // 使用 Mathf.LerpAngle 自动处理 359->1 的跨度问题
            yaw = Mathf.LerpAngle(startYaw, targetYaw, t);
            pitch = Mathf.LerpAngle(startPitch, targetPitch, t);
            
            // 实时更新相机
            UpdateCameraTransform();
            yield return null;
        }

        // 确保最终精确对齐
        yaw = targetYaw;
        pitch = targetPitch;
        UpdateCameraTransform();
    }

    #endregion

    #region 局面序列化

    /// <summary>
    /// 获取当前棋盘状态字符串（用于超级劫判断）
    /// </summary>
    private string GetBoardState()
    {
        StringBuilder sb = new StringBuilder(stoneType.Length);
        for (int i = 0; i < stoneType.Length; i++)
        {
            sb.Append((char)('0' + stoneType[i]));
        }
        return sb.ToString();
    }

    /// <summary>
    /// 从状态字符串加载棋盘
    /// </summary>
    private void LoadBoardState(string state)
    {
        if (state == null || state.Length != stoneType.Length)
            throw new Exception("Invalid board state length.");

        for (int i = 0; i < stoneType.Length; i++)
        {
            stoneType[i] = state[i] - '0';
        }
        RebuildVisualFromState(stoneType);
    }

    #endregion

    #region 悔棋/停一手/认输

    /// <summary>
    /// 保存当前状态到悔棋栈
    /// </summary>
    private void PushUndoState(string boardState)
    {
        var snap = new GameState
        {
            stoneType = (int[])stoneType.Clone(),
            blackTurn = blackTurn,
            boardState = boardState,
            boardHistory = rollbackHistoryOnUndo ? new HashSet<string>(boardHistory) : null,
            moveNumber = moveNumber,
            consecutivePasses = consecutivePasses,
            gameOver = gameOver,
            lastMoveIndex = this.lastMoveIndex
        };
        undoStack.Push(snap);
    }

    /// <summary>
    /// 悔棋（回退一步）
    /// </summary>
    public void Undo()
    {
        if (undoStack.Count <= 1)
        {
            Log("[悔棋] 已经是初始局面，不能再悔棋。");
            return;
        }

        // 弹出"当前局面"
        undoStack.Pop();

        // 回到上一个快照
        GameState prev = undoStack.Peek();
        stoneType = (int[])prev.stoneType.Clone();
        blackTurn = prev.blackTurn;
        moveNumber = prev.moveNumber;
        gameOver = prev.gameOver;
        consecutivePasses = prev.consecutivePasses;

        // [新增] 恢复最后一步索引
        lastMoveIndex = prev.lastMoveIndex;

        if (rollbackHistoryOnUndo && prev.boardHistory != null)
        {
            boardHistory = new HashSet<string>(prev.boardHistory);
        }

        RebuildVisualFromState(stoneType);
        ClearTempStone();

        // 刷新标记显示
        UpdateLastMoveMarker();

        Log($"[悔棋] 回到第 {moveNumber} 手。轮到：{(blackTurn ? "黑" : "白")}");
    }

    /// <summary>
    /// 停一手（弃权）
    /// </summary>
    public void Pass()
    {
        if (gameOver) return;

        ClearTempStone();

        Log($"[弃权] {CurrentColorName()} 弃权一手。");

        // 计入手数（与2D一致：停一手也算一手）
        moveNumber++;

        // 连停计数
        consecutivePasses++;

        // 切手
        blackTurn = !blackTurn;

        // 关键：停一手也要能悔棋回去（但不写入 boardHistory，因为局面没变）
        PushUndoState(GetBoardState());

        // 连续两次停一手 -> 终局算分
        if (endOnTwoPasses && consecutivePasses >= 2)
        {
            EndGameByTwoPasses();
        }
    }

    /// <summary>
    /// 认输
    /// </summary>
    public void Resign()
    {
        ClearTempStone();

        string win = blackTurn ? "白棋获胜" : "黑棋获胜";
        GameManger.instance.ShowPopupType2(win);
        gameOver = true;
    }

    /// <summary>
    /// 双方连续停一手，终局算分
    /// </summary>
    private void EndGameByTwoPasses()
    {
        gameOver = true;

        var result = CalculateAreaScore(komi, out float blackScore, out float whiteScore);

        GameManger.instance.ShowPopupType2("双方各停一手，\n游戏结束。\n\n" + result);
    }

    

    #endregion

    #region 终局计分

    /// <summary>
    /// 计算数子法得分（区域计分）
    /// </summary>
    private string CalculateAreaScore(float komiValue, out float blackScore, out float whiteScore)
    {
        // 统计棋子数
        int blackStones = 0, whiteStones = 0;
        for (int i = 0; i < stoneType.Length; i++)
        {
            if (stoneType[i] == 1) blackStones++;
            else if (stoneType[i] == 2) whiteStones++;
        }

        // 领地（基于邻接表 BFS 空点连通块）
        bool[] visited = new bool[stoneType.Length];
        int blackTerr = 0, whiteTerr = 0;

        for (int i = 0; i < stoneType.Length; i++)
        {
            if (stoneType[i] != 0 || visited[i]) continue;

            Queue<int> q = new Queue<int>();
            List<int> region = new List<int>();
            HashSet<int> borderColors = new HashSet<int>(); // 1黑 2白

            visited[i] = true;
            q.Enqueue(i);

            while (q.Count > 0)
            {
                int cur = q.Dequeue();
                region.Add(cur);

                foreach (int nb in points[cur].neighbors)
                {
                    if (nb < 0 || nb >= stoneType.Length) continue;

                    int c = stoneType[nb];
                    if (c == 0)
                    {
                        if (!visited[nb])
                        {
                            visited[nb] = true;
                            q.Enqueue(nb);
                        }
                    }
                    else if (c == 1 || c == 2)
                    {
                        borderColors.Add(c);
                    }
                }
            }

            // 只被单色包围才算该色领地
            if (borderColors.Count == 1)
            {
                int owner = 0;
                foreach (var oc in borderColors) owner = oc;

                if (owner == 1) blackTerr += region.Count;
                else if (owner == 2) whiteTerr += region.Count;
            }
        }

        blackScore = blackStones + blackTerr;
        whiteScore = whiteStones + whiteTerr + komiValue;

        string winner;
        float diff = Mathf.Abs(blackScore - whiteScore);
        if (blackScore > whiteScore) winner = $"黑胜 {diff:0.0}";
        else if (whiteScore > blackScore) winner = $"白胜 {diff:0.0}";
        else winner = "平局";

        return $"黑：棋子{blackStones} + 领地{blackTerr} = {blackScore:0.0}，\n白：棋子{whiteStones} + 领地{whiteTerr} + 贴目{komiValue:0.0} = {whiteScore:0.0} \n结果 -> {winner}";
    }

    #endregion

    #region 相机控制

    /// <summary>
    /// 初始化相机轨道控制
    /// </summary>
    private void InitCameraOrbit()
    {
        if (cam == null)
        {
            Debug.LogError("Main Camera not found. 请给相机设置 Tag=MainCamera");
            return;
        }

        if (cameraTarget == null) cameraTarget = transform;

        Vector3 offset = cam.transform.position - cameraTarget.position;
        distance = Mathf.Clamp(offset.magnitude, minDistance, maxDistance);

        // 从当前相机位置反推 yaw / pitch
        Vector3 dir = offset.normalized;
        pitch = Mathf.Asin(dir.y) * Mathf.Rad2Deg;
        yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // 不再限制 pitch，保持自由旋转
        yaw = Mathf.Repeat(yaw, 360f);
        pitch = Mathf.Repeat(pitch, 360f);

        UpdateCameraTransform();
    }

    /// <summary>
    /// 处理相机输入（右键旋转、滚轮缩放）
    /// </summary>
    private void HandleCameraInput()
    {
        if (cam == null) return;
        if (cameraTarget == null) cameraTarget = transform;

        // 右键按住旋转
        if (Input.GetMouseButton(1))
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            
            // 判断当前是否处于“倒立”状态
            // 因为使用了 Mathf.Repeat(pitch, 360f)，所以倒立的区间是 90 到 270 度
            bool isUpsideDown = pitch > 90f && pitch < 270f;

            // 如果倒立，反转水平输入的各方向
            if (isUpsideDown)
            {
                mx = -mx;
            }

            yaw += mx * rotateSpeed * Time.deltaTime;
            pitch -= my * rotateSpeed * Time.deltaTime;

            // 保持 0-360 循环
            yaw = Mathf.Repeat(yaw, 360f);
            pitch = Mathf.Repeat(pitch, 360f);
        }

        //滚轮缩放
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        UpdateCameraTransform();
    }

    /// <summary>
    /// 更新相机变换（位置和朝向）
    /// </summary>
    private void UpdateCameraTransform()
    {
        // 1. 直接用欧拉角生成旋转
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);

        // 2. 强制设置相机旋转
        cam.transform.rotation = rot;

        // 3. 根据旋转后的方向，向后延伸 distance 距离，确定相机位置
        // 原理：目标点位置 + (旋转方向 * 向后的向量 * 距离)
        Vector3 pos = cameraTarget.position + rot * new Vector3(0f, 0f, -distance);
        cam.transform.position = pos;

        //背摄像机位置与主摄像关于中心点对称
        Vector3 center = cameraTarget.position;
        Vector3 mainPos = cam.transform.position;

        // 位置关于中心点对称
        Vector3 backPos = center * 2f - mainPos;

        backCam.transform.position = backPos;


        // 【删除】原来的 LookAt，因为它会导致越过极点时的翻转跳变
        // cam.transform.LookAt(cameraTarget.position); 
    }

    /// <summary>
    /// 180度旋转动画（UI按钮调用）
    /// </summary>
    public void UI_RotateSphere180()
    {
        if (!rotating)
            StartCoroutine(RotateSphere180Coroutine());
    }

    /// <summary>
    /// 摄像机飞向对面视角的协程
    /// </summary>
    private System.Collections.IEnumerator RotateSphere180Coroutine()
    {
        rotating = true;

        float timer = 0f;
        
        // 1. 记录起点
        float startYaw = yaw;
        float startPitch = pitch;

        // 2. 计算终点（球心对称点）
        // 目标 Yaw = 当前 Yaw + 180度
        float targetYaw = startYaw + 180f;
        // 目标 Pitch = 当前 Pitch 取反 (例如：从俯视45度 变成 仰视45度)
        float targetPitch = -startPitch; 

        // 3. 动画插值
        while (timer < rotate180Duration)
        {
            timer += Time.deltaTime;
            // 计算进度 (0到1)
            float t = timer / rotate180Duration;
            
            // 使用平滑插值公式 (SmoothStep)，让起止动作更柔和
            // t = t * t * (3f - 2f * t); 

            // 线性插值角度
            yaw = Mathf.Lerp(startYaw, targetYaw, t);
            pitch = Mathf.Lerp(startPitch, targetPitch, t);

            // 每一帧都更新相机位置
            UpdateCameraTransform();

            yield return null;
        }

        // 4. 确保最终精确到达目标值
        yaw = targetYaw;
        pitch = targetPitch;
        UpdateCameraTransform();

        rotating = false;
    }

    #endregion

    #region 日志输出

    /// <summary>
    /// 记录落子日志
    /// </summary>
    private void LogMove(int idx, int color, List<int> captured, Dictionary<int, GroupInfo> groups, int[] headOf)
    {
        int myHead = headOf[idx];
        int myLib = (myHead >= 0 && groups.ContainsKey(myHead)) ? groups[myHead].liberties.Count : -1;

        if (captured.Count > 0)
        {
            Log($"[落子] 第 {moveNumber} 手：{ColorName(color)} @ 点{idx}，提子 {captured.Count} 枚：{FormatList(captured)}，自身气={myLib}");
        }
        else
        {
            Log($"[落子] 第 {moveNumber} 手：{ColorName(color)} @ 点{idx}，未提子，自身气={myLib}");
        }

        if (logGroupsOnMove)
        {
            // 输出双方所有棋串气（调试用）
            LogGroups(groups);
        }
    }

    /// <summary>
    /// 记录所有棋串信息（调试用）
    /// </summary>
    private void LogGroups(Dictionary<int, GroupInfo> groups)
    {
        int b = 0, w = 0;
        foreach (var kv in groups)
        {
            if (kv.Value.color == 1) b++;
            else if (kv.Value.color == 2) w++;
        }

        Log($"[Groups] 黑串={b} 白串={w}");
        foreach (var kv in groups)
        {
            var g = kv.Value;
            Log($"  - {ColorName(g.color)} head={kv.Key} stones={g.stones.Count} libs={g.liberties.Count}");
        }
    }

    /// <summary>
    /// 输出日志（可开关）
    /// </summary>
    private void Log(string msg)
    {
        if (!enableLog) return;
        Debug.Log(msg);
    }

    /// <summary>
    /// 获取当前回合方名称
    /// </summary>
    private string CurrentColorName() => blackTurn ? "黑方" : "白方";

    /// <summary>
    /// 获取颜色名称
    /// </summary>
    private string ColorName(int c) => c == 1 ? "黑" : (c == 2 ? "白" : "空");

    /// <summary>
    /// 格式化列表输出（避免刷屏）
    /// </summary>
    private string FormatList(List<int> lst)
    {
        if (lst == null || lst.Count == 0) return "[]";
        // 列表太长就截断，避免刷屏
        int cap = Mathf.Min(lst.Count, 30);
        StringBuilder sb = new StringBuilder();
        sb.Append('[');
        for (int i = 0; i < cap; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(lst[i]);
        }
        if (lst.Count > cap) sb.Append("...");
        sb.Append(']');
        return sb.ToString();
    }

    #endregion

    #region UI桥接方法

    /// <summary>
    /// UI按钮：悔棋
    /// </summary>
    public void UI_HuiQi()
    {
        if (IsOnline)
        {
            if ((PlayerType == 1 && blackTurn) || (PlayerType == 2 && !blackTurn))
            {
                GameManger.instance.SendHuiQi();
            }
        }
        else
        {
            Undo();
            Undo();
        }
    }

    /// <summary>
    /// UI按钮：停一手
    /// </summary>
    public void UI_PassTurn()
    {
        if (IsOnline)
        {
            if ((PlayerType == 1 && blackTurn) || (PlayerType == 2 && !blackTurn))
            {
                Pass();
                GameManger.instance.SendPassTurn();
            }
        }
        else
        {
            Pass();
        }
    }

    /// <summary>
    /// UI按钮：认输
    /// </summary>
    public void UI_Resign()
    {
        if (IsOnline)
        {
            if ((PlayerType == 1 && blackTurn) || (PlayerType == 2 && !blackTurn))
            {
                GameManger.instance.SendResign();
            }
        }
        else
        {
            string win = blackTurn ? "白棋获胜" : "黑棋获胜";
            GameManger.instance.ShowPopupType2(win);
            gameOver = true;
        }
    }

    /// <summary>
    /// UI按钮：重新开始游戏
    /// </summary>
    public void UI_ReStartGame()
    {
        if (IsOnline)
        {
            GameManger.instance.SendRestartGame();
        }
        else
        {
            SceneManger.instance.SwitchScene("3dGame");
        }
    }

    /// <summary>
    /// UI按钮：退出游戏
    /// </summary>
    public void UI_Exit()
    {
        if (IsOnline)
        {
            GameManger.instance.SendExitRoom();
            SceneManger.instance.SwitchScene("Room");
        }
        else
        {
            SceneManger.instance.SwitchScene("Logic");
        }
    }

    /// <summary>
    /// 重置按钮交互状态（工具方法）
    /// </summary>
    public void ResetButtonInteractableFor(Button btn)
    {
        if (btn == null) return;
        StartCoroutine(GameManger.instance.ResetButtonInteractable(btn));
    }

private void OnDisable()
    {
        // 移除所有事件监听，防止内存泄漏和报错
        if (GameManger.instance != null)
        {
            GameManger.instance.OnLuoziRecived3D -= TryPlaceStone;
            GameManger.instance.OnHuiQiSuccess -= Undo;
            GameManger.instance.OnHuiQiSuccess -= Undo; // 注意：这里注册了两次，所以也要注销两次
            GameManger.instance.OnPassTurnRecived -= Pass;
            GameManger.instance.OnResignRecived -= Resign;
        }
    }


     #endregion
}
