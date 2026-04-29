using System;
using System.Collections.Generic;
using UnityEngine;
using Work.CIW.Code.Player;

namespace Work.CIW.Code.Grid
{
    /// <summary>
    /// 그리드 공간 데이터를 관리하는 중앙 시스템
    /// </summary>
    public class GridSystem : MonoBehaviour, IGridDataService
    {
        public static GridSystem Instance { get; private set; }

        [Header("Group Setup")]
        [SerializeField] GridCell cellPrefab;
        [SerializeField] private Vector3Int gridCenter = new Vector3Int(0, 0, 0);
        [SerializeField] Vector3Int gridSize = new Vector3Int(10, 10, 10);
        [SerializeField] List<Transform> gridParent; // 생성될 Cell들이 배치될 부모 오브젝트
        [SerializeField] GameObject cellObjPrefab;
        [SerializeField] Vector3 gridStartWorldPosition = Vector3.zero;
        float _cellSize = 1f;

        [Header("Movement Check Data")]
        [SerializeField] LayerMask whatIsWalkable;

        [Header("Dependencies")]
        [SerializeField] MonoBehaviour turnServiceMono;
        ITurnService _turnService;

        Dictionary<Vector3Int, GridCell> _gridMap = new Dictionary<Vector3Int, GridCell>();

        private void Awake()
        {
            if (Instance == null)
                Instance = this;
            else
                Destroy(Instance);

            if (turnServiceMono is ITurnService service)
            {
                _turnService = service;
            }
            else
            {
                Debug.LogError("ITurnService dependency not met. Assign TurnSystemAdapter to turnServiceMono.");
            }

            InitializeCellSize();
            InitializeGrid();
        }

        /// <summary>
        /// Cell 프리팹의 크기를 기반으로 Grid의 셀 크기 계산
        /// </summary>
        private void InitializeCellSize()
        {
            if (cellObjPrefab == null)
            {
                Debug.LogError("Cell Prefab 빼먹었다 멍청한 놈아");
                return;
            }

            gridStartWorldPosition = Vector3.zero;
        }

        // Cell 프리팹의 크기를 기반으로 Grid의 셀 크기 계산
        private void InitializeGrid()
        {
            _gridMap = new Dictionary<Vector3Int, GridCell>();

            int startX = gridCenter.x - (gridSize.x / 2);
            int startZ = gridCenter.z - (gridSize.z / 2);

            foreach (Transform parent in gridParent)
            {
                float currentFloorY = parent.position.y;

                for (int x = 0; x < gridSize.x; x++)
                {
                    for (int z = 0; z < gridSize.z; z++)
                    {
                        Vector3Int pos = new Vector3Int(startX + x, Mathf.RoundToInt(currentFloorY), startZ + z);

                        float worldX = pos.x * _cellSize;
                        float worldY = currentFloorY + gridStartWorldPosition.y;
                        float worldZ = pos.z * _cellSize;

                        Vector3 worldPos = new Vector3(worldX, worldY, worldZ);

                        Quaternion rotation = Quaternion.Euler(-90f, 0f, 0f);

                        GameObject visualObj = Instantiate(cellObjPrefab, worldPos, rotation);
                        visualObj.transform.SetParent(parent, true);

                        GridCell newCell = visualObj.AddComponent<GridCell>();

                        newCell.InitializeCoodinates(pos);

                        if (!_gridMap.ContainsKey(pos))
                        {
                            _gridMap.Add(pos, newCell);
                        }
                        else
                        {
                            Debug.LogWarning($"Duplicate cell position found at {pos}. Skipping.");
                        }
                    }
                }
            }
            
        }

        public bool CanMoveTo(Vector3Int curPos, Vector3Int dir, out Vector3Int targetPos)
        {
            targetPos = curPos + dir;

            if (!_gridMap.TryGetValue(targetPos, out GridCell targetCell))
            {
                return false;
            }
            if (!targetCell.IsWalkable)
            {
                return false;
            }
            if (targetCell.IsOccupant)
            {
                return false;
            }

            Vector3 rayOrigin = new Vector3(targetPos.x, targetPos.y + 0.5f, targetPos.z);
            Vector3 rayDir = Vector3.down;
            float maxDistance = targetPos.y + 6f;

            if (Physics.Raycast(rayOrigin, rayDir, out RaycastHit hit, maxDistance, whatIsWalkable))
            {
                if (Mathf.Abs(hit.point.y - targetPos.y) < 0.1f)
                {
                    return true;
                }
            }

            return false;
        }

        public void UpdateObjectPosition(GridObjectBase movingObj, Vector3Int oldPos, Vector3Int newPos)
        {
            if (_gridMap.TryGetValue(oldPos, out GridCell oldCell))
            {
                if (oldCell.Occupant == movingObj)
                {
                    oldCell.SetOccupant(null);
                    
                    movingObj.OnCellDeoccupied();
                }
            }
            
            if (_gridMap.TryGetValue(newPos, out GridCell newCell))
            {
                newCell.SetOccupant(movingObj);
                
                movingObj.OnCellOccupied(newPos);
            }
        }

        public void SetObjectInitialPosition(GridObjectBase obj, Vector3Int initPos)
        {
            if (_gridMap.TryGetValue(initPos, out GridCell initCell))
            {
                if (!initCell.IsOccupant)
                {
                    initCell.SetOccupant(obj);

                    obj.OnCellOccupied(initPos);
                }
            }
        }

        public void RemoveObjectPosition(GridObjectBase obj, Vector3Int targetPos)
        {
            if (_gridMap.TryGetValue(targetPos, out GridCell oldCell))
            {
                if (oldCell.Occupant != null)
                {
                    oldCell.SetOccupant(null); 
                    obj.OnCellDeoccupied();
                }
            }
        }
        
        public GridCell GetCell(Vector3Int pos)
        {
            _gridMap.TryGetValue(pos, out GridCell cell);
            return cell;
        }
    }
}