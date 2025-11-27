using System;
using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


public class PlayerFor2D : MonoBehaviour
{
    [Header("判断点击位置")]
    [SerializeField] private LayerMask whatIsBoard;
    RaycastHit hit ;

    [Header("棋子信息")]
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private GameObject TempStone;
    [SerializeField] private int StoneNum = 0;
    private List<GameObject> allStone = new List<GameObject>();       //棋子对象列表
    [SerializeField] private Vector3 StoneSize;

    [Header("轮次信息")]
    [SerializeField] int turns;       //多少轮
    [SerializeField] bool BlackTurn;    //是不是黑棋回合

    [SerializeField] private Material tmp;


    private class BoardPoint
    {
        public int stoneType;          // 0,1,2 空，黑，白
        public int up;                //所有棋子存储在list中，记录id即可
        public int down;
        public int left;
        public int right;

        public BoardPoint(int stoneType, int up, int down, int left, int right)
        {
            this.stoneType = stoneType;
            this.up = up;
            this.down = down;
            this.left = left;
            this.right = right;
        }
    }

    private List<BoardPoint> boardPoints = new List<BoardPoint>();    

    private class StoneGroup
    {
        public List<BoardPoint> points;
        public int GroupQiCount;

        public StoneGroup(BoardPoint point)
        {
            points = new List<BoardPoint>();
            points.Add(point);
            GroupQiCount = 0;
        }
    }

    private List<StoneGroup> WhiteStoneGroups = new List<StoneGroup>();
    private List<StoneGroup> BlackStoneGroups = new List<StoneGroup>();

    //类似并查集，点指向头，然后头指向块，点指向物体
    //点指向头，每个点指向所属块中的头，自己是头指向自己
    private Dictionary<BoardPoint, BoardPoint>PointToPointHead = new Dictionary<BoardPoint, BoardPoint>();     
    private Dictionary<BoardPoint,StoneGroup> PointHeadToGroup = new Dictionary<BoardPoint, StoneGroup>();
    private Dictionary<BoardPoint,GameObject> PointToStone = new Dictionary<BoardPoint, GameObject>();
    /*
     * 每次落子修改相邻黑棋白棋块气的数量
     * 每次落子，理论上只影响上下左右四个地方
     * 提子遍历所有块？
     * 悔棋或其他功能需要考虑
     */

    void Awake()
    {
        CreateBoardPoint(); 
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Q) && Check_LuoZi("Black"))
        {
            LuoZiAction("Black");
        }

        if (Input.GetKeyDown(KeyCode.E) && Check_LuoZi("White"))
        {
            LuoZiAction("White");

        }

        if (Input.GetMouseButtonDown(2) && Check_HuiQi())
        {
            HuiQi();          
        }

        if (Input.GetMouseButtonDown(0))
        {
            CheckStone();             //输出当前棋盘的棋子位置，在boardpoints的下标
        }

        if (Input.GetMouseButtonDown(1))
        {
            CheckGroup();          //点击一个棋子，输出棋子所在块的气，并改变这块棋子颜色
        }
    }

    private void LuoZiAction(string s)
    {
        if (hit.collider != null)          //鼠标在没在球面上
        {
            Vector3 normal = hit.normal;
            Quaternion targetRotation = Quaternion.LookRotation(normal, Vector3.forward);

            Vector3 point = GetPoint(hit.point);

            Debug.Log(point);
            Debug.Log(PointToNum(point));

            GameObject qizi;
            if (s == "Black")
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
            allStone.Add(qizi);
            PointToStone.Add(boardPoints[PointToNum(point)],qizi);

            StoneNum++;
            turns++;
            BlackTurn = !BlackTurn;
            LuoZiLogic(point);
        }
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

    private void HuiQi()                //悔棋
                                         // 如果上轮没有提子之类，直接销毁最后一个棋子
                                         //如果上轮触发了提子之类，还需要恢复被提的子（额外的容器记录）
                                        //
    {
        Destroy(allStone[StoneNum - 1]);
        allStone.RemoveAt(StoneNum - 1);
        StoneNum--;
        turns--;
        BlackTurn = !BlackTurn;
    }
    private bool Check_LuoZi(string s)
    {
        hit = GetPosition();
        if (((s == "Black" && BlackTurn) || (s == "White" && !BlackTurn)))
        {
            if (IsForbiddenPoint())
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
        if (StoneNum > 0)
        {
            return true;
        }
        else
        {
            Debug.Log("当前禁止悔棋");
            return false;
        }
    }

    private bool IsForbiddenPoint() //落子地方是不是已经有子；落子之后是不是直接死且不提子；是不是劫
    {
        if (boardPoints[PointToNum(GetPoint(hit.point))].stoneType == 0)       //是不是有子
        {
            return false;
        }
        else
        {
            return true;
        }
    }

    private void TiZi()             //落子之后循环判断对方棋子的气
    {

    }

    private void Check_Qi()         //判断气
    {

    }

    private void LuoZiLogic(Vector3 point)
    {
        BoardPoint boardPoint = boardPoints[PointToNum(point)]; //首先自立为group，并记录在哈希表
        StoneGroup stoneGroup = new StoneGroup(boardPoint);

        #region 修正气数量
        if (boardPoint.up != -1 && boardPoints[boardPoint.up].stoneType==0)
            stoneGroup.GroupQiCount ++;
        if (boardPoint.down != -1 && boardPoints[boardPoint.down].stoneType == 0)
            stoneGroup.GroupQiCount++;
        if (boardPoint.left != -1 && boardPoints[boardPoint.left].stoneType == 0)
            stoneGroup.GroupQiCount++;
        if (boardPoint.right != -1 && boardPoints[boardPoint.right].stoneType == 0)
            stoneGroup.GroupQiCount++;
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
                stoneGroup2.GroupQiCount--;
            }
        }
        else            
        {
            BoardPoint Head2 = FindPointHead(boardPoint2);
            if (Head1 == Head2) return;                         //如果已经是一块了，不操作
            StoneGroup stoneGroup1 = PointHeadToGroup[Head1];   
            StoneGroup stoneGroup2 = PointHeadToGroup[Head2];

            stoneGroup2.points.AddRange(stoneGroup1.points);   //块1的子全都记录到块2中

            int OverlapQi = 0;                                   //判断是否存在公共气
            OverlapQi += CalculateOverlapQi(boardPoint1.up,Head2);
            OverlapQi += CalculateOverlapQi(boardPoint1.down, Head2);
            OverlapQi += CalculateOverlapQi(boardPoint1.left, Head2);
            OverlapQi += CalculateOverlapQi(boardPoint1.right, Head2);

            // group2 原有气 + group1 气 - 两组公共气 - 落点本身占用 group2 的气
            stoneGroup2.GroupQiCount += stoneGroup1.GroupQiCount - OverlapQi - 1;

            PointToPointHead[Head1] = Head2;
            PointHeadToGroup.Remove(Head1);
        }
    }

    private int CalculateOverlapQi(int num,BoardPoint head)
    {
        #region 说明
        //对于上下左右
        //1、不出界且必须是空
        //2、空相邻的point的位置如果不是空，或者没出界，而且子的块的头与块2的头一样
        //那就说明，块1的这个气也属于块2
        #endregion
        int overlapQi = 0;

        if (num != -1 && boardPoints[num].stoneType == 0)
        {
            BoardPoint boardPoint = boardPoints[num];
            if (boardPoint.up != -1 && boardPoints[boardPoint.up].stoneType!=0 && FindPointHead(boardPoints[boardPoint.up]) == head)
            {
                return 1;
            }
            if (boardPoint.down != -1 && boardPoints[boardPoint.down].stoneType != 0 && FindPointHead(boardPoints[boardPoint.down])== head)
            {
                return 1;
            }
            if (boardPoint.left != -1 && boardPoints[boardPoint.left].stoneType != 0 && FindPointHead(boardPoints[boardPoint.left]) == head)
            {
                return 1;
            }
            if (boardPoint.right != -1 && boardPoints[boardPoint.right].stoneType != 0&& FindPointHead(boardPoints[boardPoint.right]) == head)
            {
                return 1;
            }
        }

        return overlapQi;
    }

    private BoardPoint FindPointHead(BoardPoint boardPoint)
    {
        if (PointToPointHead[boardPoint] != boardPoint)
        {
            PointToPointHead[boardPoint] = FindPointHead(PointToPointHead[boardPoint]);
        }
        return PointToPointHead[boardPoint];    
    }

    private void CreateBoardPoint()
    {
        for(int j = -9; j < 10; j++)
        {
            for(int i = -9; i < 10; i++)
            {
                boardPoints.Add(new BoardPoint(0,PointToNum(i,j+1),PointToNum(i,j-1),PointToNum(i-1,j),PointToNum(i+1,j)));
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
        int num = PointToNum(GetPoint(hit.point));

        if (boardPoints[num].stoneType!=0)
        {
            StoneGroup stoneGroup = PointHeadToGroup[FindPointHead(boardPoints[num])];

            Debug.Log("Qi ="+  stoneGroup.GroupQiCount);

            foreach(BoardPoint point in stoneGroup.points)
            {
                GameObject stone = PointToStone[point];
                MeshRenderer mr = stone.GetComponent<MeshRenderer>();   
                StartCoroutine(ChangeMaterical(mr,mr.material,tmp,10f));
            }
        }
    }

    private IEnumerator ChangeMaterical(MeshRenderer mr,Material ori,Material tmp,float time)
    {
        mr.material = tmp;
        yield return new WaitForSeconds(time);
        mr.material = ori;
    }
}
