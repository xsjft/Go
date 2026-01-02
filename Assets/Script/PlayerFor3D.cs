using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

public class PlayerFor3D : MonoBehaviour
{
    [Header("点击检测")]
    [SerializeField] private LayerMask whatIsBoard;

    [Header("棋子Prefab")]
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private Vector3 StoneSize = Vector3.one;

    [Header("棋盘点数据 (建议填 Model/p.csv 或 Assets/Model/p.csv)")]
    [SerializeField] public string csvPath = "Model/p.csv"; // 相对于 Assets/ 的路径更稳
    private string csvFullPath;

    [Header("规则/操作")]
    [SerializeField] private bool useSuperKo = true;         // 超级劫：局面不可重复
    [SerializeField] private bool allowSuicide = false;      // 一般围棋不允许自杀
    [SerializeField] private bool rollbackHistoryOnUndo = true; // ? 更像2D：悔棋会回滚超级劫历史
    [SerializeField] private KeyCode undoKey = KeyCode.Z;
    [SerializeField] private KeyCode passKey = KeyCode.Space;
    [SerializeField] private KeyCode resignKey = KeyCode.R;

    [Header("终局/算分")]
    [SerializeField] private bool endOnTwoPasses = true; // 连续两次停一手就终局
    [SerializeField] private float komi = 7.5f;          // 贴目（按需要改）

    private int consecutivePasses = 0;                   // 连续停一手次数（双方合计）


    [Header("日志")]
    [SerializeField] private bool enableLog = true;
    [SerializeField] private bool logGroupsOnMove = false;

    [Header("相机控制")]
    [SerializeField] private Transform cameraTarget;  // 为空则用本物体 transform
    [SerializeField] private float rotateSpeed = 180f; // 度/秒（可调）
    [SerializeField] private float zoomSpeed = 0.1f;     // 缩放速度（可调）
    [SerializeField] private float minDistance = 2.5f; // 最小距离
    [SerializeField] private float maxDistance = 6f;  // 最大距离

    [SerializeField] private float rotate180Duration = 12f;
    private bool rotating = false;

    private float yaw;
    private float pitch;
    private float distance;
    private Camera cam;


    // =========================
    // 内部数据结构
    // =========================

    private class PointData
    {
        public int index;
        public Vector3 localPos;
        public List<int> neighbors = new List<int>();
    }

    private class GroupInfo
    {
        public int color; // 1黑 2白
        public List<int> stones = new List<int>();
        public HashSet<int> liberties = new HashSet<int>();
    }

    private struct GameState
    {
        public int[] stoneType;
        public bool blackTurn;
        public HashSet<string> boardHistory;
        public string boardState;
        public int moveNumber;

        public int consecutivePasses;
        public bool gameOver;
    }

    private List<PointData> points;                 // points[index]
    private int[] stoneType;                        // 0空 1黑 2白
    private Dictionary<int, GameObject> stoneObj;   // index -> 棋子对象

    private bool blackTurn = true;
    private bool gameOver = false;

    // 历史局面（超级劫）
    private HashSet<string> boardHistory = new HashSet<string>();
    // 用于悔棋：保存每一步的完整状态（像2D那样回滚）
    private Stack<GameState> undoStack = new Stack<GameState>();
    private int moveNumber = 0;

    // =========================
    // Unity 生命周期
    // =========================

    private void Awake()
    {
        csvFullPath = ResolveCsvFullPath(csvPath);

        if (!File.Exists(csvFullPath))
        {
            Debug.LogError($"CSV not found: {csvFullPath}\n" +
                           $"建议：把 p.csv 放到 Assets/Model/p.csv，然后在 Inspector 填 Model/p.csv");
        }
        else
        {
            Log($"CSV FULL PATH = {csvFullPath}");
        }
    }

    private void Start()
    {
        LoadBoardFromCSV();
        stoneType = new int[points.Count];
        stoneObj = new Dictionary<int, GameObject>(points.Count);

        // 初始空局面：入历史、入悔棋栈
        string initState = GetBoardState();
        boardHistory.Add(initState);

        PushUndoState(initState); // 让“第一次悔棋”回到空盘
        Log($"Board loaded. Points={points.Count}");

        InitCameraOrbit();

    }

    private void Update()
    {
        HandleCameraInput();


        if (gameOver) return;

        if (Input.GetKeyDown(undoKey)) Undo();
        if (Input.GetKeyDown(passKey)) Pass();
        if (Input.GetKeyDown(resignKey)) Resign();

        if (Input.GetMouseButtonDown(0))
        {
            if (!TryGetHit(out RaycastHit hit)) return;

            int idx = WorldPointToClosestIndex(hit.point);
            TryPlaceStone(idx);
        }
    }


    // 相机控制
    private void InitCameraOrbit()
    {
        cam = Camera.main;
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

    private void HandleCameraInput()
    {
        if (cam == null) return;
        if (cameraTarget == null) cameraTarget = transform;

        // 右键按住旋转
        if (Input.GetMouseButton(1))
        {
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");

            yaw += mx * rotateSpeed * Time.deltaTime;
            pitch -= my * rotateSpeed * Time.deltaTime;

            // 允许无限上下/左右，为了避免 pitch 无限增大导致数值过大，做一个 0~360 的循环即可
            yaw = Mathf.Repeat(yaw, 360f);
            pitch = Mathf.Repeat(pitch, 360f);

        }

        // 滚轮缩放
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.0001f)
        {
            distance -= scroll * zoomSpeed;
            distance = Mathf.Clamp(distance, minDistance, maxDistance);
        }

        UpdateCameraTransform();
    }

    private void UpdateCameraTransform()
    {
        Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pos = cameraTarget.position + rot * new Vector3(0f, 0f, -distance);

        cam.transform.position = pos;
        cam.transform.LookAt(cameraTarget.position);
    }

    

    // =========================
    // CSV 路径处理（支持相对/Assets/前缀/绝对路径）
    // =========================

    private string ResolveCsvFullPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return path;

        // 绝对路径直接用
        if (Path.IsPathRooted(path)) return path;

        // 允许用户填 "Assets/Model/p.csv" -> 转成相对于工程根目录
        // Application.dataPath = ".../<Project>/Assets"
        // 所以如果 path 以 "Assets/" 开头，就拼到工程根目录
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("Assets\\", StringComparison.OrdinalIgnoreCase))
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, path);
        }

        // 其它：认为是相对 Assets/ 的路径，比如 "Model/p.csv"
        return Path.Combine(Application.dataPath, path);
    }

    // =========================
    // CSV读取
    // =========================

    private void LoadBoardFromCSV()
    {
        var lines = File.ReadAllLines(csvFullPath);
        if (lines.Length <= 1) throw new Exception("CSV empty or only header.");

        int maxIndex = -1;
        for (int i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',');
            int idx = int.Parse(parts[0].Trim());
            if (idx > maxIndex) maxIndex = idx;
        }

        points = new List<PointData>(new PointData[maxIndex + 1]);

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

            for (int n = 4; n < parts.Length; n++)
            {
                if (int.TryParse(parts[n].Trim(), out int neighborIndex))
                {
                    if (neighborIndex >= 0) pd.neighbors.Add(neighborIndex);
                }
            }

            points[index] = pd;
        }

        for (int i = 0; i < points.Count; i++)
        {
            if (points[i] == null)
                throw new Exception($"CSV missing row for index={i} (points[{i}] is null)");
        }
    }

    // =========================
    // 输入与映射
    // =========================

    private bool TryGetHit(out RaycastHit hit)
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        return Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBoard);
    }

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

    // =========================
    // 核心：落子与规则 + 日志
    // =========================

    private enum IllegalReason
    {
        None,
        Occupied,
        Suicide,
        SuperKo
    }

    private void TryPlaceStone(int idx)
    {
        if (idx < 0 || idx >= stoneType.Length) return;

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
                // 自杀（注意：如果提子后有气，则不会进这里）
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

        // 记录历史/悔棋
        boardHistory.Add(newState);
        PushUndoState(newState);

        consecutivePasses = 0;

        // 输出日志
        LogMove(idx, color, captured, groupsAfterCapture, headOf);

        blackTurn = !blackTurn;
    }

    private void Rollback(int[] before, string beforeState)
    {
        stoneType = before;
        RebuildVisualFromState(before);
        // 注意：beforeState 本来就在历史里，不需要改 boardHistory
    }

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

    // =========================
    // 分组（BFS）+ stone->head 映射
    // =========================

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

    // =========================
    // 可视化（重建式，稳定）
    // =========================

    private void RebuildVisualFromState(int[] state)
    {
        foreach (var kv in stoneObj)
        {
            if (kv.Value != null) Destroy(kv.Value);
        }
        stoneObj.Clear();

        for (int i = 0; i < state.Length; i++)
        {
            if (state[i] == 0) continue;
            SpawnStoneAt(i, state[i]);
        }
    }

    private void SpawnStoneAt(int idx, int color)
    {
        Vector3 worldPos = transform.TransformPoint(points[idx].localPos);

        // 用球心->点 做法线，避免 hit.normal 导致邻居棋子“贴歪”
        Vector3 normal = (worldPos - transform.position).normalized;
        Quaternion rot = Quaternion.LookRotation(normal, Vector3.forward);

        GameObject prefab = (color == 1) ? BlackStone : WhiteStone;
        GameObject go = Instantiate(prefab, worldPos, rot, transform);
        go.transform.localScale = StoneSize;

        stoneObj[idx] = go;
    }

    // =========================
    // 局面序列化
    // =========================

    private string GetBoardState()
    {
        StringBuilder sb = new StringBuilder(stoneType.Length);
        for (int i = 0; i < stoneType.Length; i++)
        {
            sb.Append((char)('0' + stoneType[i]));
        }
        return sb.ToString();
    }

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

    // =========================
    // 悔棋/弃权/认输（更像2D：悔棋回滚完整状态）
    // =========================

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
            gameOver = gameOver
        };
        undoStack.Push(snap);
    }


    public void Undo()
    {
        if (undoStack.Count <= 1)
        {
            Log("[悔棋] 已经是初始局面，不能再悔棋。");
            return;
        }

        // 弹出“当前局面”
        undoStack.Pop();

        // 回到上一个快照
        GameState prev = undoStack.Peek();
        stoneType = (int[])prev.stoneType.Clone();
        blackTurn = prev.blackTurn;
        moveNumber = prev.moveNumber;
        gameOver = prev.gameOver;
        consecutivePasses = prev.consecutivePasses;

        if (rollbackHistoryOnUndo && prev.boardHistory != null)
        {
            boardHistory = new HashSet<string>(prev.boardHistory);
        }

        RebuildVisualFromState(stoneType);

        Log($"[悔棋] 回到第 {moveNumber} 手。轮到：{(blackTurn ? "黑" : "白")}");
    }

    public void Pass()
    {
        if (gameOver) return;

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


    public void Resign()
    {
        gameOver = true;
        Log(blackTurn ? "[认输] 黑方认输，白方胜。" : "[认输] 白方认输，黑方胜。");
    }

    private void EndGameByTwoPasses()
    {
        gameOver = true;

        var result = CalculateAreaScore(komi, out float blackScore, out float whiteScore);

        Log("[终局] 双方连续停一手，开始算分。");
        Log(result);
    }

    private string CalculateAreaScore(float komiValue, out float blackScore, out float whiteScore)
    {
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

        return $"[算分] 黑：棋子{blackStones}+领地{blackTerr}={blackScore:0.0}，白：棋子{whiteStones}+领地{whiteTerr}+贴目{komiValue:0.0}={whiteScore:0.0} -> {winner}";
    }


    // =========================
    // 日志输出（复述2D功能的关键）
    // =========================

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

    private void Log(string msg)
    {
        if (!enableLog) return;
        Debug.Log(msg);
    }

    private string CurrentColorName() => blackTurn ? "黑方" : "白方";
    private string ColorName(int c) => c == 1 ? "黑" : (c == 2 ? "白" : "空");

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

    // =========================
    // UI 桥接
    // =========================

    [Header("UI（可选）")]
    [SerializeField] private UnityEngine.UI.Button btnUndo;
    [SerializeField] private UnityEngine.UI.Button btnPass;
    [SerializeField] private UnityEngine.UI.Button btnResign;

    // 按钮点的是 UI_XXX，而不是直接点核心逻辑
    public void UI_Undo()
    {
        Undo();
        RefreshAllButtons();
        
    }

    public void UI_Pass()
    {
        Pass();
        RefreshAllButtons();
    }

    public void UI_Resign()
    {
        Resign();
        RefreshAllButtons();
    }

    public void UI_RotateSphere180()
    {
        if (!rotating)
            StartCoroutine(RotateSphere180Coroutine());
    }



    /// 重置按钮可点击状态
    public void ResetButtonInteractable(UnityEngine.UI.Button clickedButton)
    {
        // 这里先给一个“最小可用”的策略：
        // - 游戏结束：全部禁用
        // - 其它：默认都可用（你后面想细分规则再加）
        if (clickedButton == null) return;

        if (gameOver)
        {
            clickedButton.interactable = false;
            return;
        }

        // 你如果想实现“点完立刻变灰 0.2 秒”，也可以在这里加协程
        clickedButton.interactable = true;
    }
    private System.Collections.IEnumerator RotateSphere180Coroutine()
    {
        rotating = true;

        Quaternion start = transform.rotation;

        // 绕任意轴旋转 180°，到达对面
        Vector3 axis = (Vector3.up + Vector3.right).normalized;
        Quaternion target = Quaternion.AngleAxis(180f, axis) * start;

        float t = 0f;
        while (t < rotate180Duration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / rotate180Duration);
            transform.rotation = Quaternion.Slerp(start, target, k);
            yield return null;
        }

        transform.rotation = target;
        rotating = false;
    }



    public void UI_ReStartGame()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void UI_Exit()
    {
        // 返回主逻辑 / 主菜单
        SceneManager.LoadScene("Logic");
    }

    /// 统一刷新所有按钮
    private void RefreshAllButtons()
    {
        if (btnUndo != null) btnUndo.interactable = !gameOver && (undoStack != null && undoStack.Count > 1);
        if (btnPass != null) btnPass.interactable = !gameOver;
        if (btnResign != null) btnResign.interactable = !gameOver;
    }


}
