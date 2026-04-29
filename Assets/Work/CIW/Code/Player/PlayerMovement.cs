using Ami.BroAudio;
using Chuh007Lib.Dependencies;
using Chuh007Lib.ObjectPool.Runtime;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using Work.CIW.Code.Camera;
using Work.CIW.Code.Camera.Events;
using Work.CIW.Code.Grid;
using Work.CUH.Chuh007Lib.EventBus;
using Work.CUH.Code.Commands;
using Work.CUH.Code.GameEvents;
using Work.CUH.Code.Test;
using Work.ISC.Code.Effects;
using Work.PSB.Code.Player;

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
        [SerializeField] private MonoBehaviour gridServiceMono;
        public IGridDataService gridService;
        private GridObjectBase _gridObject;

        [Header("Camera Transition")]
        [SerializeField] FloorTransitionManager floorTransitionManager;

        Player _playerCode;

        private bool _isMoving = false;
        bool _hasArrived = false;

        [Header("Movement")]
        [SerializeField] private float moveTime = 0.15f;

        [Header("Stair Collision Setting")]
        [SerializeField] private LayerMask whatIsStair;
        [SerializeField] private LayerMask whatIsArrival;

        [Header("Object Pooling")]
        [SerializeField] PoolingItemSO moveEffect;

        [Header("Sound Setting")]
        [SerializeField] private SoundID moveSound;
        [SerializeField] private SoundID deathSound;

        [Inject] PoolManagerMono _poolManager;

        public bool isMoving
        {
            get => _isMoving;
            set => _isMoving = value;
        }

        protected void Awake()
        {
            if (gridServiceMono is IGridDataService service)
            {
                gridService = service;
            }
            else
            {
                enabled = false;
            }

            _gridObject = GetComponent<GridObjectBase>();
            _playerCode = GetComponent<Player>();
            if (_gridObject == null || _playerCode == null)
            {
                enabled = false;
            }
        }

        protected void Start()
        {
            Vector3 curWorldPos = transform.position;
            Vector3Int initGridPos = new Vector3Int(Mathf.RoundToInt(curWorldPos.x), Mathf.RoundToInt(curWorldPos.y), Mathf.RoundToInt(curWorldPos.z));

            _gridObject.CurrentGridPosition = initGridPos;
            gridService.SetObjectInitialPosition(_gridObject, initGridPos);
            _gridObject.OnCellOccupied(initGridPos);

            transform.position = new Vector3(initGridPos.x, initGridPos.y, initGridPos.z);
        }

        #region Player Movement

        public void HandleInput(Vector2 input)
        {
            if (_isMoving) return;
            if (_hasArrived) return;

            Vector3Int dir = GetDirection(input);
            if (dir == Vector3Int.zero) return;

            if (CheckForStairs(dir)) return;

            Vector3Int curPos = _gridObject.CurrentGridPosition;
            if (gridService.CanMoveTo(curPos, dir, out Vector3Int targetPos))
            {
                StartMoveLogic(input);
                gridService.UpdateObjectPosition(_gridObject, _gridObject.CurrentGridPosition, targetPos);
            }
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
            if (_playerCode != null)
            {
                _playerCode.SetInputLockState(true);
                _playerCode.ChangeState("MOVE");
            }

            _isMoving = true;
            CreateEffect();
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

            transform.position = finalWorldPos;

            _isMoving = false;

            if (_playerCode != null)
            {
                _playerCode.SetInputLockState(false);
                _playerCode.ChangeState("IDLE");

                if (!_playerCode.IsDead)
                {
                    if (_playerCode.turnAdapter != null && !_playerCode.turnAdapter.HasTurnRemaining)
                    {
                        _playerCode.HandleTurnZero();
                    }
                }
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

            if (CheckForStairs(direction)) return true;

            if (gridService.CanMoveTo(_gridObject.CurrentGridPosition, direction, out Vector3Int targetPos))
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

        public async void CreateEffect()
        {
            PoolingEffect effect = _poolManager.Pop<PoolingEffect>(moveEffect);
            effect.PlayVFX(transform.position + new Vector3(0f, 0.1f, 0f));
            BroAudio.Play(moveSound);
            await Awaitable.WaitForSecondsAsync(2f);
            _poolManager.Push(effect);
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
                if (hits[0].TryGetComponent(out StairTrigger stair))
                {
                    Vector3Int teleportPos = new Vector3Int(_gridObject.CurrentGridPosition.x, stair.GetTargetY(), _gridObject.CurrentGridPosition.z);

                    Bus<CommandEvent>.Raise(new CommandEvent(new StairCommand(
                        this, _gridObject.CurrentGridPosition, teleportPos, dir)));

                    return true;
                }
            }

            return false;
        }

        public void TeleportToFloor(Vector3Int targetPos, Vector3Int dir)
        {
            Vector3Int oldPos = _gridObject.CurrentGridPosition;

            Bus<FloorEvent>.Raise(new FloorEvent(targetPos.y > oldPos.y ? 1 : -1));

            gridService.UpdateObjectPosition(_gridObject, oldPos, targetPos);

            float targetWorldY = targetPos.y;
            Vector3 finalWorldPos = new Vector3(targetPos.x, targetWorldY, targetPos.z);
            transform.position = finalWorldPos;

            Vector3Int effectiveDir = dir;
            if (dir.y < 0)
            {
                effectiveDir.x *= -1;
                effectiveDir.z *= -1;
            }

            if (gridService.CanMoveTo(targetPos, effectiveDir, out Vector3Int finalMovePos))
            {
                gridService.UpdateObjectPosition(_gridObject, targetPos, finalMovePos);

                float finalWorldY = finalMovePos.y;
                Vector3 finalFinalWorldPos = new Vector3(finalMovePos.x, finalWorldY, finalMovePos.z);
                transform.position = finalFinalWorldPos;
            }
        }

        #endregion

        public void PlayDeathSound()
        {
            BroAudio.Play(deathSound);
        }
    }
}