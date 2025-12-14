using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public class PlayerFor2D : MonoBehaviour
{
    [Header("联机信息")]
    private int PlayerType;   //1黑2白
    private bool IsOnline;

    [Header("判断点击位置")]
    [SerializeField] private LayerMask whatIsBoard;
    RaycastHit hit ;

    [Header("棋子信息")]
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private GameObject TempStone;
    [SerializeField] private int StoneNum = 0;
    [SerializeField] private Vector3 StoneSize;

    [Header("轮次信息")]
    [SerializeField] int turns=1;       //多少轮
    [SerializeField] bool BlackTurn=true;    //是不是黑棋回合

    [SerializeField] private Material tmp;

    [SerializeField] private List<bool> moveIsPass = new List<bool>(); //是否弃权


    private class BoardPoint
    {
        public int stoneType;          // 0,1,2 空，黑，白
        public int me;
        public int up;                //所有棋子存储在list中，记录id即可
        public int down;
        public int left;
        public int right;
        public Quaternion targetRotation;

        public BoardPoint(int stoneType, int me,int up, int down, int left, int right)
        {
            this.stoneType = stoneType;
            this.me = me;
            this.up = up;
            this.down = down;
            this.left = left;
            this.right = right;
            targetRotation = Quaternion.identity;

         }
    }

    private List<BoardPoint> boardPoints = new List<BoardPoint>();    

    private class StoneGroup
    {
        public List<BoardPoint> points;
        public int GroupQiCount;
        public HashSet<BoardPoint> QiPositions;    // 气的位置集合
        public StoneGroup(BoardPoint point)
        {
            points = new List<BoardPoint>();
            points.Add(point);
            GroupQiCount = 0;
            QiPositions = new HashSet<BoardPoint>();  
        }
    }

    //类似并查集，点指向头，然后头指向块，点指向物体
    //点指向头，每个点指向所属块中的头，自己是头指向自己
    private Dictionary<BoardPoint, BoardPoint>PointToPointHead = new Dictionary<BoardPoint, BoardPoint>();     
    private Dictionary<BoardPoint,StoneGroup> PointHeadToGroup = new Dictionary<BoardPoint, StoneGroup>();
    private Dictionary<BoardPoint,GameObject> PointToStone = new Dictionary<BoardPoint, GameObject>();

    private class MoveRecord        //移除落点group,再将落点group依此落子，除了落点,再恢复被移除的group,
    {
        public int stoneType;
        public BoardPoint boardPoint;       //落点
        public List<StoneGroup> stoneGroups;      //被移出的groups  
        public List<BoardPoint> boardPoints;       //落点形成的group包含的所有点

        // 新增：该步之后的完整局面
        public string boardState;  

        public MoveRecord(int stoneType,BoardPoint boardPoint)
        {
            this.stoneType = stoneType;
            this.boardPoint = boardPoint;
            this.stoneGroups = new List<StoneGroup>();
            this.boardPoints = new List<BoardPoint>();
        }
    }

    private MoveRecord LastMove;
    private List<MoveRecord> MoveRecords;

    // 棋盘历史（所有走过的局面，用于超级劫判定）
    private HashSet<string> boardHistory;
    // 初始空棋盘局面（方便悔棋后重建）
    private string initialBoardState;

        public enum GameResult
    {
        None,
        BlackWin,
        WhiteWin,
        Draw
    }

    [Header("胜负信息")]
    [SerializeField] private GameResult gameResult = GameResult.None;
    [SerializeField] private int blackStonesCount;
    [SerializeField] private int whiteStonesCount;
    [SerializeField] private int blackTerritoryCount;
    [SerializeField] private int whiteTerritoryCount;
    [SerializeField] private bool gameOver = false;      // 对局是否已结束
    [SerializeField] private int consecutivePasses = 0;  // 连续弃权次数
    [SerializeField] private float komi = 7.5f;  // 白棋贴目


    void Awake()
    {
        CreateBoardPoint(); 
        MoveRecords = new List<MoveRecord>();

        boardHistory = new HashSet<string>();
        initialBoardState = GetBoardState();   // 此时全部是空点
        boardHistory.Add(initialBoardState);   // 把初始局面也记录进去

        SetOnline(GameManger.instance.IsOnline,GameManger.instance.PlayerType);  //初始化联网状态和执子类型
        GameManger.instance.OnLuoziRecived += LuoZiAction;
        GameManger.instance.OnHuiQiSuccess += HuiQi;
        GameManger.instance.OnHuiQiSuccess += HuiQi;
    }

    void Update()
    {
        if (IsOnline)  //在线模式
        {
            //是你的回合
            if((PlayerType ==1 && BlackTurn)||(PlayerType ==2 && !BlackTurn))
            {
                if (Input.GetMouseButtonDown(1) && Check_LuoZi(PlayerType)) //落子
                {
                    LuoZiAction(PlayerType,hit.point,hit.normal);
                    GameManger.instance.SendLuoziInfo(PlayerType, hit);
                }
                if(Input.GetMouseButtonDown(2) && Check_HuiQi())
                {
                    GameManger.instance.SendHuiQi();
                }
            }
        }
        else
        {
            if (Input.GetKeyDown(KeyCode.Q) && Check_LuoZi(1))   //黑棋
            {
                LuoZiAction(1,hit.point,hit.normal);
            }

            if (Input.GetKeyDown(KeyCode.E) && Check_LuoZi(2))   //白棋
            {
                LuoZiAction(2, hit.point, hit.normal);

            }

            if (Input.GetMouseButtonDown(2) && Check_HuiQi())
            {
                HuiQi();
                HuiQi();
            }
        }

        if (Input.GetMouseButtonDown(0) &&false)
        {
            CheckStone();             //输出当前棋盘的棋子位置，在boardpoints的下标
        }

        if (Input.GetMouseButtonDown(1)&&false)
        {
            CheckGroup();          //点击一个棋子，输出棋子所在块的气，并改变这块棋子颜色
        }

        if (Input.GetMouseButtonDown(1))
        {
            CheckGroup();          //点击一个棋子，输出棋子所在块的气，并改变这块棋子颜色
        }

        if (Input.GetKeyDown(KeyCode.P))        // 按P键弃权（Pass）一手
        {
            PassTurn();
        }

        // 按空格键统计当前局面胜负（调试用）
        if (Input.GetKeyDown(KeyCode.Space))
        {
            CalculateGameResult();
        }
    }

    private void LuoZiAction(int type,Vector3 hitPoint,Vector3 hitNormal)
    {
<<<<<<< Updated upstream
        if (gameOver)
        {
            Debug.Log("对局已结束，禁止继续落子。");
            return;
        }

        if (hit.collider != null)          //鼠标在没在球面上
        {
            Quaternion targetRotation = Quaternion.LookRotation(hit.normal, Vector3.forward);
=======
        Quaternion targetRotation = Quaternion.LookRotation(hitNormal, Vector3.forward);
>>>>>>> Stashed changes

        Vector3 point = GetPoint(hitPoint);

        CreateStone(type, targetRotation, point);

<<<<<<< Updated upstream
            boardPoints[PointToNum(point)].targetRotation = targetRotation;
            StoneNum++;
            turns++;
            BlackTurn = !BlackTurn;
            consecutivePasses = 0;  // 重置连续弃权计数
            LuoZiLogic(point, true);

            moveIsPass.Add(false); //这一手是“落子”
        }
=======
        boardPoints[PointToNum(point)].targetRotation = targetRotation;
        StoneNum++;
        turns++;
        BlackTurn = !BlackTurn;
        LuoZiLogic(point, true);
>>>>>>> Stashed changes
    }

    private void CreateStone(int type, Quaternion targetRotation, Vector3 point)
    {
        GameObject qizi;
        if (type == 1)
        {
            qizi = Instantiate(BlackStone, point, targetRotation, transform);   //创建棋子
            boardPoints[PointToNum(point)].stoneType = 1;
        }
        else
        {
            qizi = Instantiate(WhiteStone, point, targetRotation, transform);
            boardPoints[PointToNum(point)].stoneType = 2;
        }
        qizi.transform.localScale = StoneSize;
        PointToStone.Add(boardPoints[PointToNum(point)], qizi);
    }

    public RaycastHit GetPosition()              //获取鼠标在球面上碰撞点信息
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;

        Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBoard);

        return hit;

    }
    private Vector3 GetPoint(Vector3 point)
    {
        point.x = Mathf.Round(point.x);
        point.z = Mathf.Round(point.z);
        return point;
    }


    private void HuiQi()
    {
        if (moveIsPass.Count == 0)
        {
            Debug.Log("没有可悔的手数");
            return;
        }

        // 取出最后一手是“落子”还是“弃权”
        bool lastWasPass = moveIsPass[moveIsPass.Count - 1];
        moveIsPass.RemoveAt(moveIsPass.Count - 1);

        // 上一手是弃权
        if (lastWasPass)
        {
            // 撤销弃权：棋盘不变，只恢复回合、连续弃权计数
            if (consecutivePasses > 0)
                consecutivePasses--;

            if (turns > 0)
                turns--;

            BlackTurn = !BlackTurn;

            Debug.Log("悔棋：撤销上一手弃权，棋盘形势不变。");
            return;
        }

        // 上一手是正常落子
        if (MoveRecords.Count == 0)
        {
            Debug.LogWarning("MoveRecords 为空，但上一手标记为落子，数据不一致。");
            return;
        }

        MoveRecord moveRecord = MoveRecords[MoveRecords.Count - 1];

        // 1. 移除这一手形成的整块
        RemoveGroup(FindPointHead(moveRecord.boardPoint));

        // 2. 恢复这手落子前原有的己方块中的其它子
        foreach (BoardPoint boardPoint in moveRecord.boardPoints)
        {
            if (boardPoint != moveRecord.boardPoint)
            {
                Vector3 point = NumToPoint(boardPoint.me);
                CreateStone(moveRecord.stoneType, boardPoint.targetRotation, point);
                StoneNum++;
                LuoZiLogic(point, false); // 不记录新 MoveRecord
            }
        }

        // 3. 恢复被这一手吃掉的对方所有棋块
        foreach (StoneGroup group in moveRecord.stoneGroups)
        {
            foreach (BoardPoint boardPoint in group.points)
            {
                Vector3 point = NumToPoint(boardPoint.me);
                CreateStone(moveRecord.stoneType % 2 + 1, boardPoint.targetRotation, point);
                StoneNum++;
                LuoZiLogic(point, false);
            }
        }

        // 4. 回退轮次和行棋方
        if (turns > 0)
            turns--;

        BlackTurn = !BlackTurn;

        // 5. 移除这步记录
        MoveRecords.RemoveAt(MoveRecords.Count - 1);

        // 6. 重建打劫历史：只保留当前时间线的局面
        RebuildBoardHistory();
    }
<<<<<<< Updated upstream

    private bool Check_LuoZi(string s)
=======
    private bool Check_LuoZi(int stonetype)
>>>>>>> Stashed changes
    {
        if (((stonetype == 1 && BlackTurn) || (stonetype ==2 && !BlackTurn)))
        {

            hit = GetPosition();
            if (hit.collider == null)
            {
                Debug.Log("当前点击位置不在棋盘上");
                return false;
            }

            if (IsForbiddenPoint(stonetype))
            {
                Debug.Log("此处禁止落子");
                return false;
            }
            return true;
        }
        else
        {
            Debug.Log("不是你的回合");
            return false;
        }
    }

    private bool Check_HuiQi()
    {
<<<<<<< Updated upstream
        if (gameOver)
        {
            Debug.Log("对局已结束，禁止悔棋。");
            return false;
        }

        if (moveIsPass.Count > 0)
=======
        if (MoveRecords.Count > 1)
>>>>>>> Stashed changes
        {
            return true;
        }
        else
        {
            Debug.Log("当前禁止悔棋");
            return false;
        }
    }

    private void RebuildBoardHistory()
    {
        if (boardHistory == null)
            boardHistory = new HashSet<string>();

        boardHistory.Clear();

        // 把初始局面放回去
        if (!string.IsNullOrEmpty(initialBoardState))
        {
            boardHistory.Add(initialBoardState);
        }

        // 再把当前时间线上的所有局面加回去
        foreach (var record in MoveRecords)
        {
            if (!string.IsNullOrEmpty(record.boardState))
            {
                boardHistory.Add(record.boardState);
            }
        }
    }



    private bool IsForbiddenPoint(int stoneType) //落子地方是不是已经有子；落子之后是不是直接死且不提子；是不是劫
    {
        Debug.Log(GetPoint(hit.point));
        if (boardPoints[PointToNum(GetPoint(hit.point))].stoneType != 0)       //是不是有子
        {
            Debug.Log("该位置已经有子");
            return true;
        }
        else if (IsSuicideMove(boardPoints[PointToNum(GetPoint(hit.point))],stoneType))
        {
            Debug.Log("自杀且不吃子");
            return true;
        }
        else if (Check_Jie(stoneType))
        {
            Debug.Log("劫");
            return true;
        }else 
        {
            return false;
        }
    }

    // 在一个 int[] 棋盘上，从 startIndex 开始，搜集一整块及其气
    private void CollectGroupAndLiberties(int[] simStones, int startIndex, int color,
                                        HashSet<int> group, HashSet<int> liberties)
    {
        Stack<int> stack = new Stack<int>();
        stack.Push(startIndex);
        group.Add(startIndex);

        while (stack.Count > 0)
        {
            int idx = stack.Pop();
            BoardPoint p = boardPoints[idx];
            int[] neighbors = { p.up, p.down, p.left, p.right };

            foreach (int n in neighbors)
            {
                if (n == -1) continue;
                int stone = simStones[n];

                if (stone == 0)
                {
                    liberties.Add(n);
                }
                else if (stone == color && !group.Contains(n))
                {
                    group.Add(n);
                    stack.Push(n);
                }
            }
        }
    }

    // 模拟在 index 处下 stoneType，返回落子后的局面字符串
    private string SimulateBoardState(int index, int stoneType)
    {
        int count = boardPoints.Count;
        if (index < 0 || index >= count) return null;

        int[] simStones = new int[count];
        for (int i = 0; i < count; i++)
        {
            simStones[i] = boardPoints[i].stoneType;
        }

        if (simStones[index] != 0)
        {
            // 该点已有棋子，合法性前面已经判断，这里直接返回
            return null;
        }

        int opponent = stoneType % 2 + 1;
        simStones[index] = stoneType;

        // 检查四周对方棋块是否被提子（气为 0）
        BoardPoint placed = boardPoints[index];
        int[] neighbors = { placed.up, placed.down, placed.left, placed.right };

        HashSet<int> visited = new HashSet<int>();

        foreach (int n in neighbors)
        {
            if (n == -1) continue;
            if (simStones[n] == opponent && !visited.Contains(n))
            {
                HashSet<int> group = new HashSet<int>();
                HashSet<int> liberties = new HashSet<int>();

                CollectGroupAndLiberties(simStones, n, opponent, group, liberties);

                // 标记已处理，避免重复
                foreach (int g in group)
                {
                    visited.Add(g);
                }

                // 没有气 => 整块被提
                if (liberties.Count == 0)
                {
                    foreach (int g in group)
                    {
                        simStones[g] = 0;
                    }
                }
            }
        }

        // 自杀规则在 IsSuicideMove 里已处理，这里不再重复判断

        char[] chars = new char[count];
        for (int i = 0; i < count; i++)
        {
            int s = simStones[i];
            if (s < 0) s = 0;
            if (s > 2) s = 2;
            chars[i] = (char)('0' + s);
        }

        return new string(chars);
    }


    // 局面重复打劫判定（位置超级劫）：
    // 模拟当前落子，若生成的整盘局面在历史局面集合中出现过，则判定为打劫，禁止落子。
    // 不区分提一子 / 多子，统一处理。
    private bool Check_Jie(int stoneType)
    {
        // 没有历史记录，不可能打劫
        if (boardHistory == null || boardHistory.Count == 0)
            return false;

        // 当前鼠标点击位置的格点
        Vector3 point = GetPoint(hit.point);
        int index = PointToNum(point);
        if (index < 0)
            return false;

        // 模拟这一步
        string simulatedState = SimulateBoardState(index, stoneType);
        if (string.IsNullOrEmpty(simulatedState))
            return false;

        // 若该局面在历史中出现过，则属于打劫
        return boardHistory.Contains(simulatedState);
    }


    private bool IsSuicideMove(BoardPoint boardPoint,int stoneType)     //是不是自杀行为，死且不提子
    {
        int QiOfSameStoneGroup = 0;

        int[] neighbors = { boardPoint.up, boardPoint.down, boardPoint.left, boardPoint.right };

        // 避免同一个己方块被多次累加
        HashSet<BoardPoint> handledHeads = new HashSet<BoardPoint>();

        foreach (int neighbor in neighbors)
        {
            if (neighbor == -1) continue;

            BoardPoint np = boardPoints[neighbor];
            int tmp = IsSuicideMove(boardPoint, np, stoneType, handledHeads);
            if (tmp == -1)          // 有空、或者能立刻提子 => 非自杀
            {
                return false;
            }
            QiOfSameStoneGroup += tmp;
        }

        return QiOfSameStoneGroup <= 0;
}
    
    private int IsSuicideMove(BoardPoint boardPoint1, BoardPoint boardPoint2, int stoneType, HashSet<BoardPoint> handledHeads)
    {
        if (boardPoint2.stoneType == 0)
        {
            // 相邻为空点 => 有气
            return -1;
        }
        else if (boardPoint2.stoneType != stoneType)
        {
            // 相邻为对方棋子
            StoneGroup enemyGroup = PointHeadToGroup[FindPointHead(boardPoint2)];
            if (enemyGroup.GroupQiCount == 1)
            {
                // 落子后可以提子 => 非自杀
                return -1;
            }
            else
            {
                return 0;
            }
        }
        else
        {
            // 相邻为己方棋子，只需对每个己方块统计一次气
            BoardPoint head = FindPointHead(boardPoint2);
            if (handledHeads.Contains(head)) return 0;
            handledHeads.Add(head);

            StoneGroup myGroup = PointHeadToGroup[head];
            return myGroup.GroupQiCount - 1;
        }
    }

    private void LuoZiLogic(Vector3 point ,bool record)
    {
        BoardPoint boardPoint = boardPoints[PointToNum(point)]; //首先自立为group，并记录在哈希表
        StoneGroup stoneGroup = new StoneGroup(boardPoint);

        LastMove = new MoveRecord(boardPoint.stoneType, boardPoint);     //创建记录

        #region 修正气数量

        int[] neighbors = { boardPoint.up, boardPoint.down, boardPoint.left, boardPoint.right };  

        foreach (int neighbor in neighbors)
        {
            if(neighbor !=-1 && boardPoints[neighbor].stoneType == 0)
            {
                stoneGroup.GroupQiCount++;
                stoneGroup.QiPositions.Add(boardPoints[neighbor]);
            }
        }
        #endregion

        PointToPointHead.Add(boardPoint ,boardPoint);     
        PointHeadToGroup.Add(boardPoint, stoneGroup);


        //遍历上下左右，进行mergeGroup
        if(boardPoint.up!=-1 )
        MergeGroup(boardPoint, boardPoints[boardPoint.up]);
        if(boardPoint.down!=-1)
        MergeGroup(boardPoint, boardPoints[boardPoint.down]);
        if(boardPoint.left!=-1)
        MergeGroup(boardPoint, boardPoints[boardPoint.left]);
        if(boardPoint.right!=-1)
        MergeGroup(boardPoint, boardPoints[boardPoint.right]);

        foreach (BoardPoint boardpoint in PointHeadToGroup[FindPointHead(boardPoint)].points)
        {
            LastMove.boardPoints.Add(boardpoint);
        }

        // 走完这步后的完整局面
        LastMove.boardState = GetBoardState();

        if (record)
        {
            MoveRecords.Add(LastMove);//加到记录中
            boardHistory.Add(LastMove.boardState);
        }
    }

    private void MergeGroup(BoardPoint boardPoint1,BoardPoint boardPoint2)   
    {
        #region 说明
        //对于上下左右，为空不做处理
        //棋子类型不同，则邻居棋子块气减
        //棋子类型相同，则进行合并操作
        #endregion
        BoardPoint Head1 = FindPointHead(boardPoint1);

        if (boardPoint1.stoneType != boardPoint2.stoneType)    //类型不同
        {
            if (boardPoint2.stoneType != 0)   //且不是空
            {
                BoardPoint Head2 = FindPointHead(boardPoint2);
                StoneGroup stoneGroup2 = PointHeadToGroup[Head2];
                if (stoneGroup2.QiPositions.Contains(boardPoint1))
                {
                    stoneGroup2.QiPositions.Remove(boardPoint1);
                }
                stoneGroup2.GroupQiCount = stoneGroup2.QiPositions.Count;

                if(stoneGroup2.GroupQiCount == 0)
                {
                    LastMove.stoneGroups.Add(stoneGroup2);
                    RemoveGroup(Head2);
                }
            }
        }
        else            
        {
            BoardPoint Head2 = FindPointHead(boardPoint2);
            if (Head1 == Head2) return;                         //如果已经是一块了，不操作
            StoneGroup stoneGroup1 = PointHeadToGroup[Head1];   
            StoneGroup stoneGroup2 = PointHeadToGroup[Head2];

            stoneGroup2.points.AddRange(stoneGroup1.points);   //块1的子全都记录到块2中
            stoneGroup2.QiPositions.UnionWith(stoneGroup1.QiPositions); //合并气
            if(stoneGroup2.QiPositions.Contains(boardPoint1))
            stoneGroup2.QiPositions.Remove(boardPoint1);           //落点不再是气
            stoneGroup2.GroupQiCount = stoneGroup2.QiPositions.Count;  //修正气数量

            PointToPointHead[Head1] = Head2;
            PointHeadToGroup.Remove(Head1);
        }
    }

    private BoardPoint FindPointHead(BoardPoint boardPoint)
    {
        if (PointToPointHead[boardPoint] != boardPoint)
        {
            PointToPointHead[boardPoint] = FindPointHead(PointToPointHead[boardPoint]);
        }
        return PointToPointHead[boardPoint];    
    }
    private void RemoveGroup(BoardPoint head)             //块的气为0，触发提子，遍历块中每一个子，为邻居黑子块加气
    {
        StoneGroup stoneGroup = PointHeadToGroup[head];
        PointHeadToGroup.Remove(head);

        foreach (BoardPoint point in stoneGroup.points)
        {
            StoneNum--;

            PointToPointHead.Remove(point);

            Destroy(PointToStone[point]);
            PointToStone.Remove(point);

            int[] neighbors = { point.up, point.down, point.left, point.right };

            foreach (int neighbor in neighbors)
            {
                if(neighbor != -1 && boardPoints[neighbor].stoneType != point.stoneType && boardPoints[neighbor].stoneType!= 0)
                {
                    StoneGroup tmp = PointHeadToGroup[FindPointHead(boardPoints[neighbor])];
                    tmp.QiPositions.Add(point);
                    tmp.GroupQiCount=tmp.QiPositions.Count;
                }
            }

            point.stoneType = 0;
        }
    }
    private void CreateBoardPoint()
    {
        for(int j = -9; j < 10; j++)
        {
            for(int i = -9; i < 10; i++)
            {
                boardPoints.Add(new BoardPoint(0,PointToNum(i,j),PointToNum(i,j+1),PointToNum(i,j-1),PointToNum(i-1,j),PointToNum(i+1,j)));
            }
        }
    }

    private int PointToNum(int i,int j)       
    {

        #region Note
        /*
         * 游戏中，棋盘的坐标格式为（x,0,z）
         * x,z均为-9~9
         * 所以最左下角如下,省略y轴
         *           (-9,-8)
         *  (-10,-9) (-9,-9) (-8,-9)
         *           (-9,-10)
         *  从最左下角的-9,-9开始，加到boardpoints中，对于下标为0
         *  因为-10,出界，所以返回-1，访问时只需查看是否出界即可判断
         *  所以，i也就是x,一个对应1，j也就是z一个对应19
         */

        #endregion

        int num = 0;

        if(i<-9||i>9||j < -9 || j > 9)
        {
            num = -1;
            return num;
        }
        else
        {
            num += i + 9 + (j+9)*19;
        }

        return num;
    }

    private int PointToNum(Vector3 point)
    {
        int i = (int)point.x;  
        int j = (int)point.z;
        return PointToNum(i, j);
    }

    private Vector3 NumToPoint(int num)
    {
        if (num < 0 || num >= 19 * 19)
        {
            Debug.Log("越界");
        }

        int j = num / 19;   
        int i = num % 19;   

        int x = i - 9;
        int z = j - 9;

        return new Vector3(x, 0, z);
    }

    // 把当前棋盘状态（每个 BoardPoint 的 stoneType）压成字符串
    private string GetBoardState()
    {
        int count = boardPoints.Count;
        char[] chars = new char[count];

        for (int i = 0; i < count; i++)
        {
            int s = boardPoints[i].stoneType; // 0 / 1 / 2
            if (s < 0) s = 0;
            if (s > 2) s = 2;
            chars[i] = (char)('0' + s);
        }

        return new string(chars);
    }



    private void CheckStone()                  //输出当前棋盘上，不为空子的对应位置在boardpoints的下标
    {
        for(int i=0;i<boardPoints.Count;i++)
        {
            if (boardPoints[i].stoneType!=0)
            {
                Debug.Log(i);
            }
        }
    }

    private void CheckGroup() //点击一个子，会显示这个子所在的group，并且debug气数量
    {
        hit = GetPosition();
        if(hit.collider == null)
        {
            Debug.Log("当前位置不在棋盘上");
            return;
        }
        int num = PointToNum(GetPoint(hit.point));

        if (boardPoints[num].stoneType!=0)
        {
            StoneGroup stoneGroup = PointHeadToGroup[FindPointHead(boardPoints[num])];

            Debug.Log("Qi ="+  stoneGroup.GroupQiCount);

            foreach(BoardPoint point in stoneGroup.points)
            {
                GameObject stone = PointToStone[point];
                MeshRenderer mr = stone.GetComponent<MeshRenderer>();   
                StartCoroutine(ChangeMaterical(mr,mr.material,tmp,3f));
            }
        }
    }

    private IEnumerator ChangeMaterical(MeshRenderer mr,Material ori,Material tmp,float time)
    {
        mr.material = tmp;
        yield return new WaitForSeconds(time);
        mr.material = ori;
    }

<<<<<<< Updated upstream
    // 计算当前局面的胜负（简单中国数子数地规则，不含贴目）
    public void CalculateGameResult()
    {
        // 1. 统计棋子数量
        blackStonesCount = 0;
        whiteStonesCount = 0;

        for (int i = 0; i < boardPoints.Count; i++)
        {
            int s = boardPoints[i].stoneType;
            if (s == 1) blackStonesCount++;
            else if (s == 2) whiteStonesCount++;
        }

        // 2. 统计地（空点区域）
        blackTerritoryCount = 0;
        whiteTerritoryCount = 0;

        int totalPoints = boardPoints.Count;
        bool[] visited = new bool[totalPoints];

        for (int i = 0; i < totalPoints; i++)
        {
            if (visited[i]) continue;
            if (boardPoints[i].stoneType != 0) continue;   // 不是空交叉点

            // 对一个空区域做 BFS
            int regionSize = 0;
            bool adjBlack = false;
            bool adjWhite = false;

            Queue<int> q = new Queue<int>();
            q.Enqueue(i);
            visited[i] = true;

            while (q.Count > 0)
            {
                int idx = q.Dequeue();
                regionSize++;

                BoardPoint p = boardPoints[idx];
                int[] neighbors = { p.up, p.down, p.left, p.right };

                foreach (int n in neighbors)
                {
                    if (n == -1) continue;

                    int s = boardPoints[n].stoneType;
                    if (s == 0)
                    {
                        if (!visited[n])
                        {
                            visited[n] = true;
                            q.Enqueue(n);
                        }
                    }
                    else if (s == 1)
                    {
                        adjBlack = true;
                    }
                    else if (s == 2)
                    {
                        adjWhite = true;
                    }
                }
            }

            // 只有一方相邻，则这一整块空交叉点算作该方地
            if (adjBlack && !adjWhite)
            {
                blackTerritoryCount += regionSize;
            }
            else if (adjWhite && !adjBlack)
            {
                whiteTerritoryCount += regionSize;
            }
            // 若同时接触黑、白或都不接触，则为中立地，不计入任何一方
        }

        int blackScore = blackStonesCount + blackTerritoryCount;
        float whiteScore = whiteStonesCount + whiteTerritoryCount + komi;

        if (blackScore > whiteScore)
        {
            gameResult = GameResult.BlackWin;
        }
        else if (whiteScore > blackScore)
        {
            gameResult = GameResult.WhiteWin;
        }
        else
        {
            gameResult = GameResult.Draw;
        }

        Debug.Log($"黑棋：子数 {blackStonesCount}，地 {blackTerritoryCount}，总分 {blackScore}");
        Debug.Log($"白棋：子数 {whiteStonesCount}，地 {whiteTerritoryCount}，贴目 {komi}，总分 {whiteScore}");
        Debug.Log($"结果：{gameResult}");
    }


    // 一方弃权（Pass）一手
    public void PassTurn()
    {
        if (gameOver)
        {
            Debug.Log("对局已结束，不能再弃权。");
            return;
        }

        moveIsPass.Add(true); //这一手是“弃权”


        // 记录一手弃权
        consecutivePasses++;

        // 切换行棋方
        BlackTurn = !BlackTurn;
        turns++;

        Debug.Log($"玩家弃权，当前连续弃权次数：{consecutivePasses}");

        // 若连续弃权达到 2 次，则自动终局并结算
        if (consecutivePasses >= 2)
        {
            Debug.Log("双方连续弃权，对局结束，开始结算。");
            CalculateGameResult();
            gameOver = true;
        }
    }


}
=======
    public void SetOnline(bool IsOnline,int palyerType)
    {
        this.IsOnline=IsOnline;
        this.PlayerType = palyerType;
    }
}
>>>>>>> Stashed changes
