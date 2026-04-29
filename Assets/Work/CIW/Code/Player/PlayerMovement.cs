using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Work.CIW.Code.Grid;
using Work.CUH.Code.Commands;
using Work.CUH.Code.Test;

namespace Work.CIW.Code.Player
{
    #region Interfaces

    public interface IGridDataService
    {
        bool CanMoveTo(Vector3Int curPos, Vector3Int dir, out Vector3Int targetPos);
        void UpdateObjectPosition(GridObjectBase movingObj, Vector3Int oldPos, Vector3Int newPos);
        void SetObjectInitialPosition(GridObjectBase obj, Vector3Int initPos);
    }

    public interface IMovement
    {
        void HandleInput(Vector2 input);

        bool StartMove(Vector3Int direction);
    }

    #endregion

    public class PlayerMovement : MonoBehaviour, ICommandable, IMovement, IMoveableTest
    {
        [Header("Dependencies - DIP")]
        [SerializeField] MonoBehaviour gridServiceMono;
        IGridDataService _gridService;
        GridObjectBase _gridObject;

        bool _isMoving = false;
        bool _hasArrived = false;

        [Header("Movement")]
        [SerializeField] float moveTime = 0.15f;

        [Header("Stair Collision Setting")]
        [SerializeField] LayerMask whatIsStair;

        [SerializeField] LayerMask whatIsArrival;

        public bool isMoving
        {
            get => _isMoving;
            set => _isMoving = value;
        }

        Player _player;

        protected void Awake()
        {
            if (gridServiceMono is IGridDataService service)
            {
                _gridService = service;
            }
            else
            {
                Debug.LogError("IGridDataService dependency not met. Assign GridSystem component to 'gridServiceMono'.");
                enabled = false;
            }

            _gridObject = GetComponent<GridObjectBase>();
            if (_gridObject == null)
            {
                Debug.LogError("Player object must inherit from GridObjectBase.");
                enabled = false;
            }
        }

        protected void Start()
        {
            Vector3 curWorldPos = transform.position;
            Vector3Int initGridPos = new Vector3Int(Mathf.RoundToInt(curWorldPos.x), Mathf.RoundToInt(curWorldPos.y), Mathf.RoundToInt(curWorldPos.z));

            _gridObject.CurrentGridPosition = initGridPos;
            _gridService.SetObjectInitialPosition(_gridObject, initGridPos);
            _gridObject.OnCellOccupied(initGridPos);

            transform.position = new Vector3(initGridPos.x, initGridPos.y, initGridPos.z);
        }

        #region Player Movement

        // 입력 처리는 Player에서 해주니, 더 이상 필요 없다.
        public void HandleInput(Vector2 input)
        {
            if (_hasArrived) return;

            StartMoveLogic(input);
        }

        private Vector3Int GetDirection(Vector2 input)
        {
            if (input.y > 0.5f) return Vector3Int.forward;
            if (input.y < -0.5f) return Vector3Int.back;

            if (input.x > 0.5f) return Vector3Int.right;
            if (input.x < -0.5f) return Vector3Int.left;
            
            return Vector3Int.zero;
        }

        private IEnumerator MoveRoutine(Vector3Int targetPos)
        {
            Player player = GetComponent<Player>();
            if (player != null)
            {
                player.ChangeState("MOVE");
            }

            _isMoving = true;
            Vector3Int oldPos = _gridObject.CurrentGridPosition;

            float startWorldY = oldPos.y;
            Vector3 start = new Vector3(oldPos.x, startWorldY, oldPos.z);

            transform.position = start;

            float targetWorldY = targetPos.y;
            Vector3 finalWorldPos = new Vector3(targetPos.x, targetWorldY, targetPos.z);

            float elapsed = 0f;
            while (elapsed < moveTime)
            {
                transform.position = Vector3.Lerp(start, finalWorldPos, elapsed / moveTime);
                elapsed += Time.deltaTime;
                yield return null;
            }

            // 애니메이션 종료 후 최종 위치 확정
            transform.position = finalWorldPos;

            // GridSystem에 오프셋이 없는 순수한 Grid 좌표(targetPos)를 전달
            _gridService.UpdateObjectPosition(_gridObject, oldPos, targetPos);

            _isMoving = false;

            if (player != null)
            {
                player.ChangeState("IDLE");
            }
        }

        public void StartMoveLogic(Vector2 dir)
        {
            if (_isMoving) return;

            Vector3Int movementDir = GetDirection(dir);
            if (movementDir == Vector3Int.zero) return;

            StartMove(movementDir);
        }

        public bool StartMove(Vector3Int direction)
        {
            if (_hasArrived) return false;
            if (_isMoving) return false;

            if (CheckForArrival(direction)) return true;
            if (CheckForStairs(direction)) return true;

            if (_gridService.CanMoveTo(_gridObject.CurrentGridPosition, direction, out Vector3Int targetPos))
            {
                Vector3 worldDirection = new Vector3(direction.x, 0, direction.z);

                if (worldDirection != Vector3.zero)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(worldDirection, Vector3.up);
                    transform.rotation = targetRotation;
                }

                StartCoroutine(MoveRoutine(targetPos));
                return true;
            }

            return false;
        }

        #endregion

        #region Check Stairs

        private bool CheckForStairs(Vector3Int dir)
        {
            Vector3Int targetGridPos = _gridObject.CurrentGridPosition + dir;
            Vector3 boxCenter = new Vector3(targetGridPos.x, targetGridPos.y + 0.5f, targetGridPos.z);
            Vector3 boxHalfExtents = new Vector3(0.49f, 0.49f, 0.49f);

            Collider[] hits = Physics.OverlapBox(boxCenter, boxHalfExtents, Quaternion.identity, whatIsStair);

            if (hits.Length > 0)
            {
                Debug.Log("Stair 감지");

                if (hits[0].TryGetComponent(out StairTrigger stair))
                {
                    Vector3Int teleportPos = new Vector3Int(_gridObject.CurrentGridPosition.x, stair.GetTargetY(), _gridObject.CurrentGridPosition.z);

                    TeleportToFloor(teleportPos, dir);

                    return true;
                }
            }

            return false;
        }

        private void TeleportToFloor(Vector3Int targetPos, Vector3Int dir)
        {
            Vector3Int oldPos = _gridObject.CurrentGridPosition;
            _gridService.UpdateObjectPosition(_gridObject, oldPos, targetPos);

            float targetWorldY = targetPos.y;
            Vector3 finalWorldPos = new Vector3(targetPos.x, targetWorldY, targetPos.z);
            transform.position = finalWorldPos;

            Debug.Log($"텔포 시킴. 현재 위치는 {_gridObject.CurrentGridPosition}");

            Vector3Int effectiveDir = dir;
            if (dir.y < 0)
            {
                effectiveDir.x *= -1;
                effectiveDir.z *= -1;
            }


            if (_gridService.CanMoveTo(targetPos, effectiveDir, out Vector3Int finalMovePos))
            {
                _gridService.UpdateObjectPosition(_gridObject, targetPos, finalMovePos);
                    
                float finalWorldY = finalMovePos.y;
                Vector3 finalFinalWorldPos = new Vector3(finalMovePos.x, finalWorldY, finalMovePos.z);
                transform.position = finalFinalWorldPos;

                Debug.Log($"텔포 후 강제 이동. 최종 위치는 {_gridObject.CurrentGridPosition}");
            }
            else
            {
                Debug.LogWarning("텔레포트 후 강제 이동 실패. 다음 칸이 막혀있거나 경계 밖. 계단에 남아있을 수 있습니다.");
            }
        }

        private bool CheckForArrival(Vector3Int dir)
        {
            Vector3Int targetGridPos = _gridObject.CurrentGridPosition + dir;
            Vector3 rayOrigin = new Vector3(targetGridPos.x, targetGridPos.y + 5f, targetGridPos.z);
            Vector3 rayDirection = Vector3.down;

            float maxDistance = 6f;

            if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit hit, maxDistance, whatIsArrival))
            {
                if (hit.collider.GetComponent<ArrivalTrigger>() != null)
                {
                    Debug.Log("도착했습니다!");

                    _hasArrived = true;
                    StartCoroutine(MoveRoutine(targetGridPos));
                    return true;
                }
            }

            return false;
        }

        #endregion
    }
}