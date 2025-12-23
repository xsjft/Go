using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;
using System.IO;   // 提供 File, FileInfo, StreamReader 等
using System.Data;
using System.Drawing;

public class PlayerFor3D : MonoBehaviour
{
    [Header("判断点击位置")]
    [SerializeField] private LayerMask whatIsBoard;
    private RaycastHit hit;

    [Header("棋子,棋盘信息")]
    [SerializeField] private MeshFilter meshFilter;  // 棋盘网格
    [SerializeField] private GameObject BlackStone;
    [SerializeField] private GameObject WhiteStone;
    [SerializeField] private Vector3 StoneSize;
    [SerializeField] private int StoneNum = 0;
    private List<GameObject> allStone = new List<GameObject>(); // 棋子对象列表

    private class BoardPoint
    {
        public int stoneType;  // 0=空,1=黑,2=白
        public int me;
        public List<int> neighbors;
        public BoardPoint(int stoneType, int me)
        {
            this.stoneType = stoneType;
            this.me = me;
            this.neighbors = new List<int>();
        }
    }

    public string csvPath = "Assets/Model/p.csv";//交叉点坐标信息
    private List<BoardPoint> boardPoints = new List<BoardPoint>();//存储棋盘交叉点
    private Dictionary<int,Vector3> NumToPoint = new Dictionary<int,Vector3>(); //从boardpoints的下标转到对应的坐标

    void Start()
    {
        LoadBoardFromCSV();//初始化交叉点信息
        Debug.Log("BoardPoint 构建完成");
    }

    private void Update()
    {
        if(Input.GetMouseButtonDown(0)) 
        {
            test();
        }
    }

    void LoadBoardFromCSV()
    {
        var lines = File.ReadAllLines(csvPath);

        for (int i = 1; i < lines.Length; i++)  // 从第2行开始，跳过表头
        {
            var parts = lines[i].Split(',');

            int index = int.Parse(parts[0]);
            float x = float.Parse(parts[1]);
            float y = float.Parse(parts[2]);
            float z = float.Parse(parts[3]);

            Vector3 pos = new Vector3(x, y, z);
            
            BoardPoint bp = new BoardPoint(0, index);

            for (int n = 4; n < parts.Length; n++)
            {
                if (int.TryParse(parts[n].Trim(), out int neighborIndex))
                {
                    bp.neighbors.Add(neighborIndex);
                    Debug.Log($"顶点 {index} 邻居: {neighborIndex}");
                }
            }

            boardPoints.Add(bp);
            NumToPoint[index] = pos;
        }
    }

    private void test()//用于测试交叉点信息是否正确，点击棋盘对应位置生成棋子及邻居棋子
    {
        hit = GetPosition();

        int closestIndex = PointToNum(hit.point);

        create(NumToPoint[closestIndex]);

        foreach(var Index in boardPoints[closestIndex].neighbors)
        {
            create(NumToPoint[Index]);
        }
    }

    private RaycastHit GetPosition()              //获取鼠标在球面上碰撞点信息
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;

        Physics.Raycast(ray, out hit, Mathf.Infinity, whatIsBoard);

        return hit;
    }

    private int PointToNum(Vector3 point)   //找到点击点对应的最近的交叉点下标
    {
        int closestIndex = -1;
        float minDistanceSqr = float.MaxValue;

        foreach (KeyValuePair<int, Vector3> kvp in NumToPoint)
        {
            int index = kvp.Key;
            Vector3 localPos = kvp.Value;                // 存储的局部坐标
            Vector3 worldPos = transform.TransformPoint(localPos); // 转换为世界坐标

            float distanceSqr = (worldPos - point).sqrMagnitude;
            if (distanceSqr < minDistanceSqr)
            {
                minDistanceSqr = distanceSqr;
                closestIndex = index;
            }

        }

        return closestIndex; 
    }

    private void create(Vector3 position)
    {
        // 将存储的局部坐标转换为世界坐标
        Vector3 worldPos = transform.TransformPoint(position);

        // 根据碰撞点法线生成朝向
        Quaternion targetRotation = Quaternion.LookRotation(hit.normal, Vector3.forward);

        // 实例化棋子
        GameObject qizi = Instantiate(BlackStone, worldPos, targetRotation, transform);

        // 设置棋子大小
        qizi.transform.localScale = StoneSize;
    }

}
