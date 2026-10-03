using System;
using System.Collections.Generic;
using System.Numerics;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Client.ResourceManagement;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Network.Messages;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.Log;
using Direction = Robust.Shared.Maths.Direction;
using Robust.Shared.Map.Components;
using System.Linq;

namespace Robust.Client.Placement
{
    public sealed partial class PlacementManager : IPlacementManager, IDisposable, IEntityEventSubscriber
    {
        [Dependency] private readonly ILogManager _logManager = default!;
        [Dependency] private readonly IClientNetManager _networkManager = default!;
        [Dependency] internal readonly IPlayerManager PlayerManager = default!;
        [Dependency] internal readonly IResourceCache ResourceCache = default!;
        [Dependency] private readonly IReflectionManager _reflectionManager = default!;
        [Dependency] private readonly IMapManager _mapManager = default!;
        [Dependency] private readonly IGameTiming _time = default!;
        [Dependency] private readonly IEyeManager _eyeManager = default!;
        [Dependency] internal readonly IInputManager InputManager = default!;
        [Dependency] private readonly IEntitySystemManager _entitySystemManager = default!;
        [Dependency] private readonly IEntityManager _entityManager = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IBaseClient _baseClient = default!;
        [Dependency] private readonly IOverlayManager _overlayManager = default!;
        [Dependency] internal readonly IClyde Clyde = default!;

        private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";

        public IEntityManager EntityManager => _entityManager;
        public IEyeManager EyeManager => _eyeManager;
        public IMapManager MapManager => _mapManager;

        private ISawmill _sawmill = default!;

        private SharedMapSystem Maps => EntityManager.System<SharedMapSystem>();
        private SharedTransformSystem XformSystem => EntityManager.System<SharedTransformSystem>();
        private SpriteSystem Sprite => EntityManager.System<SpriteSystem>();

        /// <summary>
        ///         How long a requested tile change may stay pending before it is discarded.
        /// </summary>
        private static readonly TimeSpan PendingTileTimeout = TimeSpan.FromSeconds(2.0);

        /// <summary>
        /// Maps the name of each placement mode to the type that implements it.
        /// </summary>
        private readonly Dictionary<string, Type> _modeDictionary = new();
        private readonly List<Tuple<EntityCoordinates, TimeSpan>> _pendingTileChanges = new();

        /// <summary>
        ///     Set while the place key is held down; asks <see cref="FrameUpdate"/> and the key-release handler
        ///     to act on a placement request.
        /// </summary>
        private bool _placenextframe;

        // Massive hack to avoid creating a billion grids for now.
        private bool _gridFrameBuffer;

        /// <summary>
        /// How the active mode places things: one at a time, along a line or over a grid, as far as the mode allows.
        /// </summary>
        public PlacementTypes PlacementType { get; set; }

        /// <summary>
        /// The point where a line or grid placement begins.
        /// </summary>
        public EntityCoordinates StartPoint { get; set; }

        /// <summary>
        ///     True while the manager is in a mode that accepts placement or eraser input.
        ///     Assigning the value also switches the input context, unless the current permission explicitly
        ///     opts out of the editor context.
        /// </summary>
        public bool IsActive
        {
            get => _isActive;
            private set
            {
                _isActive = value;

                // An explicit "false" means the placement does not want the editor context. Null does not count.
                if (CurrentPermission != null && CurrentPermission.UseEditorContext == false)
                    return;

                SwitchEditorContext(value);
            }
        }

        /// <summary>
        /// True when a click deletes the entity under the cursor instead of placing a new one.
        /// </summary>
        public bool Eraser { get; private set; }

        public bool Replacement { get; set; } = true;

        /// <summary>
        /// Holds the selection rectangle for the eraser
        /// </summary>
        public Box2? EraserRect { get; set; }

        /// <summary>
        /// Drawing shader for drawing without being affected by lighting
        /// </summary>
        private ShaderInstance? _drawingShader { get; set; }

        /// <summary>
        /// The entity for placement overlay.
        /// Colour of this gets swapped around in PlacementMode.
        /// This entity needs to stay in nullspace.
        /// </summary>
        public EntityUid? CurrentPlacementOverlayEntity { get; set; }

        /// <summary>
        /// A BAD way to explicitly control the icons used!!!
        /// Need to fix Content for this
        /// </summary>
        public List<IDirectionalTextureProvider>? CurrentTextures {
            set {
                PreparePlacementTexList(value, !Hijack?.CanRotate ?? value != null, null);
            }
        }

        /// <summary>
        /// The placement mode that is active right now, or null when nothing is being placed.
        /// </summary>
        public PlacementMode? CurrentMode { get; set; }

        public PlacementInformation? CurrentPermission { get; set; }

        public PlacementHijack? Hijack { get; private set; }

        private EntityPrototype? _currentPrototype;

        /// <summary>
        ///     The entity prototype that will be spawned when the player confirms a placement.
        ///     Setting a non-null value adopts that prototype's placement offset. Every assignment resets
        ///     the collider box to an empty box at the origin.
        /// </summary>
        public EntityPrototype? CurrentPrototype
        {
            get => _currentPrototype;
            set
            {
                _currentPrototype = value;

                if (value != null)
                    PlacementOffset = value.PlacementOffset;

                _colliderAABB = new Box2(0f, 0f, 0f, 0f);
            }
        }

        public Vector2i PlacementOffset { get; set; }


        private Box2 _colliderAABB = new(0f, 0f, 0f, 0f);

        /// <summary>
        /// Bounds that the collision checks of certain placement modes are tested against.
        /// </summary>
        public Box2 ColliderAABB
        {
            get => _colliderAABB;
            set => _colliderAABB = value;
        }

        private Direction _direction = Direction.South;

        private bool _mirrored;

        public bool Mirrored
        {
            get => _mirrored;
            set
            {
                _mirrored = value;
                MirroredChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc />
        public Direction Direction
        {
            get => _direction;
            set
            {
                _direction = value;
                DirectionChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private string[]? _allModeNames;
        public string[] AllModeNames
        {
            get
            {
                return _allModeNames ??= [IPlacementManager.DefaultModeName, .. _modeDictionary.Keys.Order()];
            }
        }

        /// <inheritdoc />
        public event EventHandler? DirectionChanged;

        /// <inheritdoc />
        public event EventHandler? MirroredChanged;

        private PlacementOverlay _drawOverlay = default!;
        private bool _isActive;

        /// <summary>
        ///     Sets up shaders, logging, net message handling, the placement mode registry, tile change tracking
        ///     and the overlay. Input bindings are not installed here; they are installed once the local player
        ///     joins a server.
        /// </summary>
        public void Initialize()
        {
            _drawingShader = _prototypeManager.Index(UnshadedShader).Instance();
            _sawmill = _logManager.GetSawmill("placement");

            _networkManager.RegisterNetMessage<MsgPlacement>(HandlePlacementMessage);

            _modeDictionary.Clear();
            foreach (var type in _reflectionManager.GetAllChildren<PlacementMode>())
            {
                _modeDictionary.Add(type.Name, type);
            }

            EntityManager.EventBus.SubscribeEvent<TileChangedEvent>(EventSource.Local, this, HandleTileChanged);

            _drawOverlay = new PlacementOverlay(this);
            _overlayManager.AddOverlay(_drawOverlay);

            _baseClient.PlayerJoinedServer += (_, _) => SetupInput();
            _baseClient.PlayerLeaveServer += (_, _) => TearDownInput();
        }

        /// <summary>
        ///     Installs the key bindings used for placing, erasing, rotating and cancelling, and starts listening
        ///     for the local player detaching. Undone by <see cref="TearDownInput"/>.
        /// </summary>
        private void SetupInput()
        {
            CommandBinds.Builder
                .Bind(EngineKeyFunctions.EditorLinePlace, InputCmdHandler.FromDelegate(session =>
                {
                    if (IsActive && !Eraser)
                        ActivateLineMode();
                }))
                .Bind(EngineKeyFunctions.EditorGridPlace, InputCmdHandler.FromDelegate(session =>
                {
                    if (!IsActive)
                        return;

                    if (Eraser)
                        EraseRectMode();
                    else
                        ActivateGridMode();
                }))
                .Bind(EngineKeyFunctions.EditorPlaceObject, new PointerStateInputCmdHandler(
                    (session, netCoords, nent) =>
                    {
                        if (!IsActive)
                            return false;

                        if (EraserRect.HasValue)
                        {
                            HandleRectDeletion(StartPoint, EraserRect.Value);
                            EraserRect = null;
                            return true;
                        }

                        if (Eraser)
                        {
                            if (HandleDeletion(netCoords))
                                return true;

                            if (nent == EntityUid.Invalid)
                                return false;

                            HandleDeletion(nent);
                        }
                        else
                        {
                            _placenextframe = true;
                        }

                        return true;
                    },
                    (session, coords, uid) =>
                    {
                        if (!IsActive || Eraser || !_placenextframe)
                            return false;

                        // Tiles are placed every frame while the key is held; only entities are placed on release.
                        if (!CurrentPermission!.IsTile)
                            HandlePlacement();

                        _gridFrameBuffer = false;
                        _placenextframe = false;
                        return true;
                    },
                    true))
                .Bind(EngineKeyFunctions.EditorRotateObject, InputCmdHandler.FromDelegate(session =>
                {
                    if (IsActive && !Eraser)
                        Rotate();
                }))
                .Bind(EngineKeyFunctions.EditorCancelPlace, InputCmdHandler.FromDelegate(session =>
                {
                    if (!IsActive)
                        return;

                    // Cancelling a line or grid selection keeps the placement itself alive.
                    if (DeactivateSpecialPlacement())
                        return;

                    Clear();
                }, outsidePrediction: true))
                .Register<PlacementManager>();

            PlayerManager.LocalPlayerDetached += OnDetached;
        }

        private void TearDownInput()
        {
            CommandBinds.Unregister<PlacementManager>();
            PlayerManager.LocalPlayerDetached -= OnDetached;
        }

        private void OnDetached(EntityUid obj)
        {
            Clear();
        }

        /// <summary>
        ///     Switches the active input context: the editor context when enabling, otherwise the context that
        ///     follows the controlled entity.
        /// </summary>
        /// <param name="enabled">True to use the editor context, false to restore the entity context.</param>
        private void SwitchEditorContext(bool enabled)
        {
            if (enabled)
            {
                InputManager.Contexts.SetActiveContext("editor");
            }
            else
            {
                _entitySystemManager.GetEntitySystem<InputSystem>().SetEntityContextActive();
            }
        }

        public void Dispose()
        {
            _drawOverlay?.Dispose();
        }

        /// <summary>
        ///     Dispatches a placement message received from the server. Only start and cancel are handled.
        /// </summary>
        /// <param name="msg">The incoming message.</param>
        private void HandlePlacementMessage(MsgPlacement msg)
        {
            switch (msg.PlaceType)
            {
                case PlacementManagerMessage.StartPlacement:
                    HandleStartPlacement(msg);
                    break;

                case PlacementManagerMessage.CancelPlacement:
                    Clear();
                    break;
            }
        }

        private void HandleTileChanged(ref TileChangedEvent args)
        {
            foreach (var change in args.Changes)
            {
                var coords = Maps.GridTileToLocal(
                args.Entity,
                args.Entity.Comp,
                change.GridIndices);

                _pendingTileChanges.RemoveAll(c => c.Item1 == coords);
            }
        }

        /// <inheritdoc />
        public event EventHandler? PlacementChanged;

        public void Clear()
        {
            ClearWithoutDeactivation();
            IsActive = false;
        }

        /// <summary>
        ///     Resets all placement state but leaves <see cref="IsActive"/> untouched.
        ///     <see cref="PlacementChanged"/> is raised first, so listeners still see the old state.
        /// </summary>
        private void ClearWithoutDeactivation()
        {
            PlacementChanged?.Invoke(this, EventArgs.Empty);

            Hijack = null;
            EnsureNoPlacementOverlayEntity();
            CurrentPrototype = null;
            CurrentPermission = null;
            CurrentMode = null;
            DeactivateSpecialPlacement();
            _placenextframe = false;
            Eraser = false;
            EraserRect = null;
            PlacementOffset = Vector2i.Zero;
        }

        /// <summary>
        ///     Turns the placement a quarter turn clockwise. Does nothing if the active hijack forbids rotation
        ///     or if the current direction is not a cardinal one.
        /// </summary>
        public void Rotate()
        {
            if (Hijack != null && !Hijack.CanRotate)
                return;

            switch (Direction)
            {
                case Direction.North:
                    Direction = Direction.East;
                    break;
                case Direction.East:
                    Direction = Direction.South;
                    break;
                case Direction.South:
                    Direction = Direction.West;
                    break;
                case Direction.West:
                    Direction = Direction.North;
                    break;
            }
        }

        /// <summary>
        ///     Sends placement requests for the current selection: a single position, a line or a grid.
        ///     Line and grid selections are ended afterwards.
        /// </summary>
        public void HandlePlacement()
        {
            if (!IsActive || Eraser)
                return;

            switch (PlacementType)
            {
                case PlacementTypes.None:
                    RequestPlacement(CurrentMode!.MouseCoords);
                    break;

                case PlacementTypes.Line:
                    foreach (var coordinates in CurrentMode!.LineCoordinates())
                    {
                        RequestPlacement(coordinates);
                    }

                    DeactivateSpecialPlacement();
                    break;

                case PlacementTypes.Grid:
                    // Stops continuous tile placement for the remainder of this click.
                    _gridFrameBuffer = true;
                    foreach (var coordinates in CurrentMode!.GridCoordinates())
                    {
                        RequestPlacement(coordinates);
                    }

                    DeactivateSpecialPlacement();
                    break;
            }
        }

        public bool HandleDeletion(EntityCoordinates coordinates)
        {
            if (!IsActive || !Eraser) return false;
            if (Hijack != null)
                return Hijack.HijackDeletion(coordinates);

            return false;
        }

        /// <summary>
        ///     Asks the server to delete an entity. Only acts in eraser mode, and a hijack may consume the
        ///     request first.
        /// </summary>
        /// <param name="entity">The entity to delete.</param>
        public void HandleDeletion(EntityUid entity)
        {
            if (!IsActive || !Eraser)
                return;

            if (Hijack != null && Hijack.HijackDeletion(entity))
                return;

            var message = new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.RequestEntRemove,
                EntityUid = EntityManager.GetNetEntity(entity),
            };
            _networkManager.ClientSendMessage(message);
        }

        public void HandleRectDeletion(EntityCoordinates start, Box2 rect)
        {
            var msg = new MsgPlacement();
            msg.PlaceType = PlacementManagerMessage.RequestRectRemove;
            msg.NetCoordinates = new NetCoordinates(EntityManager.GetNetEntity(StartPoint.EntityId), rect.BottomLeft);
            msg.RectSize = rect.Size;
            _networkManager.ClientSendMessage(msg);
        }

        /// <summary>
        ///     Turns the eraser on when nothing is active; in every other case cancels the current mode.
        /// </summary>
        public void ToggleEraser()
        {
            if (!Eraser && !IsActive)
            {
                IsActive = true;
                Eraser = true;
            }
            else
            {
                Clear();
            }
        }

        /// <summary>
        ///     Like <see cref="ToggleEraser"/>, but installs a hijack that can intercept erase requests.
        ///     <see cref="PlacementHijack.StartHijack"/> is not called.
        /// </summary>
        /// <param name="hijack">The hijack to use; ignored when the eraser is switched off.</param>
        public void ToggleEraserHijacked(PlacementHijack hijack)
        {
            if (!Eraser && !IsActive)
            {
                IsActive = true;
                Eraser = true;
                Hijack = hijack;
            }
            else
            {
                Clear();
            }
        }

        public void BeginPlacing(PlacementInformation info, PlacementHijack? hijack = null)
        {
            BeginHijackedPlacing(info, hijack);
        }

        /// <summary>
        ///     Starts a placement described by <paramref name="info"/>, optionally under the control of a hijack.
        ///     Any earlier placement state is wiped first. If the requested mode does not exist the manager is
        ///     cleared and deactivated.
        /// </summary>
        /// <param name="info">Describes what to place and which placement mode to use.</param>
        /// <param name="hijack">Optional hijack that takes over placement requests.</param>
        public void BeginHijackedPlacing(PlacementInformation info, PlacementHijack? hijack = null)
        {
            ClearWithoutDeactivation();

            var option = info.PlacementOption;
            if (option == null || !_modeDictionary.TryGetValue(option, out var modeType))
            {
                _sawmill.Log(LogLevel.Warning, $"Invalid placement mode `{option}`");
                Clear();
                return;
            }

            CurrentPermission = info;
            CurrentMode = (PlacementMode) Activator.CreateInstance(modeType, this)!;

            if (hijack != null)
            {
                Hijack = hijack;
                Hijack.StartHijack(this);
                IsActive = true;
                return;
            }

            if (info.IsTile)
                PreparePlacementTile();
            else
                PreparePlacement(info.EntityType!);
        }

        /// <summary>
        ///     Gets the mouse position if a placement is in progress and the local player is on a real map.
        /// </summary>
        /// <param name="coordinates">The mouse position, or default when there is no placement context.</param>
        /// <returns>True if the placement context exists and the mouse position is valid.</returns>
        private bool CurrentMousePosition(out ScreenCoordinates coordinates)
        {
            var map = MapId.Nullspace;
            if (EntityManager.TryGetComponent(PlayerManager.LocalEntity, out TransformComponent? xform))
                map = xform.MapID;

            if (map == MapId.Nullspace || CurrentPermission == null || CurrentMode == null)
            {
                coordinates = default;
                return false;
            }

            coordinates = InputManager.MouseScreenPosition;
            return coordinates.IsValid;
        }

        private bool CurrentEraserMouseCoordinates(out EntityCoordinates coordinates)
        {
            var ent = PlayerManager.LocalEntity ?? EntityUid.Invalid;
            if (ent == EntityUid.Invalid)
            {
                coordinates = new EntityCoordinates();
                return false;
            }
            else
            {
                var mousePosition = EyeManager.PixelToMap(InputManager.MouseScreenPosition);
                var map = EntityManager.GetComponent<TransformComponent>(ent).MapID;
                if (map == MapId.Nullspace || !Eraser || mousePosition.MapId == MapId.Nullspace)
                {
                    coordinates = new EntityCoordinates();
                    return false;
                }
                coordinates = XformSystem.ToCoordinates(mousePosition);
                return true;
            }
        }

        /// <inheritdoc />
        public void FrameUpdate(FrameEventArgs e)
        {
            if (!CurrentMousePosition(out var mouseScreen))
            {
                // Without a placement context only the eraser rectangle needs tracking.
                if (EraserRect.HasValue)
                {
                    if (!CurrentEraserMouseCoordinates(out var end))
                        return;

                    var left = Math.Min(StartPoint.X, end.X);
                    var right = Math.Max(StartPoint.X, end.X);
                    var bottom = Math.Min(StartPoint.Y, end.Y);
                    var top = Math.Max(StartPoint.Y, end.Y);
                    EraserRect = new Box2(left, bottom, right, top);
                }

                return;
            }

            CurrentMode!.AlignPlacementMode(mouseScreen);

            // Drop pending tile changes that were never confirmed.
            var now = _time.RealTime;
            _pendingTileChanges.RemoveAll(change => change.Item2 < now);

            // Entities are placed on key release; tiles are placed every frame while the key is held.
            if (_placenextframe && CurrentPermission!.IsTile && !_gridFrameBuffer)
                HandlePlacement();
        }

        /// <summary>
        ///     Starts a line selection at the current mouse position, if the mode supports lines.
        /// </summary>
        private void ActivateLineMode()
        {
            if (!CurrentMode!.HasLineMode)
                return;

            if (!CurrentMousePosition(out var mouse))
                return;

            CurrentMode.AlignPlacementMode(mouse);
            StartPoint = CurrentMode.MouseCoords;
            PlacementType = PlacementTypes.Line;
        }

        /// <summary>
        ///     Starts a grid selection at the current mouse position, if the mode supports grids.
        /// </summary>
        private void ActivateGridMode()
        {
            if (!CurrentMode!.HasGridMode)
                return;

            if (!CurrentMousePosition(out var mouse))
                return;

            CurrentMode.AlignPlacementMode(mouse);
            StartPoint = CurrentMode.MouseCoords;
            PlacementType = PlacementTypes.Grid;
        }

        private void EraseRectMode()
        {
            if (!CurrentEraserMouseCoordinates(out EntityCoordinates coordinates))
                return;

            StartPoint = coordinates;
            EraserRect = new Box2(coordinates.Position, Vector2.Zero);
        }

        /// <summary>
        ///     Returns to single-position placement.
        /// </summary>
        /// <returns>True if a line or grid selection was active and has been ended.</returns>
        private bool DeactivateSpecialPlacement()
        {
            if (PlacementType == PlacementTypes.None)
                return false;

            PlacementType = PlacementTypes.None;
            return true;
        }

        /// <summary>
        ///     Draws the placement visuals: the eraser rectangle, the mode's own graphics and the range ring.
        ///     Called by <see cref="PlacementOverlay"/>.
        /// </summary>
        /// <param name="args">Draw arguments from the overlay.</param>
        private void Render(in OverlayDrawArgs args)
        {
            if (CurrentMode == null || !IsActive)
            {
                if (EraserRect.HasValue)
                {
                    args.WorldHandle.UseShader(_drawingShader);
                    args.WorldHandle.DrawRect(EraserRect.Value, new Color(255, 0, 0, 50));
                    args.WorldHandle.UseShader(null);
                }

                return;
            }

            CurrentMode.Render(args);

            if (CurrentPermission == null || CurrentPermission.Range <= 0 || !CurrentMode.RangeRequired)
                return;

            if (PlayerManager.LocalEntity is not { } controlled || !controlled.Valid)
                return;

            var worldPos = XformSystem.GetWorldPosition(controlled);
            args.WorldHandle.DrawCircle(worldPos, CurrentPermission.Range, new Color(1f, 1f, 1f, 0.25f));
        }

        /// <summary>
        ///     Begins a placement as requested by the server.
        /// </summary>
        /// <param name="msg">The start-placement message.</param>
        private void HandleStartPlacement(MsgPlacement msg)
        {
            CurrentPermission = new PlacementInformation
            {
                Range = msg.Range,
                IsTile = msg.IsTile,
            };

            // Holds either a tile type name or an entity prototype id, depending on IsTile.
            CurrentPermission.EntityType = msg.ObjType;
            CurrentPermission.PlacementOption = msg.AlignOption;

            BeginPlacing(CurrentPermission);
        }

        private void EnsureNoPlacementOverlayEntity()
        {
            if (CurrentPlacementOverlayEntity == null)
                return;

            if (!EntityManager.Deleted(CurrentPlacementOverlayEntity))
                EntityManager.DeleteEntity(CurrentPlacementOverlayEntity.Value);

            CurrentPlacementOverlayEntity = null;
        }

        private Entity<SpriteComponent> SetupPlacementOverlayEntity()
        {
            EnsureNoPlacementOverlayEntity();
            CurrentPlacementOverlayEntity = EntityManager.SpawnEntity(null, MapCoordinates.Nullspace);
            return (CurrentPlacementOverlayEntity.Value, EntityManager.EnsureComponent<SpriteComponent>(CurrentPlacementOverlayEntity.Value));
        }

        /// <summary>
        ///     Prepares entity placement: adopts the prototype, activates the manager and spawns a ghost entity
        ///     in nullspace to preview the placement.
        /// </summary>
        /// <param name="templateName">Id of the entity prototype to place.</param>
        private void PreparePlacement(string templateName)
        {
            EnsureNoPlacementOverlayEntity();

            var prototype = _prototypeManager.Index<EntityPrototype>(templateName);
            CurrentPrototype = prototype;
            IsActive = true;

            var ghost = EntityManager.SpawnEntity(templateName, MapCoordinates.Nullspace);
            CurrentPlacementOverlayEntity = ghost;
            EntityManager.RunMapInit(ghost, EntityManager.GetComponent<MetaDataComponent>(ghost));
        }

        public void PreparePlacementSprite(Entity<SpriteComponent> sprite)
        {
            var sc = SetupPlacementOverlayEntity();
            Sprite.CopySprite(sprite.AsNullable(), sc.AsNullable());
        }

        [Obsolete("Use the Entity<SpriteComponent> overload.")]
        public void PreparePlacementSprite(SpriteComponent sprite)
        {
            PreparePlacementSprite((sprite.Owner, sprite));
        }

        public void PreparePlacementTexList(List<IDirectionalTextureProvider>? texs, bool noRot, EntityPrototype? prototype)
        {
            var sc = SetupPlacementOverlayEntity();
            if (texs != null)
            {
                // This one covers most cases (including Construction)
                foreach (var v in texs)
                {
                    if (v is RSI.State st)
                    {
                        Sprite.AddRsiLayer(sc.AsNullable(), st.StateId, st.RSI);
                    }
                    else
                    {
                        // Fallback
                        Sprite.AddTextureLayer(sc.AsNullable(), v.Default);
                    }
                }
            }
            else
            {
                Sprite.AddTextureLayer(sc.AsNullable(), new ResPath("/Textures/Interface/tilebuildoverlay.png"));
            }

            sc.Comp.NoRotation = noRot;

            if (prototype != null && prototype.TryGetComponent<SpriteComponent>("Sprite", out var spriteComp))
            {
                Sprite.SetScale(sc.AsNullable(), spriteComp.Scale);
            }

        }

        /// <summary>
        ///     Prepares tile placement: creates a ghost entity showing the tile build overlay texture and
        ///     activates the manager.
        /// </summary>
        private void PreparePlacementTile()
        {
            var sprite = SetupPlacementOverlayEntity();
            Sprite.AddTextureLayer(sprite.AsNullable(), new ResPath("/Textures/Interface/tilebuildoverlay.png"));
            IsActive = true;
        }

        /// <summary>
        ///     Sends a placement request for one position to the server, after local validity, hijack and
        ///     duplicate-tile checks.
        /// </summary>
        /// <param name="coordinates">The position to place at.</param>
        private void RequestPlacement(EntityCoordinates coordinates)
        {
            if (CurrentPermission == null)
                return;

            if (!CurrentMode!.IsValidPosition(coordinates))
                return;

            if (Hijack != null && Hijack.HijackPlacementRequest(coordinates))
                return;

            if (CurrentPermission.IsTile)
            {
                var gridUid = XformSystem.GetGrid(coordinates);
                if (gridUid.HasValue && gridUid.Value.IsValid())
                {
                    var grid = EntityManager.GetComponent<MapGridComponent>(gridUid.Value);
                    var existing = Maps.GetTileRef(gridUid.Value, grid, coordinates).Tile;

                    // Skip the request if the tile already is exactly what we would place.
                    var wantedRotation = Tile.DirectionToByte(Direction) + (Mirrored ? 4 : 0);
                    if (existing.TypeId == CurrentPermission.TileType && existing.RotationMirroring == wantedRotation)
                        return;
                }

                foreach (var pending in _pendingTileChanges)
                {
                    if (pending.Item1 == coordinates)
                        return;
                }

                _pendingTileChanges.Add(Tuple.Create(coordinates, _time.RealTime + PendingTileTimeout));
            }

            var message = new MsgPlacement
            {
                PlaceType = PlacementManagerMessage.RequestPlacement,
                Align = CurrentMode.ModeName,
                IsTile = CurrentPermission.IsTile,
                Replacement = Replacement,
            };

            if (CurrentPermission.IsTile)
            {
                message.TileType = CurrentPermission.TileType;
                message.Mirrored = Mirrored;
            }
            else
            {
                message.EntityTemplateName = CurrentPermission.EntityType;
            }

            message.NetCoordinates = EntityManager.GetNetCoordinates(coordinates);
            message.DirRcv = Direction;

            _networkManager.ClientSendMessage(message);
        }

        public enum PlacementTypes : byte
        {
            None = 0,
            Line = 1,
            Grid = 2
        }
    }
}
